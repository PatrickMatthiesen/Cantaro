using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackSourcePresentationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PresentationKind",
                table: "TrackSourceIds",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PresentationKindClassifiedAt",
                table: "TrackSourceIds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PresentationKindConfidence",
                table: "TrackSourceIds",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationKindEvidenceIdentity",
                table: "TrackSourceIds",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationKindEvidenceMethod",
                table: "TrackSourceIds",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationKindEvidenceSource",
                table: "TrackSourceIds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationKindMethodVersion",
                table: "TrackSourceIds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PresentationKindRevision",
                table: "TrackSourceIds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploaderAuthority",
                table: "TrackSourceIds",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UploaderAuthorityClassifiedAt",
                table: "TrackSourceIds",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UploaderAuthorityConfidence",
                table: "TrackSourceIds",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploaderAuthorityEvidenceIdentity",
                table: "TrackSourceIds",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploaderAuthorityEvidenceMethod",
                table: "TrackSourceIds",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploaderAuthorityEvidenceSource",
                table: "TrackSourceIds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UploaderAuthorityMethodVersion",
                table: "TrackSourceIds",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UploaderAuthorityRevision",
                table: "TrackSourceIds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackSourceIds_PresentationKindBundle",
                table: "TrackSourceIds",
                sql: "(\"PresentationKind\" IS NULL AND \"PresentationKindConfidence\" IS NULL AND \"PresentationKindEvidenceSource\" IS NULL AND \"PresentationKindEvidenceIdentity\" IS NULL AND \"PresentationKindEvidenceMethod\" IS NULL AND \"PresentationKindMethodVersion\" IS NULL AND \"PresentationKindClassifiedAt\" IS NULL AND \"PresentationKindRevision\" IS NULL) OR (\"PresentationKind\" IS NOT NULL AND \"PresentationKindConfidence\" IS NOT NULL AND \"PresentationKindEvidenceSource\" IS NOT NULL AND \"PresentationKindEvidenceIdentity\" IS NOT NULL AND \"PresentationKindEvidenceMethod\" IS NOT NULL AND \"PresentationKindClassifiedAt\" IS NOT NULL AND \"PresentationKindRevision\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackSourceIds_PresentationKindConfidence",
                table: "TrackSourceIds",
                sql: "\"PresentationKindConfidence\" IS NULL OR (CAST(\"PresentationKindConfidence\" AS REAL) >= 0 AND CAST(\"PresentationKindConfidence\" AS REAL) <= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackSourceIds_UploaderAuthorityBundle",
                table: "TrackSourceIds",
                sql: "(\"UploaderAuthority\" IS NULL AND \"UploaderAuthorityConfidence\" IS NULL AND \"UploaderAuthorityEvidenceSource\" IS NULL AND \"UploaderAuthorityEvidenceIdentity\" IS NULL AND \"UploaderAuthorityEvidenceMethod\" IS NULL AND \"UploaderAuthorityMethodVersion\" IS NULL AND \"UploaderAuthorityClassifiedAt\" IS NULL AND \"UploaderAuthorityRevision\" IS NULL) OR (\"UploaderAuthority\" IS NOT NULL AND \"UploaderAuthorityConfidence\" IS NOT NULL AND \"UploaderAuthorityEvidenceSource\" IS NOT NULL AND \"UploaderAuthorityEvidenceIdentity\" IS NOT NULL AND \"UploaderAuthorityEvidenceMethod\" IS NOT NULL AND \"UploaderAuthorityClassifiedAt\" IS NOT NULL AND \"UploaderAuthorityRevision\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TrackSourceIds_UploaderAuthorityConfidence",
                table: "TrackSourceIds",
                sql: "\"UploaderAuthorityConfidence\" IS NULL OR (CAST(\"UploaderAuthorityConfidence\" AS REAL) >= 0 AND CAST(\"UploaderAuthorityConfidence\" AS REAL) <= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TrackSourceIds_PresentationKindBundle",
                table: "TrackSourceIds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrackSourceIds_PresentationKindConfidence",
                table: "TrackSourceIds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrackSourceIds_UploaderAuthorityBundle",
                table: "TrackSourceIds");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TrackSourceIds_UploaderAuthorityConfidence",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKind",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindClassifiedAt",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindConfidence",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindEvidenceIdentity",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindEvidenceMethod",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindEvidenceSource",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindMethodVersion",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "PresentationKindRevision",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthority",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityClassifiedAt",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityConfidence",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityEvidenceIdentity",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityEvidenceMethod",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityEvidenceSource",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityMethodVersion",
                table: "TrackSourceIds");

            migrationBuilder.DropColumn(
                name: "UploaderAuthorityRevision",
                table: "TrackSourceIds");
        }
    }
}
