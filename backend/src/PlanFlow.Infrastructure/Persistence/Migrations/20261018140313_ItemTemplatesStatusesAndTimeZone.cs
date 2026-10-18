using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 7.5.1: the new item/user/score-log columns plus the status clean-up. Hand-edited: the existing rows need real
    /// defaults (events were Busy, users share Availability, old score rows are algorithm v1) and the status numbers change meaning.
    /// Status numbers: 0 Todo->Backlog, 1 InProgress, 2 Blocked (retired)->Backlog, 3 Done->Completed, 4 Cancelled, 5 Confirmed, 6 Tentative.
    /// </summary>
    public partial class ItemTemplatesStatusesAndTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CalendarSharingLevel",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AlgorithmVersion",
                table: "urgency_score_logs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<double>(
                name: "ScheduledComponent",
                table: "urgency_score_logs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedByUserId",
                table: "calendar_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorKey",
                table: "calendar_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventTemplate",
                table: "calendar_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExternalDescriptionHash",
                table: "calendar_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalStatus",
                table: "calendar_items",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAllDay",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAssignmentLocked",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBusy",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeadlineDefaulted",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "calendar_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesId",
                table: "calendar_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_AssignedByUserId",
                table: "calendar_items",
                column: "AssignedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_calendar_items_users_AssignedByUserId",
                table: "calendar_items",
                column: "AssignedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // "Blocked" stops being a status (it is derived from BlockedByTaskId); the link stays, the item returns to the backlog.
            migrationBuilder.Sql("UPDATE calendar_items SET \"Status\" = 0 WHERE \"Status\" = 2;");
            // Events do not get "done": every live event becomes Confirmed (Cancelled stays Cancelled). TaskType 1 = Event.
            migrationBuilder.Sql("UPDATE calendar_items SET \"Status\" = 5 WHERE \"TaskType\" = 1 AND \"Status\" IN (0, 1, 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Event-only statuses did not exist before: Confirmed/Tentative go back to Todo.
            migrationBuilder.Sql("UPDATE calendar_items SET \"Status\" = 0 WHERE \"Status\" IN (5, 6);");

            migrationBuilder.DropForeignKey(
                name: "FK_calendar_items_users_AssignedByUserId",
                table: "calendar_items");

            migrationBuilder.DropIndex(
                name: "IX_calendar_items_AssignedByUserId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "CalendarSharingLevel",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AlgorithmVersion",
                table: "urgency_score_logs");

            migrationBuilder.DropColumn(
                name: "ScheduledComponent",
                table: "urgency_score_logs");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "ColorKey",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "EventTemplate",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "ExternalDescriptionHash",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "ExternalStatus",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "IsAllDay",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "IsAssignmentLocked",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "IsBusy",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "IsDeadlineDefaulted",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "calendar_items");
        }
    }
}
