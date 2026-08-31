using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddBetCombosAndLegs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BetCombos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Stake = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", precision: 3, scale: 2, nullable: false),
                    Reasoning = table.Column<string>(type: "TEXT", nullable: true),
                    CombinedOdds = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    Winnings = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BetCombos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComboLegs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BetComboId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<string>(type: "TEXT", nullable: false),
                    HomeTeam = table.Column<string>(type: "TEXT", nullable: true),
                    AwayTeam = table.Column<string>(type: "TEXT", nullable: true),
                    BetType = table.Column<string>(type: "TEXT", nullable: false),
                    Odds = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    MatchUtcDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComboLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComboLegs_BetCombos_BetComboId",
                        column: x => x.BetComboId,
                        principalTable: "BetCombos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComboLegs_BetComboId",
                table: "ComboLegs",
                column: "BetComboId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComboLegs");

            migrationBuilder.DropTable(
                name: "BetCombos");
        }
    }
}
