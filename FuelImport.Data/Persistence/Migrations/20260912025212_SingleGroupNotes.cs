using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SingleGroupNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DashboardNotes",
                table: "FuelEvents");

            migrationBuilder.RenameColumn(
                name: "FuelNotes",
                table: "FuelEvents",
                newName: "Notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "FuelEvents",
                newName: "FuelNotes");

            migrationBuilder.AddColumn<string>(
                name: "DashboardNotes",
                table: "FuelEvents",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }
    }
}
