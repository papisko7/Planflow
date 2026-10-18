using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoogleEventImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalSettings",
                table: "calendar_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFullSyncAtUtc",
                table: "calendar_integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    DaysBefore = table.Column<int>(type: "integer", nullable: true),
                    TimeOfDayMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_reminders", x => x.Id);
                    table.CheckConstraint("CK_item_reminders_one_form", "(\"OffsetMinutes\" IS NOT NULL AND \"DaysBefore\" IS NULL AND \"TimeOfDayMinutes\" IS NULL) OR (\"OffsetMinutes\" IS NULL AND \"DaysBefore\" IS NOT NULL AND \"TimeOfDayMinutes\" IS NOT NULL AND \"TimeOfDayMinutes\" BETWEEN 0 AND 1439)");
                    table.ForeignKey(
                        name: "FK_item_reminders_calendar_items_CalendarItemId",
                        column: x => x.CalendarItemId,
                        principalTable: "calendar_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_item_reminders_CalendarItemId",
                table: "item_reminders",
                column: "CalendarItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_reminders");

            migrationBuilder.DropColumn(
                name: "ExternalSettings",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "LastFullSyncAtUtc",
                table: "calendar_integrations");
        }
    }
}
