using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAssessmentToTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAssessmentAtUtc",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AiAssessmentScore",
                table: "tasks");
        }
    }
}
