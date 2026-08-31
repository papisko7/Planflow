using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarSyncFieldsToTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalCalendarEventId",
                table: "tasks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceCalendarIntegrationId",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_SourceCalendarIntegrationId_ExternalCalendarEventId",
                table: "tasks",
                columns: new[] { "SourceCalendarIntegrationId", "ExternalCalendarEventId" },
                unique: true,
                filter: "\"SourceCalendarIntegrationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_calendar_integrations_SourceCalendarIntegrationId",
                table: "tasks",
                column: "SourceCalendarIntegrationId",
                principalTable: "calendar_integrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_calendar_integrations_SourceCalendarIntegrationId",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_SourceCalendarIntegrationId_ExternalCalendarEventId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "ExternalCalendarEventId",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "SourceCalendarIntegrationId",
                table: "tasks");
        }
    }
}
