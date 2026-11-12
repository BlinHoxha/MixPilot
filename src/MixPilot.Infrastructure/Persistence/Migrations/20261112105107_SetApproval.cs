using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MixPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SetApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "SetVersion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedVersion",
                table: "Sets",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "SetVersion");

            migrationBuilder.DropColumn(
                name: "ApprovedVersion",
                table: "Sets");
        }
    }
}
