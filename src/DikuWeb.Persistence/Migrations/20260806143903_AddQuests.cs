using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "character_quests",
                columns: table => new
                {
                    character_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quest_key = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    times_completed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_character_quests", x => new { x.character_id, x.quest_key });
                });

            migrationBuilder.CreateTable(
                name: "quests",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    zone_key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    giver_mob_key = table.Column<string>(type: "text", nullable: false),
                    turnin_mob_key = table.Column<string>(type: "text", nullable: false),
                    required_item_key = table.Column<string>(type: "text", nullable: false),
                    required_count = table.Column<int>(type: "integer", nullable: false),
                    reward_xp = table.Column<int>(type: "integer", nullable: false),
                    reward_gold = table.Column<int>(type: "integer", nullable: false),
                    reward_item_key = table.Column<string>(type: "text", nullable: true),
                    reward_item_count = table.Column<int>(type: "integer", nullable: false),
                    prerequisite_quest_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_repeatable = table.Column<bool>(type: "boolean", nullable: false),
                    dialogue = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quests", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_character_quests_character_id",
                table: "character_quests",
                column: "character_id");

            migrationBuilder.CreateIndex(
                name: "ix_character_quests_character_id_status",
                table: "character_quests",
                columns: new[] { "character_id", "status" },
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_quests_giver_mob_key",
                table: "quests",
                column: "giver_mob_key");

            migrationBuilder.CreateIndex(
                name: "ix_quests_turnin_mob_key",
                table: "quests",
                column: "turnin_mob_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "character_quests");

            migrationBuilder.DropTable(
                name: "quests");
        }
    }
}
