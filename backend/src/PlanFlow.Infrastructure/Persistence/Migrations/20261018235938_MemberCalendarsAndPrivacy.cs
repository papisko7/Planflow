using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberCalendarsAndPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "calendar_items",
                type: "boolean",
                nullable: true);

            // Events imported before this column existed have unknown privacy (read as private). Forgetting the last full pass makes
            // the next sync re-read every event in the window, which fills the flag in.
            migrationBuilder.Sql("UPDATE calendar_integrations SET \"LastFullSyncAtUtc\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "calendar_items");
        }
    }
}
