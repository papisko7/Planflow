using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlanFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnItemLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ItemListId",
                table: "calendar_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_lists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_lists_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_lists_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_calendar_items_ItemListId",
                table: "calendar_items",
                column: "ItemListId");

            migrationBuilder.CreateIndex(
                name: "IX_item_lists_CreatedByUserId",
                table: "item_lists",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_item_lists_TeamId",
                table: "item_lists",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_calendar_items_item_lists_ItemListId",
                table: "calendar_items",
                column: "ItemListId",
                principalTable: "item_lists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_calendar_items_item_lists_ItemListId",
                table: "calendar_items");

            migrationBuilder.DropTable(
                name: "item_lists");

            migrationBuilder.DropIndex(
                name: "IX_calendar_items_ItemListId",
                table: "calendar_items");

            migrationBuilder.DropColumn(
                name: "ItemListId",
                table: "calendar_items");
        }
    }
}
