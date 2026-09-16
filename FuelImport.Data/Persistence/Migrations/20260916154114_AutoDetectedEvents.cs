using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelImport.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AutoDetectedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetectedAtUtc",
                table: "FuelEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectionDetailsJson",
                table: "FuelEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntrySource",
                table: "FuelEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedAtUtc",
                table: "FuelEvents");

            migrationBuilder.DropColumn(
                name: "DetectionDetailsJson",
                table: "FuelEvents");

            migrationBuilder.DropColumn(
                name: "EntrySource",
                table: "FuelEvents");
        }
    }
}
