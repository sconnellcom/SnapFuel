using FuelImport.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FuelImport.Data.Persistence;

public class FuelImportDbContext(DbContextOptions<FuelImportDbContext> options) : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<SourceImage> SourceImages => Set<SourceImage>();
    public DbSet<FuelEvent> FuelEvents => Set<FuelEvent>();
    public DbSet<FuelEventSourceImage> FuelEventSourceImages => Set<FuelEventSourceImage>();
    public DbSet<OcrResult> OcrResults => Set<OcrResult>();
    public DbSet<ValidationIssue> ValidationIssues => Set<ValidationIssue>();
    public DbSet<HumanReview> HumanReviews => Set<HumanReview>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceImage>(entity =>
        {
            entity.HasKey(x => x.SourceImageId);
            entity.HasIndex(x => x.FileHash).IsUnique();
            entity.Property(x => x.FilePath).HasMaxLength(1024);
            entity.Property(x => x.FileName).HasMaxLength(255);
            entity.Property(x => x.FileHash).HasMaxLength(128);
            entity.Property(x => x.ManualGroupKey).HasMaxLength(128);
        });

        modelBuilder.Entity<FuelEvent>(entity =>
        {
            entity.HasKey(x => x.FuelEventId);
            entity.HasIndex(x => new { x.PumpSourceImageId, x.DashSourceImageId }).IsUnique();
            entity.Property(x => x.LocationName).HasMaxLength(256);
            entity.Property(x => x.Notes).HasMaxLength(2048);
            entity.Property(x => x.ReviewReason).HasMaxLength(512);
        });

        modelBuilder.Entity<FuelEventSourceImage>(entity =>
        {
            entity.HasKey(x => x.FuelEventSourceImageId);
            entity.HasIndex(x => new { x.FuelEventId, x.SourceImageId }).IsUnique();
        });

        modelBuilder.Entity<ValidationIssue>().HasKey(x => x.ValidationIssueId);
        modelBuilder.Entity<OcrResult>().HasKey(x => x.OcrResultId);
        modelBuilder.Entity<HumanReview>().HasKey(x => x.HumanReviewId);
        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.HasKey(x => x.VehicleId);
            entity.Property(x => x.PhotoDescription).HasMaxLength(1024);
        });
        modelBuilder.Entity<ImportBatch>().HasKey(x => x.ImportBatchId);
    }
}
