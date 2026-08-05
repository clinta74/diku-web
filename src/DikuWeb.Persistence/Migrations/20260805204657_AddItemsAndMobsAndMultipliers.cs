using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddItemsAndMobsAndMultipliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "multipliers",
                table: "zones",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "multipliers",
                table: "worlds",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "text", nullable: false),
                    resolved_stats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    spawn_multipliers = table.Column<Dictionary<string, decimal>>(type: "jsonb", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false),
                    owner_character_id = table.Column<Guid>(type: "uuid", nullable: true),
                    container_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    room_key = table.Column<string>(type: "text", nullable: true),
                    equipped_slot = table.Column<int>(type: "integer", nullable: true),
                    State = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                    table.CheckConstraint("ck_item_instance_location", "(num_nonnulls(owner_character_id, container_item_id, room_key) = 1)");
                });

            migrationBuilder.CreateTable(
                name: "ItemTemplates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: true),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    BaseValue = table.Column<int>(type: "integer", nullable: false),
                    BaseStats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTemplates", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Mobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateKey = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    RoomKey = table.Column<string>(type: "text", nullable: false),
                    ResolvedStats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    SpawnMultipliers = table.Column<Dictionary<string, decimal>>(type: "jsonb", nullable: false),
                    ResolvedXp = table.Column<int>(type: "integer", nullable: false),
                    ResolvedGold = table.Column<int>(type: "integer", nullable: false),
                    Vitals_Health = table.Column<int>(type: "integer", nullable: false),
                    Vitals_HealthMax = table.Column<int>(type: "integer", nullable: false),
                    Vitals_Focus = table.Column<int>(type: "integer", nullable: false),
                    Vitals_FocusMax = table.Column<int>(type: "integer", nullable: false),
                    Vitals_Stamina = table.Column<int>(type: "integer", nullable: false),
                    Vitals_StaminaMax = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MobTemplates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    BaseStats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    BaseXp = table.Column<int>(type: "integer", nullable: false),
                    BaseGold = table.Column<int>(type: "integer", nullable: false),
                    Behavior = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    Loot = table.Column<List<Dictionary<string, object>>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobTemplates", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Spawners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneKey = table.Column<string>(type: "text", nullable: false),
                    TemplateKey = table.Column<string>(type: "text", nullable: false),
                    TemplateKind = table.Column<int>(type: "integer", nullable: false),
                    RoomKeys = table.Column<List<string>>(type: "text[]", nullable: false),
                    TargetCount = table.Column<int>(type: "integer", nullable: false),
                    RespawnSeconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Spawners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_owner_character_id",
                table: "ItemInstances",
                column: "owner_character_id");

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_room_key",
                table: "ItemInstances",
                column: "room_key");

            migrationBuilder.CreateIndex(
                name: "IX_Mobs_RoomKey",
                table: "Mobs",
                column: "RoomKey");

            migrationBuilder.CreateIndex(
                name: "IX_Spawners_ZoneKey",
                table: "Spawners",
                column: "ZoneKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemInstances");

            migrationBuilder.DropTable(
                name: "ItemTemplates");

            migrationBuilder.DropTable(
                name: "Mobs");

            migrationBuilder.DropTable(
                name: "MobTemplates");

            migrationBuilder.DropTable(
                name: "Spawners");

            migrationBuilder.DropColumn(
                name: "multipliers",
                table: "zones");

            migrationBuilder.DropColumn(
                name: "multipliers",
                table: "worlds");
        }
    }
}
