using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddShortTeamNamesToBetAndComboLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeTeamShort",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwayTeamShort",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeTeamShort",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwayTeamShort",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeTeamShort",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "AwayTeamShort",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "HomeTeamShort",
                table: "ComboLegs");

            migrationBuilder.DropColumn(
                name: "AwayTeamShort",
                table: "ComboLegs");
        }
    }
}
