using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectionToComboLeg : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Selection",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Selection",
                table: "ComboLegs");
        }
    }
}
