using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UrgencyAlgorithmV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "ImpactComponent",
                table: "urgency_score_logs",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "urgency_score_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "ImpactScore",
                table: "tasks",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // Calendar-imported tasks were stored with Impact 0 only because nobody could rate them:
            // mark them "not rated" so the new algorithm doesn't treat that 0 as a real rating.
            migrationBuilder.Sql(
                "UPDATE tasks SET \"ImpactScore\" = NULL WHERE \"ExternalCalendarEventId\" IS NOT NULL AND \"ImpactScore\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "urgency_score_logs");

            migrationBuilder.AlterColumn<double>(
                name: "ImpactComponent",
                table: "urgency_score_logs",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ImpactScore",
                table: "tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
