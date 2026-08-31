using FuelImport.Aws.Services;
using FuelImport.Aws.Options;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Options;
using FuelImport.Core.Services;
using FuelImport.Data.Persistence;
using FuelImport.Worker.Options;
using FuelImport.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection("Import"));
builder.Services.Configure<ConfidenceOptions>(builder.Configuration.GetSection("Confidence"));
builder.Services.Configure<ValidationOptions>(builder.Configuration.GetSection("Validation"));
builder.Services.Configure<AwsVisionOptions>(builder.Configuration.GetSection("AwsVision"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IOptions<ConfidenceOptions>>().Value);
builder.Services.AddScoped(sp => sp.GetRequiredService<IOptions<ValidationOptions>>().Value);

builder.Services.AddDbContext<FuelImportDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("FuelImport") ?? "Data Source=fuelimport.db"));

builder.Services.AddScoped<IImageMetadataExtractor, FileImageMetadataExtractor>();
builder.Services.AddScoped<IImageClassifier, RekognitionImageClassifier>();
builder.Services.AddScoped<IPumpOcrService, TextractPumpOcrService>();
builder.Services.AddScoped<IDashboardOcrService, RekognitionDashboardOcrService>();
builder.Services.AddScoped<IEventPairingService>(_ => new EventPairingService(TimeSpan.FromMinutes(10)));
builder.Services.AddScoped<IValidationEngine, ValidationEngine>();
builder.Services.AddScoped<IVehicleResolver, SimpleVehicleResolver>();
builder.Services.AddScoped<IConfidenceScorer, ConfidenceScorer>();
builder.Services.AddScoped<A2IReviewRouter>();
builder.Services.AddHostedService<ImportWorker>();

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FuelImportDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
}

await host.RunAsync();
