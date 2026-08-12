using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTitleRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Format",
                table: "MediaTitles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RelationsLastVerifiedAt",
                table: "MediaProviderLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaTitleRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelatedMediaTitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRelationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTitleRelations", x => x.Id);
                    table.CheckConstraint("CK_MediaTitleRelations_NoSelfRelation", "\"MediaTitleId\" <> \"RelatedMediaTitleId\"");
                    table.ForeignKey(
                        name: "FK_MediaTitleRelations_MediaTitles_MediaTitleId",
                        column: x => x.MediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaTitleRelations_MediaTitles_RelatedMediaTitleId",
                        column: x => x.RelatedMediaTitleId,
                        principalTable: "MediaTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTitleRelations_MediaTitleId_RelatedMediaTitleId_Relati~",
                table: "MediaTitleRelations",
                columns: new[] { "MediaTitleId", "RelatedMediaTitleId", "RelationType", "SourceProvider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaTitleRelations_RelatedMediaTitleId_RelationType_MediaT~",
                table: "MediaTitleRelations",
                columns: new[] { "RelatedMediaTitleId", "RelationType", "MediaTitleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaTitleRelations");

            migrationBuilder.DropColumn(
                name: "RelationsLastVerifiedAt",
                table: "MediaProviderLinks");

            migrationBuilder.AlterColumn<string>(
                name: "Format",
                table: "MediaTitles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
