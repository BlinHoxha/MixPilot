using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MixPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SetGenerationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SetGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RequirementsJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    Prompt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Territory = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResultSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetGenerationJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SetGenerationJobs_OwnerKey_CreatedAt",
                table: "SetGenerationJobs",
                columns: new[] { "OwnerKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SetGenerationJobs_Status_LeaseExpiresAt",
                table: "SetGenerationJobs",
                columns: new[] { "Status", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetGenerationJobs");
        }
    }
}
