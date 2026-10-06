using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAiComponent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiComponent",
                table: "urgency_score_logs");

            migrationBuilder.DropColumn(
                name: "AiFallbackUsed",
                table: "urgency_score_logs");

            migrationBuilder.DropColumn(
                name: "AiAssessmentAtUtc",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AiAssessmentScore",
                table: "tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AiComponent",
                table: "urgency_score_logs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "AiFallbackUsed",
                table: "urgency_score_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiAssessmentAtUtc",
                table: "tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AiAssessmentScore",
                table: "tasks",
                type: "double precision",
                nullable: true);
        }
    }
}
