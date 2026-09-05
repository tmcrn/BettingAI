using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamCrestsToBetAndComboLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeTeamCrest",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwayTeamCrest",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeTeamCrest",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwayTeamCrest",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeTeamCrest",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "AwayTeamCrest",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "HomeTeamCrest",
                table: "ComboLegs");

            migrationBuilder.DropColumn(
                name: "AwayTeamCrest",
                table: "ComboLegs");
        }
    }
}
