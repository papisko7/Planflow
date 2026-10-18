using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ItemAlertsAndParticipants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ItemReminders",
                table: "notification_preferences",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing users get reminders on, exactly like new ones (EF would write false)

            migrationBuilder.AddColumn<int>(
                name: "Anchor",
                table: "item_reminders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFiredForUtc",
                table: "item_reminders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_participants_calendar_items_CalendarItemId",
                        column: x => x.CalendarItemId,
                        principalTable: "calendar_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_participants_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_participants_CalendarItemId_UserId",
                table: "item_participants",
                columns: new[] { "CalendarItemId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_participants_UserId",
                table: "item_participants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_participants");

            migrationBuilder.DropColumn(
                name: "ItemReminders",
                table: "notification_preferences");

            migrationBuilder.DropColumn(
                name: "Anchor",
                table: "item_reminders");

            migrationBuilder.DropColumn(
                name: "LastFiredForUtc",
                table: "item_reminders");
        }
    }
}
