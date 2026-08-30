using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamStatsAndContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearningNotebook",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Patterns = table.Column<string>(type: "TEXT", nullable: true),
                    FailureAnalysis = table.Column<string>(type: "TEXT", nullable: true),
                    SuccessPatterns = table.Column<string>(type: "TEXT", nullable: true),
                    TotalBets = table.Column<int>(type: "INTEGER", nullable: false),
                    WonBets = table.Column<int>(type: "INTEGER", nullable: false),
                    WinRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    AverageConfidence = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningNotebook", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchContexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<string>(type: "TEXT", nullable: true),
                    HomeTeam = table.Column<string>(type: "TEXT", nullable: true),
                    AwayTeam = table.Column<string>(type: "TEXT", nullable: true),
                    MatchDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HomeLineup = table.Column<string>(type: "TEXT", nullable: true),
                    AwayLineup = table.Column<string>(type: "TEXT", nullable: true),
                    HomeMissingPlayers = table.Column<string>(type: "TEXT", nullable: true),
                    AwayMissingPlayers = table.Column<string>(type: "TEXT", nullable: true),
                    HomeWinsH2H = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawsH2H = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayWinsH2H = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgGoalsH2H = table.Column<decimal>(type: "TEXT", nullable: false),
                    Competition = table.Column<string>(type: "TEXT", nullable: true),
                    Weather = table.Column<string>(type: "TEXT", nullable: true),
                    Altitude = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEuropeanMatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDerby = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpectedScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    HomeExpectedWinProbability = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchContexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamName = table.Column<string>(type: "TEXT", nullable: true),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false),
                    xG = table.Column<decimal>(type: "TEXT", nullable: false),
                    xA = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShotsOnTarget = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalShots = table.Column<int>(type: "INTEGER", nullable: false),
                    ConversionRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    PossessionAvg = table.Column<decimal>(type: "TEXT", nullable: false),
                    xGA = table.Column<decimal>(type: "TEXT", nullable: false),
                    ShotsConceded = table.Column<int>(type: "INTEGER", nullable: false),
                    CleanSheets = table.Column<int>(type: "INTEGER", nullable: false),
                    DefenseRating = table.Column<decimal>(type: "TEXT", nullable: false),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false),
                    Draws = table.Column<int>(type: "INTEGER", nullable: false),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false),
                    FormLast5 = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsHomeMatch = table.Column<bool>(type: "INTEGER", nullable: false),
                    DaysSinceLastMatch = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyInjuries = table.Column<string>(type: "TEXT", nullable: true),
                    FatigueIndex = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConsecutiveMatches = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamStats", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearningNotebook");

            migrationBuilder.DropTable(
                name: "MatchContexts");

            migrationBuilder.DropTable(
                name: "TeamStats");
        }
    }
}
