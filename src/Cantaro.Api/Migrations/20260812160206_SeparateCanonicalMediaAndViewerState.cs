using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCanonicalMediaAndViewerState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cantaro is still in beta and this model intentionally replaces the old
            // provider-owned library rows. There is no compatibility or backfill path:
            // canonical media is rebuilt from providers and viewer state starts clean.
            migrationBuilder.Sql("""
                TRUNCATE TABLE
                    "MediaProviderOperations",
                    "MediaProviderListMemberships",
                    "MediaObservationCandidates",
                    "MediaObservationEpisodeOffsets",
                    "MediaEpisodeProviderIdentities",
                    "MediaObservations",
                    "MediaEpisodes",
                    "MediaLibraryEntries",
                    "MediaProviderLinks",
                    "MediaTitles"
                RESTART IDENTITY CASCADE;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_MediaLibraryEntries_ConnectedServiceAccounts_ConnectedServi~",
                table: "MediaLibraryEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaProviderListMemberships_MediaLibraryEntries_MediaLibra~",
                table: "MediaProviderListMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaProviderOperations_AspNetUsers_UserId",
                table: "MediaProviderOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaProviderOperations_ConnectedServiceAccounts_ConnectedS~",
                table: "MediaProviderOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaProviderOperations_MediaLibraryEntries_MediaLibraryEnt~",
                table: "MediaProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_MediaProviderOperations_ConnectedServiceAccountId",
                table: "MediaProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_MediaProviderOperations_UserId_Provider_CreatedAt",
                table: "MediaProviderOperations");

            migrationBuilder.DropIndex(
                name: "IX_MediaLibraryEntries_ConnectedServiceAccountId",
                table: "MediaLibraryEntries");

            migrationBuilder.DropIndex(
                name: "IX_MediaLibraryEntries_UserId_Provider_ProviderAccountId_Provi~",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "ConnectedServiceAccountId",
                table: "MediaProviderOperations");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "MediaProviderOperations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "MediaProviderOperations");

            migrationBuilder.DropColumn(
                name: "ConnectedServiceAccountId",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "LastRemoteUpdateAt",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "ProviderAccountId",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "ProviderLibraryEntryId",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "ProviderMediaId",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "RawMetadata",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "CanonicalMetadata",
                table: "MediaTitles");

            migrationBuilder.RenameColumn(
                name: "MediaLibraryEntryId",
                table: "MediaProviderOperations",
                newName: "MediaLibraryProviderBindingId");

            migrationBuilder.RenameIndex(
                name: "IX_MediaProviderOperations_MediaLibraryEntryId",
                table: "MediaProviderOperations",
                newName: "IX_MediaProviderOperations_MediaLibraryProviderBindingId");

            migrationBuilder.RenameColumn(
                name: "MediaLibraryEntryId",
                table: "MediaProviderListMemberships",
                newName: "MediaLibraryProviderBindingId");

            migrationBuilder.RenameIndex(
                name: "IX_MediaProviderListMemberships_Name_MediaLibraryEntryId",
                table: "MediaProviderListMemberships",
                newName: "IX_MediaProviderListMemberships_Name_MediaLibraryProviderBindi~");

            migrationBuilder.AddColumn<string>(
                name: "BackgroundUrl",
                table: "MediaTitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "MediaTitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextReleaseAt",
                table: "MediaTitles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextReleaseLabel",
                table: "MediaTitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PosterUrl",
                table: "MediaTitles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleasedCount",
                table: "MediaTitles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaLibraryProviderBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaLibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaProviderLinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedServiceAccountId = table.Column<int>(type: "integer", nullable: true),
                    ProviderAccountId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProviderLibraryEntryId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRemoteUpdateAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawMetadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraryProviderBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaLibraryProviderBindings_ConnectedServiceAccounts_Conne~",
                        column: x => x.ConnectedServiceAccountId,
                        principalTable: "ConnectedServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaLibraryProviderBindings_MediaLibraryEntries_MediaLibra~",
                        column: x => x.MediaLibraryEntryId,
                        principalTable: "MediaLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaLibraryProviderBindings_MediaProviderLinks_MediaProvid~",
                        column: x => x.MediaProviderLinkId,
                        principalTable: "MediaProviderLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_UserId_MediaTitleId",
                table: "MediaLibraryEntries",
                columns: new[] { "UserId", "MediaTitleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryProviderBindings_ConnectedServiceAccountId",
                table: "MediaLibraryProviderBindings",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryProviderBindings_MediaLibraryEntryId_MediaProvi~",
                table: "MediaLibraryProviderBindings",
                columns: new[] { "MediaLibraryEntryId", "MediaProviderLinkId", "ProviderAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryProviderBindings_MediaProviderLinkId",
                table: "MediaLibraryProviderBindings",
                column: "MediaProviderLinkId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaProviderListMemberships_MediaLibraryProviderBindings_M~",
                table: "MediaProviderListMemberships",
                column: "MediaLibraryProviderBindingId",
                principalTable: "MediaLibraryProviderBindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaProviderOperations_MediaLibraryProviderBindings_MediaL~",
                table: "MediaProviderOperations",
                column: "MediaLibraryProviderBindingId",
                principalTable: "MediaLibraryProviderBindings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaProviderListMemberships_MediaLibraryProviderBindings_M~",
                table: "MediaProviderListMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaProviderOperations_MediaLibraryProviderBindings_MediaL~",
                table: "MediaProviderOperations");

            migrationBuilder.DropTable(
                name: "MediaLibraryProviderBindings");

            migrationBuilder.DropIndex(
                name: "IX_MediaLibraryEntries_UserId_MediaTitleId",
                table: "MediaLibraryEntries");

            migrationBuilder.DropColumn(
                name: "BackgroundUrl",
                table: "MediaTitles");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "MediaTitles");

            migrationBuilder.DropColumn(
                name: "NextReleaseAt",
                table: "MediaTitles");

            migrationBuilder.DropColumn(
                name: "NextReleaseLabel",
                table: "MediaTitles");

            migrationBuilder.DropColumn(
                name: "ReleasedCount",
                table: "MediaTitles");

            migrationBuilder.DropColumn(
                name: "PosterUrl",
                table: "MediaTitles");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalMetadata",
                table: "MediaTitles",
                type: "text",
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "MediaLibraryProviderBindingId",
                table: "MediaProviderOperations",
                newName: "MediaLibraryEntryId");

            migrationBuilder.RenameIndex(
                name: "IX_MediaProviderOperations_MediaLibraryProviderBindingId",
                table: "MediaProviderOperations",
                newName: "IX_MediaProviderOperations_MediaLibraryEntryId");

            migrationBuilder.RenameColumn(
                name: "MediaLibraryProviderBindingId",
                table: "MediaProviderListMemberships",
                newName: "MediaLibraryEntryId");

            migrationBuilder.RenameIndex(
                name: "IX_MediaProviderListMemberships_Name_MediaLibraryProviderBindi~",
                table: "MediaProviderListMemberships",
                newName: "IX_MediaProviderListMemberships_Name_MediaLibraryEntryId");

            migrationBuilder.AddColumn<int>(
                name: "ConnectedServiceAccountId",
                table: "MediaProviderOperations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "MediaProviderOperations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "MediaProviderOperations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ConnectedServiceAccountId",
                table: "MediaLibraryEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRemoteUpdateAt",
                table: "MediaLibraryEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                table: "MediaLibraryEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderAccountId",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderLibraryEntryId",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMediaId",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawMetadata",
                table: "MediaLibraryEntries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_ConnectedServiceAccountId",
                table: "MediaProviderOperations",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_UserId_Provider_CreatedAt",
                table: "MediaProviderOperations",
                columns: new[] { "UserId", "Provider", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_ConnectedServiceAccountId",
                table: "MediaLibraryEntries",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_UserId_Provider_ProviderAccountId_Provi~",
                table: "MediaLibraryEntries",
                columns: new[] { "UserId", "Provider", "ProviderAccountId", "ProviderMediaId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaLibraryEntries_ConnectedServiceAccounts_ConnectedServi~",
                table: "MediaLibraryEntries",
                column: "ConnectedServiceAccountId",
                principalTable: "ConnectedServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaProviderListMemberships_MediaLibraryEntries_MediaLibra~",
                table: "MediaProviderListMemberships",
                column: "MediaLibraryEntryId",
                principalTable: "MediaLibraryEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaProviderOperations_AspNetUsers_UserId",
                table: "MediaProviderOperations",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaProviderOperations_ConnectedServiceAccounts_ConnectedS~",
                table: "MediaProviderOperations",
                column: "ConnectedServiceAccountId",
                principalTable: "ConnectedServiceAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaProviderOperations_MediaLibraryEntries_MediaLibraryEnt~",
                table: "MediaProviderOperations",
                column: "MediaLibraryEntryId",
                principalTable: "MediaLibraryEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
