using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualEntryWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DashboardNotes",
                table: "FuelEvents",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FuelNotes",
                table: "FuelEvents",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationName",
                table: "FuelEvents",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FuelEventSourceImages",
                columns: table => new
                {
                    FuelEventSourceImageId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FuelEventId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceImageId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelEventSourceImages", x => x.FuelEventSourceImageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelEventSourceImages_FuelEventId_SourceImageId",
                table: "FuelEventSourceImages",
                columns: new[] { "FuelEventId", "SourceImageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelEventSourceImages");

            migrationBuilder.DropColumn(
                name: "DashboardNotes",
                table: "FuelEvents");

            migrationBuilder.DropColumn(
                name: "FuelNotes",
                table: "FuelEvents");

            migrationBuilder.DropColumn(
                name: "LocationName",
                table: "FuelEvents");
        }
    }
}
