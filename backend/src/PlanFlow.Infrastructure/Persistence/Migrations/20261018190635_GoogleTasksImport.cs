using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoogleTasksImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalCalendar~",
                table: "calendar_items");

            migrationBuilder.AddColumn<string>(
                name: "ExternalTaskId",
                table: "calendar_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExternalTaskListId",
                table: "calendar_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrantedScopes",
                table: "calendar_integrations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTasksSyncedAtUtc",
                table: "calendar_integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_task_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarIntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_task_lists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_task_lists_calendar_integrations_CalendarIntegrati~",
                        column: x => x.CalendarIntegrationId,
                        principalTable: "calendar_integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_ExternalTaskListId",
                table: "calendar_items",
                column: "ExternalTaskListId");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalCalendar~",
                table: "calendar_items",
                columns: new[] { "SourceCalendarIntegrationId", "ExternalCalendarEventId" },
                unique: true,
                filter: "\"ExternalCalendarEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalTaskId",
                table: "calendar_items",
                columns: new[] { "SourceCalendarIntegrationId", "ExternalTaskId" },
                unique: true,
                filter: "\"ExternalTaskId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_external_task_lists_CalendarIntegrationId_ExternalId",
                table: "external_task_lists",
                columns: new[] { "CalendarIntegrationId", "ExternalId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_calendar_items_external_task_lists_ExternalTaskListId",
                table: "calendar_items",
                column: "ExternalTaskListId",
                principalTable: "external_task_lists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_calendar_items_external_task_lists_ExternalTaskListId",
                table: "calendar_items");

            migrationBuilder.DropTable(
                name: "external_task_lists");

            migrationBuilder.DropIndex(
                name: "IX_calendar_items_ExternalTaskListId",
                table: "calendar_items");

            migrationBuilder.DropIndex(
                name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalCalendar~",
                table: "calendar_items");

            migrationBuilder.DropIndex(
                name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalTaskId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "ExternalTaskId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "ExternalTaskListId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "GrantedScopes",
                table: "calendar_integrations");

            migrationBuilder.DropColumn(
                name: "LastTasksSyncedAtUtc",
                table: "calendar_integrations");

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalCalendar~",
                table: "calendar_items",
                columns: new[] { "SourceCalendarIntegrationId", "ExternalCalendarEventId" },
                unique: true,
                filter: "\"SourceCalendarIntegrationId\" IS NOT NULL");
        }
    }
}
