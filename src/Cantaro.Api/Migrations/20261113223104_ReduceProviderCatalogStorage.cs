using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReduceProviderCatalogStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CatalogReductionData.Stage);

            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistEntries_Playlists_PlaylistId",
                table: "PlaylistEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistEntries_TrackObservations_TrackObservationId",
                table: "PlaylistEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropForeignKey(
                name: "FK_ServicePlaylistMappings_Playlists_PlaylistId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropForeignKey(
                name: "FK_TrackObservations_ProviderCatalogItems_ProviderCatalogItemId",
                table: "TrackObservations");

            migrationBuilder.DropForeignKey(
                name: "FK_Tracks_ProviderCatalogItems_PreferredCatalogItemId",
                table: "Tracks");

            migrationBuilder.DropTable(
                name: "TrackSourceIds");

            migrationBuilder.DropIndex(
                name: "IX_Tracks_PreferredCatalogItemId",
                table: "Tracks");

            migrationBuilder.DropIndex(
                name: "IX_TrackObservations_IdentityLookup",
                table: "TrackObservations");

            migrationBuilder.DropIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId",
                table: "ServicePlaylistMappings");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistEntries_TrackObservationId",
                table: "PlaylistEntries");

            migrationBuilder.DropIndex(
                name: "IX_Artists_MusicBrainzArtistId",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "CanonicalMetadata",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "PreferredCatalogItemId",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SearchArtist",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "SearchTitle",
                table: "Tracks");

            migrationBuilder.DropColumn(
                name: "NormalizedArtist",
                table: "TrackObservations");

            migrationBuilder.DropColumn(
                name: "NormalizedTitle",
                table: "TrackObservations");

            migrationBuilder.DropColumn(
                name: "Provenance",
                table: "TrackArtistCredits");

            migrationBuilder.DropColumn(
                name: "MusicBrainzArtistId",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "ArtistReconciliationConflicts");

            migrationBuilder.RenameColumn(
                name: "RawMetadata",
                table: "TrackResolutionCandidates",
                newName: "EvaluationJson");

            migrationBuilder.RenameColumn(
                name: "RawMetadata",
                table: "TrackObservations",
                newName: "PrivateInputJson");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "TrackResolutionCandidates",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderCatalogItemId",
                table: "TrackResolutionCandidates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderCatalogRevision",
                table: "TrackResolutionCandidates",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "TrackObservations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "InputKind",
                table: "TrackObservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ProviderCatalogRevision",
                table: "TrackArtistCredits",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceCreditPosition",
                table: "TrackArtistCredits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssociationEvidenceJson",
                table: "ProviderCatalogItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssociationFactsRevision",
                table: "ProviderCatalogItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssociationMethod",
                table: "ProviderCatalogItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AssociationRevision",
                table: "ProviderCatalogItems",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "AssociationState",
                table: "ProviderCatalogItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssociationVerifiedAt",
                table: "ProviderCatalogItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOfficial",
                table: "ProviderCatalogItems",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PresentationConfidence",
                table: "ProviderCatalogItems",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "PresentationKind",
                table: "ProviderCatalogItems",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "TrackId",
                table: "ProviderCatalogItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "PlaylistEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IdentityConflictJson",
                table: "ArtistReconciliationConflicts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderCatalogItemId",
                table: "ArtistReconciliationConflicts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderCatalogRevision",
                table: "ArtistReconciliationConflicts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceCreditPosition",
                table: "ArtistReconciliationConflicts",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(CatalogReductionData.Convert);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TrackResolutionCandidates_Id_TrackObservationId",
                table: "TrackResolutionCandidates",
                columns: new[] { "Id", "TrackObservationId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TrackObservations_Id_OwnerUserId",
                table: "TrackObservations",
                columns: new[] { "Id", "OwnerUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProviderCatalogItems_Id_SourceType_ExternalId",
                table: "ProviderCatalogItems",
                columns: new[] { "Id", "SourceType", "ExternalId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Playlists_Id_UserId",
                table: "Playlists",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ConnectedServiceAccounts_Id_UserId_Service",
                table: "ConnectedServiceAccounts",
                columns: new[] { "Id", "UserId", "Service" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackResolutionCandidates_ProviderCatalogItemId_CandidateSo~",
                table: "TrackResolutionCandidates",
                columns: new[] { "ProviderCatalogItemId", "CandidateSource", "ExternalId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackResolutionCandidates_PublicInput",
                table: "TrackResolutionCandidates",
                sql: "\"ProviderCatalogItemId\" IS NULL OR (\"ProviderCatalogRevision\" IS NOT NULL AND \"ProviderCatalogRevision\" > 0 AND \"Title\" IS NULL AND \"Artist\" IS NULL AND \"MbidRecording\" IS NULL AND \"Isrc\" IS NULL AND \"DurationSeconds\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_AcceptedCandidateId_Id",
                table: "TrackObservations",
                columns: new[] { "AcceptedCandidateId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_OwnerUserId_TrackId",
                table: "TrackObservations",
                columns: new[] { "OwnerUserId", "TrackId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackObservations_ProviderCatalogItemId_SourceType_External~",
                table: "TrackObservations",
                columns: new[] { "ProviderCatalogItemId", "SourceType", "ExternalId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackObservations_Input",
                table: "TrackObservations",
                sql: "(\"InputKind\" = 2 AND \"Title\" IS NOT NULL AND length(trim(\"Title\")) > 0) OR (\"InputKind\" IN (0, 1) AND (\"InputKind\" <> 1 OR \"ProviderCatalogItemId\" IS NOT NULL) AND \"Title\" IS NULL AND \"Artist\" IS NULL AND \"ThumbnailUrl\" IS NULL AND \"DurationSeconds\" IS NULL AND \"PrivateInputJson\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_ConnectedServiceAccountId_UserId_Se~",
                table: "ServicePlaylistMappings",
                columns: new[] { "ConnectedServiceAccountId", "UserId", "Service" });

            migrationBuilder.CreateIndex(
                name: "IX_ServicePlaylistMappings_PlaylistId_UserId",
                table: "ServicePlaylistMappings",
                columns: new[] { "PlaylistId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderCatalogItems_TrackId",
                table: "ProviderCatalogItems",
                column: "TrackId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProviderCatalogItems_PresentationConfidence",
                table: "ProviderCatalogItems",
                sql: "\"PresentationConfidence\" IS NULL OR (\"PresentationConfidence\" >= 0 AND \"PresentationConfidence\" <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProviderCatalogItems_VerifiedAssociation",
                table: "ProviderCatalogItems",
                sql: "\"AssociationState\" <> 1 OR (\"TrackId\" IS NOT NULL AND \"AssociationMethod\" IS NOT NULL AND \"AssociationMethod\" IN ('provider_origin', 'independent_recording') AND \"AssociationVerifiedAt\" IS NOT NULL AND \"AssociationEvidenceJson\" IS NOT NULL AND \"AssociationFactsRevision\" IS NOT NULL AND \"AssociationFactsRevision\" > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_PlaylistId_OwnerUserId",
                table: "PlaylistEntries",
                columns: new[] { "PlaylistId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistEntries_TrackObservationId_OwnerUserId",
                table: "PlaylistEntries",
                columns: new[] { "TrackObservationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ArtistReconciliationConflicts_ProviderCatalogItemId",
                table: "ArtistReconciliationConflicts",
                column: "ProviderCatalogItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArtistReconciliationConflicts_ProviderCatalogItems_Provider~",
                table: "ArtistReconciliationConflicts",
                column: "ProviderCatalogItemId",
                principalTable: "ProviderCatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistEntries_Playlists_PlaylistId_OwnerUserId",
                table: "PlaylistEntries",
                columns: new[] { "PlaylistId", "OwnerUserId" },
                principalTable: "Playlists",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistEntries_TrackObservations_TrackObservationId_OwnerU~",
                table: "PlaylistEntries",
                columns: new[] { "TrackObservationId", "OwnerUserId" },
                principalTable: "TrackObservations",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProviderCatalogItems_Tracks_TrackId",
                table: "ProviderCatalogItems",
                column: "TrackId",
                principalTable: "Tracks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePlaylistMappings_ConnectedServiceAccounts_ConnectedS~",
                table: "ServicePlaylistMappings",
                columns: new[] { "ConnectedServiceAccountId", "UserId", "Service" },
                principalTable: "ConnectedServiceAccounts",
                principalColumns: new[] { "Id", "UserId", "Service" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ServicePlaylistMappings_Playlists_PlaylistId_UserId",
                table: "ServicePlaylistMappings",
                columns: new[] { "PlaylistId", "UserId" },
                principalTable: "Playlists",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackObservations_ProviderCatalogItems_ProviderCatalogItemI~",
                table: "TrackObservations",
                columns: new[] { "ProviderCatalogItemId", "SourceType", "ExternalId" },
                principalTable: "ProviderCatalogItems",
                principalColumns: new[] { "Id", "SourceType", "ExternalId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackObservations_TrackResolutionCandidates_AcceptedCandida~",
                table: "TrackObservations",
                columns: new[] { "AcceptedCandidateId", "Id" },
                principalTable: "TrackResolutionCandidates",
                principalColumns: new[] { "Id", "TrackObservationId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrackResolutionCandidates_ProviderCatalogItems_ProviderCata~",
                table: "TrackResolutionCandidates",
                columns: new[] { "ProviderCatalogItemId", "CandidateSource", "ExternalId" },
                principalTable: "ProviderCatalogItems",
                principalColumns: new[] { "Id", "SourceType", "ExternalId" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(CatalogReductionData.AssertPreserved);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore the pre-deployment backup and old application to roll back the catalog reduction. Dropped metadata copies cannot be reconstructed safely.");
        }
    }
}