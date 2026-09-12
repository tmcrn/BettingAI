using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddCombinedOddsIsManualToBetCombo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CombinedOddsIsManual",
                table: "BetCombos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CombinedOddsIsManual",
                table: "BetCombos");
        }
    }
}
