using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehicleThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxGallonsPerFillUp",
                table: "Vehicles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxMpg",
                table: "Vehicles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxGallonsPerFillUp",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "MaxMpg",
                table: "Vehicles");
        }
    }
}
