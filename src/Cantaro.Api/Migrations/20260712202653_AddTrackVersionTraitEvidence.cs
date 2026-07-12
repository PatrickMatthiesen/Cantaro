using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackVersionTraitEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackVersionTraits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackId = table.Column<Guid>(type: "uuid", nullable: false),
                    TraitKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    EvidenceSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceIdentity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EvidenceMethod = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    MethodVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssertedByType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AssertedById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    RevokedById = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SupersedesTraitId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackVersionTraits", x => x.Id);
                    table.CheckConstraint("CK_TrackVersionTraits_Confidence", "CAST(\"Confidence\" AS REAL) >= 0 AND CAST(\"Confidence\" AS REAL) <= 1");
                    table.CheckConstraint("CK_TrackVersionTraits_Revocation", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_TrackVersionTraits_RevocationAudit", "(\"RevokedAt\" IS NULL AND \"RevokedByType\" IS NULL AND \"RevokedById\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"RevokedAt\" IS NOT NULL AND \"RevokedByType\" IS NOT NULL AND \"RevokedById\" IS NOT NULL AND \"RevocationReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TrackVersionTraits_TrackVersionTraits_SupersedesTraitId",
                        column: x => x.SupersedesTraitId,
                        principalTable: "TrackVersionTraits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackVersionTraits_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackVersionTraits_SupersedesTraitId",
                table: "TrackVersionTraits",
                column: "SupersedesTraitId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackVersionTraits_Track_Active_Trait",
                table: "TrackVersionTraits",
                columns: new[] { "TrackId", "RevokedAt", "TraitKey" });

            migrationBuilder.CreateIndex(
                name: "UX_TrackVersionTraits_ActiveEvidence",
                table: "TrackVersionTraits",
                columns: new[] { "TrackId", "TraitKey", "EvidenceSource", "EvidenceIdentity", "EvidenceMethod" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackVersionTraits");
        }
    }
}
