using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantaro.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAioStreamsPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AioStreamsPairings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AioStreamsPairings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_AioStreamsPairings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AioStreamsPairings_TokenHash",
                table: "AioStreamsPairings",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AioStreamsPairings");
        }
    }
}
