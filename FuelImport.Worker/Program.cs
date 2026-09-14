using FuelImport.Core.Interfaces;
using FuelImport.Data.Persistence;
using FuelImport.Worker.Options;
using FuelImport.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection("Import"));

builder.Services.AddDbContext<FuelImportDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("FuelImport") ?? "Data Source=fuelimport.db"));

builder.Services.AddScoped<IImageMetadataExtractor, FileImageMetadataExtractor>();
builder.Services.AddScoped<IImageClassifier, SimpleImageClassifier>();
builder.Services.AddHostedService<ImportWorker>();

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FuelImportDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
}

await host.RunAsync();
