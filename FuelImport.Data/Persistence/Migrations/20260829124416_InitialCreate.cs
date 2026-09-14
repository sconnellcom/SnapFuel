using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuelEvents",
                columns: table => new
                {
                    FuelEventId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: true),
                    PumpSourceImageId = table.Column<int>(type: "INTEGER", nullable: true),
                    DashSourceImageId = table.Column<int>(type: "INTEGER", nullable: true),
                    EventTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EventTimeLocal = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Gallons = table.Column<decimal>(type: "TEXT", nullable: true),
                    TotalPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    PricePerGallon = table.Column<decimal>(type: "TEXT", nullable: true),
                    Odometer = table.Column<int>(type: "INTEGER", nullable: true),
                    MilesSincePrevious = table.Column<decimal>(type: "TEXT", nullable: true),
                    EstimatedMpg = table.Column<decimal>(type: "TEXT", nullable: true),
                    IsEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    OverallConfidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    NeedsReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReviewStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelEvents", x => x.FuelEventId);
                });

            migrationBuilder.CreateTable(
                name: "HumanReviews",
                columns: table => new
                {
                    HumanReviewId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FuelEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewSystem = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerName = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewerAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginalValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CorrectedValuesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HumanReviews", x => x.HumanReviewId);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RootFolder = table.Column<string>(type: "TEXT", nullable: false),
                    IsDryRun = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.ImportBatchId);
                });

            migrationBuilder.CreateTable(
                name: "OcrResults",
                columns: table => new
                {
                    OcrResultId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceImageId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderOperation = table.Column<string>(type: "TEXT", nullable: false),
                    RawResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedText = table.Column<string>(type: "TEXT", nullable: false),
                    ParsedFieldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrResults", x => x.OcrResultId);
                });

            migrationBuilder.CreateTable(
                name: "SourceImages",
                columns: table => new
                {
                    SourceImageId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CapturedAtLocal = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageTypeCandidate = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageTypeConfidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    RawMetadataJson = table.Column<string>(type: "TEXT", nullable: false),
                    RawClassificationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceImages", x => x.SourceImageId);
                });

            migrationBuilder.CreateTable(
                name: "ValidationIssues",
                columns: table => new
                {
                    ValidationIssueId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FuelEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueType = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestedValue = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationIssues", x => x.ValidationIssueId);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    VehicleId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DashboardLabel = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedTankGallonsMin = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpectedTankGallonsMax = table.Column<decimal>(type: "TEXT", nullable: false),
                    OdometerMinKnown = table.Column<int>(type: "INTEGER", nullable: true),
                    OdometerMaxKnown = table.Column<int>(type: "INTEGER", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.VehicleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelEvents_PumpSourceImageId_DashSourceImageId",
                table: "FuelEvents",
                columns: new[] { "PumpSourceImageId", "DashSourceImageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceImages_FileHash",
                table: "SourceImages",
                column: "FileHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelEvents");

            migrationBuilder.DropTable(
                name: "HumanReviews");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "OcrResults");

            migrationBuilder.DropTable(
                name: "SourceImages");

            migrationBuilder.DropTable(
                name: "ValidationIssues");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
