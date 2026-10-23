using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileProviderArtists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JoinPhrase",
                table: "TrackArtistCredits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provenance",
                table: "TrackArtistCredits",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArtistProviderLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProfileUrl = table.Column<string>(type: "text", nullable: true),
                    ArtworkUrl = table.Column<string>(type: "text", nullable: true),
                    RawMetadata = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistProviderLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtistProviderLinks_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtistReconciliationConflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Evidence = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtistReconciliationConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtistReconciliationConflicts_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtistProviderLinks_ArtistId_Provider",
                table: "ArtistProviderLinks",
                columns: new[] { "ArtistId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistProviderLinks_Provider_ExternalId",
                table: "ArtistProviderLinks",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtistReconciliationConflicts_TrackId_EvidenceKey",
                table: "ArtistReconciliationConflicts",
                columns: new[] { "TrackId", "EvidenceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtistProviderLinks");

            migrationBuilder.DropTable(
                name: "ArtistReconciliationConflicts");

            migrationBuilder.DropColumn(
                name: "JoinPhrase",
                table: "TrackArtistCredits");

            migrationBuilder.DropColumn(
                name: "Provenance",
                table: "TrackArtistCredits");
        }
    }
}
