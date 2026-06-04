using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDpopReplayCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DpopReplayEntries",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAtUnixTimeSeconds = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DpopReplayEntries", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DpopReplayEntries_ExpiresAtUnixTimeSeconds",
                table: "DpopReplayEntries",
                column: "ExpiresAtUnixTimeSeconds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DpopReplayEntries");
        }
    }
}
