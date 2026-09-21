using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Core.Services;
using FuelImport.HuggingFace.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace FuelImport.HuggingFace.Services;

/// <summary>
/// Sends a photo plus the estimated value windows to a Hugging Face hosted vision-language model
/// through the OpenAI-compatible inference router.
/// </summary>
public class HuggingFaceVisionDetector(
    HttpClient httpClient,
    IOptions<HuggingFaceVisionOptions> options,
    ILogger<HuggingFaceVisionDetector> logger) : IVisionAutoDetector
{
    private readonly HuggingFaceVisionOptions _options = options.Value;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiToken);

    public async Task<ImageDetection> DetectAsync(
        SourceImage image,
        DetectionEstimates estimates,
        IReadOnlyCollection<VehicleHint> vehicleHints,
        CancellationToken cancellationToken = default)
    {
        var detection = new ImageDetection { SourceImageId = image.SourceImageId };

        if (!IsConfigured)
        {
            detection.ErrorMessage = "Hugging Face auto-detect is not configured. Set HuggingFaceVision:Enabled and an API token.";
            return detection;
        }

        if (string.IsNullOrWhiteSpace(image.FilePath) || !File.Exists(image.FilePath))
        {
            detection.ErrorMessage = "The image file is no longer available on disk.";
            return detection;
        }

        string? temporaryUploadPath = null;
        try
        {
            var uploadPath = image.FilePath;
            if (new FileInfo(image.FilePath).Length > _options.ResizeAboveBytes && CanResize(image.FilePath))
            {
                temporaryUploadPath = await CreateResizedUploadAsync(image.FilePath, cancellationToken);
                uploadPath = temporaryUploadPath;
            }

            var fileInfo = new FileInfo(uploadPath);
            if (fileInfo.Length > _options.MaxImageBytes)
            {
                detection.ErrorMessage = $"The upload image is larger than the configured limit of {_options.MaxImageBytes:N0} bytes.";
                return detection;
            }

            var bytes = await File.ReadAllBytesAsync(uploadPath, cancellationToken);
            var dataUri = $"data:{GetMimeType(uploadPath)};base64,{Convert.ToBase64String(bytes)}";
            var prompt = DetectionPromptBuilder.Build(estimates, vehicleHints);

            var payload = new
            {
                model = _options.Model,
                max_tokens = _options.MaxTokens,
                temperature = _options.Temperature,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = dataUri } }
                        }
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var detail = ExtractErrorDetail(body);
                logger.LogWarning(
                    "Hugging Face detection failed for image {SourceImageId} with status {StatusCode}: {Detail}",
                    image.SourceImageId,
                    (int)response.StatusCode,
                    detail);

                detection.ErrorMessage = $"The model endpoint returned HTTP {(int)response.StatusCode}: {detail}";
                return detection;
            }

            var content = ReadMessageContent(body);
            var parsed = DetectionResponseParser.Parse(image.SourceImageId, content);
            return parsed;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            detection.ErrorMessage = "The model request timed out.";
            return detection;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Hugging Face detection request failed for image {SourceImageId}.", image.SourceImageId);
            detection.ErrorMessage = "The model endpoint could not be reached.";
            return detection;
        }
        finally
        {
            if (temporaryUploadPath is not null)
            {
                File.Delete(temporaryUploadPath);
            }
        }
    }

    private static bool CanResize(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" => true,
        _ => false
    };

    private async Task<string> CreateResizedUploadAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"snapfuel-{Guid.NewGuid():N}.jpg");
        try
        {
            using var source = await Image.LoadAsync(sourcePath, cancellationToken);
            source.Mutate(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(_options.MaxUploadImageDimension, _options.MaxUploadImageDimension)
            }));

            await source.SaveAsJpegAsync(temporaryPath, new JpegEncoder
            {
                Quality = _options.UploadJpegQuality
            }, cancellationToken);

            return temporaryPath;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static string ExtractErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no details returned";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : error.TryGetProperty("message", out var nested) ? nested.GetString() : null;

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return Truncate(message);
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body below.
        }

        return Truncate(body);
    }

    private static string Truncate(string value) =>
        value.Length > 300 ? value[..300] : value;

    private static string? ReadMessageContent(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        // Some providers return the assistant message as an array of content parts.
        if (content.ValueKind == JsonValueKind.Array)
        {
            return string.Concat(content
                .EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString()));
        }

        return null;
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".heic" => "image/heic",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };
}
