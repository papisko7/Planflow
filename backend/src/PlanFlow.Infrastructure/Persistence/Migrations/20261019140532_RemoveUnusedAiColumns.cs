using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedAiColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiImpactReasoning",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "AiImpactScore",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "UseAiEnhancement",
                table: "calendar_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AiImpactReasoning",
                table: "calendar_items",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AiImpactScore",
                table: "calendar_items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAiEnhancement",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
