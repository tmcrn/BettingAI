using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamRecentResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamRecentResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamName = table.Column<string>(type: "TEXT", nullable: false),
                    OpponentName = table.Column<string>(type: "TEXT", nullable: false),
                    MatchDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GoalsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    IsHome = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRecentResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamRecentResults_TeamName",
                table: "TeamRecentResults",
                column: "TeamName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamRecentResults");
        }
    }
}
