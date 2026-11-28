using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MixPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Album",
                table: "Tracks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "Tracks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReleaseDate",
                table: "Tracks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subgenre",
                table: "Tracks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Tracks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Album",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Label",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Subgenre",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Tracks");
        }
    }
}
