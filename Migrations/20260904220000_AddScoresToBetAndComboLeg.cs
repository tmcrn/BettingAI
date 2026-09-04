using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddScoresToBetAndComboLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeScore",
                table: "Bets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwayScore",
                table: "Bets",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeScore",
                table: "ComboLegs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwayScore",
                table: "ComboLegs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeScore",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "AwayScore",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "HomeScore",
                table: "ComboLegs");

            migrationBuilder.DropColumn(
                name: "AwayScore",
                table: "ComboLegs");
        }
    }
}
