using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLitersToFuelEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Liters",
                table: "FuelEvents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Liters",
                table: "FuelEvents");
        }
    }
}
