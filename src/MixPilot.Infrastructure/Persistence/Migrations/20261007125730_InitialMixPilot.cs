using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MixPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMixPilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OwnerKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PreferredGenre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MinBpm = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    MaxBpm = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    PreferVocals = table.Column<bool>(type: "boolean", nullable: true),
                    OwnerKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OwnerKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Artist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IdentityKey = table.Column<string>(type: "character varying(710)", maxLength: 710, nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Bpm = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    MusicalKey = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Energy = table.Column<int>(type: "integer", nullable: true),
                    Genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Mood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HasVocals = table.Column<bool>(type: "boolean", nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    AuthorizationDeclaredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Rights = table.Column<string>(type: "text", nullable: false),
                    OwnerKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SetVersion",
                columns: table => new
                {
                    SetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlanJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetVersion", x => new { x.SetId, x.Number });
                    table.ForeignKey(
                        name: "FK_SetVersion_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionTrack",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionTrack", x => new { x.CollectionId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_CollectionTrack_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionTrack_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_OwnerKey_CreatedAt",
                table: "Collections",
                columns: new[] { "OwnerKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionTrack_TrackId",
                table: "CollectionTrack",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_OwnerKey",
                table: "Profiles",
                column: "OwnerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_OwnerKey_CreatedAt",
                table: "Profiles",
                columns: new[] { "OwnerKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sets_OwnerKey_CreatedAt",
                table: "Sets",
                columns: new[] { "OwnerKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_OwnerKey_CreatedAt",
                table: "Tracks",
                columns: new[] { "OwnerKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_OwnerKey_IdentityKey",
                table: "Tracks",
                columns: new[] { "OwnerKey", "IdentityKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionTrack");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "SetVersion");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "Tracks");

            migrationBuilder.DropTable(
                name: "Sets");
        }
    }
}
