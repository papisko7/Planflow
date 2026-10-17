using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 7.1: the "tasks" table becomes "calendar_items" (one table for Task and Event) and gains the scheduling,
    /// recurrence, AI and data-quality columns. Hand-edited: the scaffolded version dropped and recreated the table,
    /// which would have deleted every existing task, so it is a rename + AddColumn instead and rows are preserved.
    /// </summary>
    public partial class CalendarItemFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_alerts_tasks_TaskItemId", table: "alerts");
            migrationBuilder.DropForeignKey(name: "FK_task_histories_tasks_TaskItemId", table: "task_histories");
            migrationBuilder.DropForeignKey(name: "FK_urgency_score_logs_tasks_TaskItemId", table: "urgency_score_logs");

            // The table itself, then every constraint/index name that embeds the old table name, so the database
            // matches the EF model exactly (no phantom differences in the next migration).
            migrationBuilder.RenameTable(name: "tasks", newName: "calendar_items");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"PK_tasks\" TO \"PK_calendar_items\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_tasks_calendar_integrations_SourceCalendarIntegrationId\" TO \"FK_calendar_items_calendar_integrations_SourceCalendarIntegrat~\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_tasks_tasks_BlockedByTaskId\" TO \"FK_calendar_items_calendar_items_BlockedByTaskId\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_tasks_teams_TeamId\" TO \"FK_calendar_items_teams_TeamId\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_tasks_users_AssignedUserId\" TO \"FK_calendar_items_users_AssignedUserId\";");
            migrationBuilder.RenameIndex(name: "IX_tasks_AssignedUserId", table: "calendar_items", newName: "IX_calendar_items_AssignedUserId");
            migrationBuilder.RenameIndex(name: "IX_tasks_BlockedByTaskId", table: "calendar_items", newName: "IX_calendar_items_BlockedByTaskId");
            migrationBuilder.RenameIndex(name: "IX_tasks_CurrentUrgencyScore", table: "calendar_items", newName: "IX_calendar_items_CurrentUrgencyScore");
            migrationBuilder.RenameIndex(name: "IX_tasks_SourceCalendarIntegrationId_ExternalCalendarEventId", table: "calendar_items", newName: "IX_calendar_items_SourceCalendarIntegrationId_ExternalCalendar~");
            migrationBuilder.RenameIndex(name: "IX_tasks_TeamId", table: "calendar_items", newName: "IX_calendar_items_TeamId");

            // Child tables: the foreign key column follows the entity name.
            migrationBuilder.RenameColumn(name: "TaskItemId", table: "urgency_score_logs", newName: "CalendarItemId");
            migrationBuilder.RenameIndex(name: "IX_urgency_score_logs_TaskItemId_CreatedAtUtc", table: "urgency_score_logs", newName: "IX_urgency_score_logs_CalendarItemId_CreatedAtUtc");
            migrationBuilder.RenameColumn(name: "TaskItemId", table: "task_histories", newName: "CalendarItemId");
            migrationBuilder.RenameIndex(name: "IX_task_histories_TaskItemId_CreatedAtUtc", table: "task_histories", newName: "IX_task_histories_CalendarItemId_CreatedAtUtc");
            migrationBuilder.RenameColumn(name: "TaskItemId", table: "alerts", newName: "CalendarItemId");
            migrationBuilder.RenameIndex(name: "IX_alerts_TaskItemId", table: "alerts", newName: "IX_alerts_CalendarItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_calendar_items_CalendarItemId", table: "alerts", column: "CalendarItemId",
                principalTable: "calendar_items", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_task_histories_calendar_items_CalendarItemId", table: "task_histories", column: "CalendarItemId",
                principalTable: "calendar_items", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_urgency_score_logs_calendar_items_CalendarItemId", table: "urgency_score_logs", column: "CalendarItemId",
                principalTable: "calendar_items", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            // New columns. Everything is nullable or has a default, so existing rows become plain Tasks with AI off.
            migrationBuilder.AddColumn<string>(name: "ReasonText", table: "urgency_score_logs", type: "character varying(512)", maxLength: 512, nullable: true);

            migrationBuilder.AddColumn<string>(name: "AiImpactReasoning", table: "calendar_items", type: "character varying(2000)", maxLength: 2000, nullable: true);
            migrationBuilder.AddColumn<double>(name: "AiImpactScore", table: "calendar_items", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "EndUtc", table: "calendar_items", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<double>(name: "EstimatedEffortHours", table: "calendar_items", type: "double precision", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsDataIncomplete", table: "calendar_items", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(name: "IsRecurring", table: "calendar_items", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(name: "RecurrenceEndsAtUtc", table: "calendar_items", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(name: "RecurrenceIntervalHours", table: "calendar_items", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "StartUtc", table: "calendar_items", type: "timestamp with time zone", nullable: true);
            migrationBuilder.AddColumn<int>(name: "TaskType", table: "calendar_items", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "UseAiEnhancement", table: "calendar_items", type: "boolean", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReasonText", table: "urgency_score_logs");

            migrationBuilder.DropColumn(name: "AiImpactReasoning", table: "calendar_items");
            migrationBuilder.DropColumn(name: "AiImpactScore", table: "calendar_items");
            migrationBuilder.DropColumn(name: "EndUtc", table: "calendar_items");
            migrationBuilder.DropColumn(name: "EstimatedEffortHours", table: "calendar_items");
            migrationBuilder.DropColumn(name: "IsDataIncomplete", table: "calendar_items");
            migrationBuilder.DropColumn(name: "IsRecurring", table: "calendar_items");
            migrationBuilder.DropColumn(name: "RecurrenceEndsAtUtc", table: "calendar_items");
            migrationBuilder.DropColumn(name: "RecurrenceIntervalHours", table: "calendar_items");
            migrationBuilder.DropColumn(name: "StartUtc", table: "calendar_items");
            migrationBuilder.DropColumn(name: "TaskType", table: "calendar_items");
            migrationBuilder.DropColumn(name: "UseAiEnhancement", table: "calendar_items");

            migrationBuilder.DropForeignKey(name: "FK_alerts_calendar_items_CalendarItemId", table: "alerts");
            migrationBuilder.DropForeignKey(name: "FK_task_histories_calendar_items_CalendarItemId", table: "task_histories");
            migrationBuilder.DropForeignKey(name: "FK_urgency_score_logs_calendar_items_CalendarItemId", table: "urgency_score_logs");

            migrationBuilder.RenameIndex(name: "IX_urgency_score_logs_CalendarItemId_CreatedAtUtc", table: "urgency_score_logs", newName: "IX_urgency_score_logs_TaskItemId_CreatedAtUtc");
            migrationBuilder.RenameColumn(name: "CalendarItemId", table: "urgency_score_logs", newName: "TaskItemId");
            migrationBuilder.RenameIndex(name: "IX_task_histories_CalendarItemId_CreatedAtUtc", table: "task_histories", newName: "IX_task_histories_TaskItemId_CreatedAtUtc");
            migrationBuilder.RenameColumn(name: "CalendarItemId", table: "task_histories", newName: "TaskItemId");
            migrationBuilder.RenameIndex(name: "IX_alerts_CalendarItemId", table: "alerts", newName: "IX_alerts_TaskItemId");
            migrationBuilder.RenameColumn(name: "CalendarItemId", table: "alerts", newName: "TaskItemId");

            migrationBuilder.RenameIndex(name: "IX_calendar_items_AssignedUserId", table: "calendar_items", newName: "IX_tasks_AssignedUserId");
            migrationBuilder.RenameIndex(name: "IX_calendar_items_BlockedByTaskId", table: "calendar_items", newName: "IX_tasks_BlockedByTaskId");
            migrationBuilder.RenameIndex(name: "IX_calendar_items_CurrentUrgencyScore", table: "calendar_items", newName: "IX_tasks_CurrentUrgencyScore");
            migrationBuilder.RenameIndex(name: "IX_calendar_items_SourceCalendarIntegrationId_ExternalCalendar~", table: "calendar_items", newName: "IX_tasks_SourceCalendarIntegrationId_ExternalCalendarEventId");
            migrationBuilder.RenameIndex(name: "IX_calendar_items_TeamId", table: "calendar_items", newName: "IX_tasks_TeamId");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_calendar_items_users_AssignedUserId\" TO \"FK_tasks_users_AssignedUserId\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_calendar_items_teams_TeamId\" TO \"FK_tasks_teams_TeamId\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_calendar_items_calendar_items_BlockedByTaskId\" TO \"FK_tasks_tasks_BlockedByTaskId\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"FK_calendar_items_calendar_integrations_SourceCalendarIntegrat~\" TO \"FK_tasks_calendar_integrations_SourceCalendarIntegrationId\";");
            migrationBuilder.Sql("ALTER TABLE calendar_items RENAME CONSTRAINT \"PK_calendar_items\" TO \"PK_tasks\";");
            migrationBuilder.RenameTable(name: "calendar_items", newName: "tasks");

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_tasks_TaskItemId", table: "alerts", column: "TaskItemId",
                principalTable: "tasks", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_task_histories_tasks_TaskItemId", table: "task_histories", column: "TaskItemId",
                principalTable: "tasks", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_urgency_score_logs_tasks_TaskItemId", table: "urgency_score_logs", column: "TaskItemId",
                principalTable: "tasks", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        }
    }
}
