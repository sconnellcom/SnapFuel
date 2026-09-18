using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnomalyAcknowledged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AnomalyAcknowledged",
                table: "FuelEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnomalyAcknowledged",
                table: "FuelEvents");
        }
    }
}
