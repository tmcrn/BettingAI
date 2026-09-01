using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BettingAI.Migrations
{
    /// <inheritdoc />
    public partial class AddLearnedModelWeights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EdgeAlignmentFeature",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FormAlignmentFeature",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MomentumAlignmentFeature",
                table: "Bets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                table: "ComboLegs",
                type: "TEXT",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EdgeAlignmentFeature",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FormAlignmentFeature",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MomentumAlignmentFeature",
                table: "ComboLegs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LearnedModelWeights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Bias = table.Column<decimal>(type: "TEXT", nullable: false),
                    WeightEdgeAlignment = table.Column<decimal>(type: "TEXT", nullable: false),
                    WeightFormAlignment = table.Column<decimal>(type: "TEXT", nullable: false),
                    WeightMomentumAlignment = table.Column<decimal>(type: "TEXT", nullable: false),
                    WeightConfidence = table.Column<decimal>(type: "TEXT", nullable: false),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnedModelWeights", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnedModelWeights");

            migrationBuilder.DropColumn(
                name: "EdgeAlignmentFeature",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "FormAlignmentFeature",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "MomentumAlignmentFeature",
                table: "Bets");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ComboLegs");

            migrationBuilder.DropColumn(
                name: "EdgeAlignmentFeature",
                table: "ComboLegs");

            migrationBuilder.DropColumn(
                name: "FormAlignmentFeature",
                table: "ComboLegs");

            migrationBuilder.DropColumn(
                name: "MomentumAlignmentFeature",
                table: "ComboLegs");
        }
    }
}
