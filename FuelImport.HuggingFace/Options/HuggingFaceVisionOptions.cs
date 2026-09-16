namespace FuelImport.HuggingFace.Options;

public class HuggingFaceVisionOptions
{
    public bool Enabled { get; set; }

    /// <summary>Hugging Face access token. Supply it via user-secrets or the HuggingFaceVision__ApiToken environment variable, not appsettings.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>OpenAI-compatible chat completions endpoint served by the Hugging Face inference router.</summary>
    public string Endpoint { get; set; } = "https://router.huggingface.co/v1/chat/completions";

    public string Model { get; set; } = "Qwen/Qwen3-VL-30B-A3B-Instruct";

    public int TimeoutSeconds { get; set; } = 120;

    public int MaxTokens { get; set; } = 400;

    public double Temperature { get; set; }

    /// <summary>Images larger than this are skipped instead of being uploaded.</summary>
    public int MaxImageBytes { get; set; } = 12 * 1024 * 1024;
}
