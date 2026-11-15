using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddValueBettingFieldsToBet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bookmaker",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingOdds",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosingOddsAt",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedValue",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarketProbability",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ModelProbability",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OddsEventId",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PoissonProbability",
                table: "Bets",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bookmaker",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "ClosingOdds",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "ClosingOddsAt",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "ExpectedValue",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "MarketProbability",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "ModelProbability",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "OddsEventId",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "PoissonProbability",
                table: "Bets");
        }
    }
}
