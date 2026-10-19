using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Step 7.5.7. Repeating moves from three columns on every item (an interval in hours that was walked from one finished item to the
    /// next) to a rule of its own: recurrence_series (frequency, interval, weekdays, end, time zone, cursor), which the occurrences point at.
    /// Every item that was still repeating becomes the first occurrence of a series of its own (the series takes the item's id), so nothing
    /// that repeated before stops repeating. An interval that was a whole number of weeks becomes a weekly rule, any other becomes daily.
    /// </summary>
    public partial class RepeatRulesAndSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurrence_series",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskType = table.Column<int>(type: "integer", nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    Weekdays = table.Column<int>(type: "integer", nullable: false),
                    AnchorUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GeneratedThroughUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurrence_series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurrence_series_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "RecurrenceSeriesId",
                table: "calendar_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSeriesException",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_RecurrenceSeriesId",
                table: "calendar_items",
                column: "RecurrenceSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_recurrence_series_GeneratedThroughUtc",
                table: "recurrence_series",
                column: "GeneratedThroughUtc");

            migrationBuilder.CreateIndex(
                name: "IX_recurrence_series_TeamId",
                table: "recurrence_series",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_calendar_items_recurrence_series_RecurrenceSeriesId",
                table: "calendar_items",
                column: "RecurrenceSeriesId",
                principalTable: "recurrence_series",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Data: each item that was still repeating (the live link of its chain) starts a series. Weekly if the interval was whole weeks
            // (Frequency 1), otherwise daily (0) rounded up to whole days; an event repeats from its start, a task from its deadline;
            // the cursor starts at that first occurrence, so the recurring job carries on from there.
            migrationBuilder.Sql(@"
                INSERT INTO recurrence_series (""Id"", ""TeamId"", ""TaskType"", ""Frequency"", ""Interval"", ""Weekdays"", ""AnchorUtc"", ""EndsAtUtc"",
                                               ""TimeZoneId"", ""GeneratedThroughUtc"", ""CreatedAtUtc"")
                SELECT ci.""Id"", ci.""TeamId"", ci.""TaskType"",
                       CASE WHEN ci.""RecurrenceIntervalHours"" % 168 = 0 THEN 1 ELSE 0 END,
                       CASE WHEN ci.""RecurrenceIntervalHours"" % 168 = 0 THEN ci.""RecurrenceIntervalHours"" / 168
                            ELSE GREATEST(1, CEIL(ci.""RecurrenceIntervalHours"" / 24.0)::int) END,
                       0,
                       CASE WHEN ci.""TaskType"" = 1 THEN ci.""StartUtc"" ELSE ci.""DeadlineUtc"" END,
                       ci.""RecurrenceEndsAtUtc"",
                       CASE WHEN ci.""IsAllDay"" THEN NULL ELSE u.""TimeZoneId"" END,
                       CASE WHEN ci.""TaskType"" = 1 THEN ci.""StartUtc"" ELSE ci.""DeadlineUtc"" END,
                       now()
                FROM calendar_items ci
                LEFT JOIN users u ON u.""Id"" = ci.""AssignedUserId""
                WHERE ci.""IsRecurring"" AND ci.""RecurrenceIntervalHours"" > 0
                  AND (CASE WHEN ci.""TaskType"" = 1 THEN ci.""StartUtc"" ELSE ci.""DeadlineUtc"" END) IS NOT NULL;

                UPDATE calendar_items SET ""RecurrenceSeriesId"" = ""Id"" WHERE ""Id"" IN (SELECT ""Id"" FROM recurrence_series);
            ");

            migrationBuilder.DropColumn(
                name: "RecurrenceEndsAtUtc",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "RecurrenceIntervalHours",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "IsRecurring",
                table: "calendar_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRecurring",
                table: "calendar_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceIntervalHours",
                table: "calendar_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceEndsAtUtc",
                table: "calendar_items",
                type: "timestamp with time zone",
                nullable: true);

            // Back to a chain: only the latest occurrence of each series stays "repeating", with the rule as a number of hours
            // (a month counts 30 days, a year 365; the weekdays of a weekly rule are lost).
            migrationBuilder.Sql(@"
                UPDATE calendar_items ci
                SET ""IsRecurring"" = TRUE,
                    ""RecurrenceEndsAtUtc"" = s.""EndsAtUtc"",
                    ""RecurrenceIntervalHours"" = s.""Interval"" * CASE s.""Frequency"" WHEN 0 THEN 24 WHEN 1 THEN 168 WHEN 2 THEN 720 ELSE 8760 END
                FROM recurrence_series s
                WHERE ci.""RecurrenceSeriesId"" = s.""Id""
                  AND (CASE WHEN ci.""TaskType"" = 1 THEN ci.""StartUtc"" ELSE ci.""DeadlineUtc"" END) =
                      (SELECT MAX(CASE WHEN x.""TaskType"" = 1 THEN x.""StartUtc"" ELSE x.""DeadlineUtc"" END)
                       FROM calendar_items x WHERE x.""RecurrenceSeriesId"" = s.""Id"");
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_calendar_items_recurrence_series_RecurrenceSeriesId",
                table: "calendar_items");

            migrationBuilder.DropTable(
                name: "recurrence_series");

            migrationBuilder.DropIndex(
                name: "IX_calendar_items_RecurrenceSeriesId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "RecurrenceSeriesId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "IsSeriesException",
                table: "calendar_items");
        }
    }
}
