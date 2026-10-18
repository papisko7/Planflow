using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncompleteItemsReminderPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncompleteItemsReminder",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing users get the reminder on, exactly like new ones (EF would write false)

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIncompleteReminderSentAtUtc",
                table: "notification_preferences",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncompleteItemsReminder",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "LastIncompleteReminderSentAtUtc",
                table: "notification_preferences");
        }
    }
}
