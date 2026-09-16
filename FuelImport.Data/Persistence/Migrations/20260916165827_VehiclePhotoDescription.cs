using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VehiclePhotoDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoDescription",
                table: "Vehicles",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoDescription",
                table: "Vehicles");
        }
    }
}
