using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaObjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalTitle = table.Column<string>(type: "text", nullable: false),
                    SortTitle = table.Column<string>(type: "text", nullable: true),
                    OriginalTitle = table.Column<string>(type: "text", nullable: true),
                    MediaKind = table.Column<string>(type: "text", nullable: false),
                    Synopsis = table.Column<string>(type: "text", nullable: true),
                    StartYear = table.Column<int>(type: "integer", nullable: true),
                    EpisodeCount = table.Column<int>(type: "integer", nullable: true),
                    ChapterCount = table.Column<int>(type: "integer", nullable: true),
                    VolumeCount = table.Column<int>(type: "integer", nullable: true),
                    SupportsEpisodeProgress = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsChapterProgress = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsVolumeProgress = table.Column<bool>(type: "boolean", nullable: false),
                    IsCompletionOnly = table.Column<bool>(type: "boolean", nullable: false),
                    PrimaryProgressDimension = table.Column<string>(type: "text", nullable: false),
                    ReleaseStatusDimension = table.Column<string>(type: "text", nullable: false),
                    CanonicalMetadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTitles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaLibraryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedServiceAccountId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "text", nullable: false),
                    ProviderMediaId = table.Column<string>(type: "text", nullable: false),
                    ProviderLibraryEntryId = table.Column<string>(type: "text", nullable: true),
                    NormalizedStatus = table.Column<string>(type: "text", nullable: false),
                    RawStatus = table.Column<string>(type: "text", nullable: true),
                    RawListName = table.Column<string>(type: "text", nullable: true),
                    ProgressEpisodes = table.Column<int>(type: "integer", nullable: true),
                    ProgressChapters = table.Column<int>(type: "integer", nullable: true),
                    ProgressVolumes = table.Column<int>(type: "integer", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRemoteUpdateAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLocalEditAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMutationSource = table.Column<string>(type: "text", nullable: true),
                    RawMetadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    AutoProgressFromObservations = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibraryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaLibraryEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaLibraryEntries_ConnectedServiceAccounts_ConnectedServi~",
                        column: x => x.ConnectedServiceAccountId,
                        principalTable: "ConnectedServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaLibraryEntries_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SiteIdentifier = table.Column<string>(type: "text", nullable: false),
                    ObservedUrl = table.Column<string>(type: "text", nullable: false),
                    SiteMediaId = table.Column<string>(type: "text", nullable: true),
                    ObservedTitle = table.Column<string>(type: "text", nullable: false),
                    ProgressHint = table.Column<string>(type: "text", nullable: true),
                    ObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExtensionVersion = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    MatchStatus = table.Column<string>(type: "text", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedCandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    MatchAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastMatchAttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMatchError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaObservations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaObservations_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaProviderLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    ExternalUrl = table.Column<string>(type: "text", nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    RawMetadata = table.Column<string>(type: "text", nullable: true),
                    LinkSource = table.Column<string>(type: "text", nullable: false),
                    LinkedByUserId = table.Column<int>(type: "integer", nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaProviderLinks_AspNetUsers_LinkedByUserId",
                        column: x => x.LinkedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaProviderLinks_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaProviderOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MediaLibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedServiceAccountId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    OperationType = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaProviderOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaProviderOperations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaProviderOperations_ConnectedServiceAccounts_ConnectedS~",
                        column: x => x.ConnectedServiceAccountId,
                        principalTable: "ConnectedServiceAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaProviderOperations_MediaLibraryEntries_MediaLibraryEnt~",
                        column: x => x.MediaLibraryEntryId,
                        principalTable: "MediaLibraryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaObservationCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaObservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateSource = table.Column<string>(type: "text", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderMediaId = table.Column<string>(type: "text", nullable: true),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    MediaKind = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<decimal>(type: "numeric", nullable: false),
                    Explanation = table.Column<string>(type: "text", nullable: true),
                    IsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaObservationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaObservationCandidates_MediaObservations_MediaObservati~",
                        column: x => x.MediaObservationId,
                        principalTable: "MediaObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaObservationCandidates_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_ConnectedServiceAccountId",
                table: "MediaLibraryEntries",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_MediaTitleId",
                table: "MediaLibraryEntries",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_UserId_NormalizedStatus",
                table: "MediaLibraryEntries",
                columns: new[] { "UserId", "NormalizedStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibraryEntries_UserId_Provider_ProviderAccountId_Provi~",
                table: "MediaLibraryEntries",
                columns: new[] { "UserId", "Provider", "ProviderAccountId", "ProviderMediaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationCandidates_MediaObservationId_Score",
                table: "MediaObservationCandidates",
                columns: new[] { "MediaObservationId", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservationCandidates_MediaTitleId",
                table: "MediaObservationCandidates",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_MediaTitleId",
                table: "MediaObservations",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_UserId_CreatedAt",
                table: "MediaObservations",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_UserId_MatchStatus",
                table: "MediaObservations",
                columns: new[] { "UserId", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaObservations_UserId_SiteIdentifier_SiteMediaId",
                table: "MediaObservations",
                columns: new[] { "UserId", "SiteIdentifier", "SiteMediaId" },
                unique: true,
                filter: "\"SiteMediaId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderLinks_LinkedByUserId",
                table: "MediaProviderLinks",
                column: "LinkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderLinks_MediaTitleId",
                table: "MediaProviderLinks",
                column: "MediaTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderLinks_Provider_ExternalId",
                table: "MediaProviderLinks",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_ConnectedServiceAccountId",
                table: "MediaProviderOperations",
                column: "ConnectedServiceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_MediaLibraryEntryId",
                table: "MediaProviderOperations",
                column: "MediaLibraryEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_Status_NextAttemptAt",
                table: "MediaProviderOperations",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaProviderOperations_UserId_Provider_CreatedAt",
                table: "MediaProviderOperations",
                columns: new[] { "UserId", "Provider", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTitles_MediaKind_CanonicalTitle",
                table: "MediaTitles",
                columns: new[] { "MediaKind", "CanonicalTitle" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaObservationCandidates");

            migrationBuilder.DropTable(
                name: "MediaProviderLinks");

            migrationBuilder.DropTable(
                name: "MediaProviderOperations");

            migrationBuilder.DropTable(
                name: "MediaObservations");

            migrationBuilder.DropTable(
                name: "MediaLibraryEntries");

            migrationBuilder.DropTable(
                name: "MediaTitles");
        }
    }
}
