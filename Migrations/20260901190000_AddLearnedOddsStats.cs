using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddLearnedOddsStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearnedOddsStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BetType = table.Column<string>(type: "TEXT", nullable: false),
                    AverageOdds = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnedOddsStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearnedOddsStats_BetType",
                table: "LearnedOddsStats",
                column: "BetType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnedOddsStats");
        }
    }
}
