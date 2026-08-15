using System;
using System.Collections.Generic;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Inhabitants;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "abilities",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<int>(type: "integer", nullable: false),
                    unlock_level = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    cost_type = table.Column<int>(type: "integer", nullable: false),
                    cost_value = table.Column<int>(type: "integer", nullable: false),
                    cooldown_pulses = table.Column<long>(type: "bigint", nullable: false),
                    cast_time_pulses = table.Column<long>(type: "bigint", nullable: true),
                    targeting_type = table.Column<int>(type: "integer", nullable: false),
                    effects = table.Column<List<AbilityEffectSpec>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_abilities", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    email = table.Column<string>(type: "citext", maxLength: 320, nullable: false),
                    username = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_banned = table.Column<bool>(type: "boolean", nullable: false),
                    ban_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    muted_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "admin_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    before = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    after = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit", x => x.id);
                });

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
                    table.PrimaryKey("pk_character_quests", x => new { x.character_id, x.quest_key });
                });

            migrationBuilder.CreateTable(
                name: "content_audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entity_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    action = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_audit", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_key = table.Column<string>(type: "text", nullable: false),
                    spawner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_name = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    resolved_stats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    spawn_multipliers = table.Column<Dictionary<string, decimal>>(type: "jsonb", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false),
                    owner_character_id = table.Column<Guid>(type: "uuid", nullable: true),
                    container_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    room_key = table.Column<string>(type: "text", nullable: true),
                    equipped_slot = table.Column<int>(type: "integer", nullable: true),
                    state = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_instances", x => x.id);
                    table.CheckConstraint("ck_item_instance_location", "(num_nonnulls(owner_character_id, container_item_id, room_key) = 1)");
                });

            migrationBuilder.CreateTable(
                name: "item_templates",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: true),
                    weight = table.Column<int>(type: "integer", nullable: false),
                    base_value = table.Column<int>(type: "integer", nullable: false),
                    base_stats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    attack_delay_pulses = table.Column<int>(type: "integer", nullable: true),
                    attack_verb = table.Column<string>(type: "text", nullable: true),
                    is_quest_item = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_templates", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "mob_templates",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    wander_interval_pulses = table.Column<int>(type: "integer", nullable: false),
                    base_stats = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    base_xp = table.Column<int>(type: "integer", nullable: false),
                    base_gold = table.Column<int>(type: "integer", nullable: false),
                    behavior = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    loot = table.Column<List<Dictionary<string, object>>>(type: "jsonb", nullable: false),
                    attacks = table.Column<List<MobAttack>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mob_templates", x => x.key);
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
                    required_item_key = table.Column<string>(type: "text", nullable: true),
                    required_count = table.Column<int>(type: "integer", nullable: false),
                    reward_xp = table.Column<int>(type: "integer", nullable: false),
                    reward_gold = table.Column<int>(type: "integer", nullable: false),
                    reward_item_key = table.Column<string>(type: "text", nullable: true),
                    reward_item_count = table.Column<int>(type: "integer", nullable: false),
                    reward_flag_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    prerequisite_quest_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_repeatable = table.Column<bool>(type: "boolean", nullable: false),
                    auto_start = table.Column<bool>(type: "boolean", nullable: false),
                    dialogue = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quests", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "spawners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_key = table.Column<string>(type: "text", nullable: false),
                    template_key = table.Column<string>(type: "text", nullable: false),
                    template_kind = table.Column<int>(type: "integer", nullable: false),
                    room_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    target_count = table.Column<int>(type: "integer", nullable: false),
                    respawn_seconds = table.Column<int>(type: "integer", nullable: false),
                    wanders = table.Column<bool>(type: "boolean", nullable: true),
                    fights_at_level = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spawners", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "worlds",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    multipliers = table.Column<string>(type: "jsonb", nullable: false),
                    flags = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_worlds", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "characters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 24, nullable: false),
                    path = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    xp = table.Column<long>(type: "bigint", nullable: false),
                    room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rest_state = table.Column<int>(type: "integer", nullable: false),
                    combat_state = table.Column<int>(type: "integer", nullable: false),
                    current_target = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    respawn_room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    gold = table.Column<long>(type: "bigint", nullable: false),
                    flags = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    playtime_seconds = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    vitals = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_characters", x => x.id);
                    table.ForeignKey(
                        name: "fk_characters_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    world_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    min_level = table.Column<int>(type: "integer", nullable: false),
                    max_level = table.Column<int>(type: "integer", nullable: false),
                    multipliers = table.Column<string>(type: "jsonb", nullable: false),
                    flags = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zones", x => x.key);
                    table.ForeignKey(
                        name: "fk_zones_worlds_world_key",
                        column: x => x.world_key,
                        principalTable: "worlds",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    zone_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    flags = table.Column<string>(type: "jsonb", nullable: false),
                    grid = table.Column<List<string>>(type: "text[]", nullable: false),
                    legend = table.Column<string>(type: "jsonb", nullable: false),
                    editor_x = table.Column<int>(type: "integer", nullable: true),
                    editor_y = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.key);
                    table.ForeignKey(
                        name: "fk_rooms_zones_zone_key",
                        column: x => x.zone_key,
                        principalTable: "zones",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "room_exits",
                columns: table => new
                {
                    from_room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    to_room_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    required_flag_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    required_item_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    refusal_message = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_exits", x => new { x.from_room_key, x.direction });
                    table.ForeignKey(
                        name: "fk_room_exits_rooms_from_room_key",
                        column: x => x.from_room_key,
                        principalTable: "rooms",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_abilities_path_unlock_level",
                table: "abilities",
                columns: new[] { "path", "unlock_level" });

            migrationBuilder.CreateIndex(
                name: "ix_abilities_targeting_type",
                table: "abilities",
                column: "targeting_type");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_email",
                table: "accounts",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_username",
                table: "accounts",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_target",
                table: "admin_audit",
                columns: new[] { "target_account_id", "at" },
                descending: new[] { false, true });

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
                name: "ix_characters_account_id",
                table: "characters",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_characters_name",
                table: "characters",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_content_audit_entity",
                table: "content_audit",
                columns: new[] { "entity_kind", "entity_key", "at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_item_instances_owner_character_id",
                table: "item_instances",
                column: "owner_character_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_instances_room_key",
                table: "item_instances",
                column: "room_key");

            migrationBuilder.CreateIndex(
                name: "ix_quests_giver_mob_key",
                table: "quests",
                column: "giver_mob_key");

            migrationBuilder.CreateIndex(
                name: "ix_quests_turnin_mob_key",
                table: "quests",
                column: "turnin_mob_key");

            migrationBuilder.CreateIndex(
                name: "ix_room_exits_to_room_key",
                table: "room_exits",
                column: "to_room_key");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_zone_key",
                table: "rooms",
                column: "zone_key");

            migrationBuilder.CreateIndex(
                name: "ix_spawners_zone_key",
                table: "spawners",
                column: "zone_key");

            migrationBuilder.CreateIndex(
                name: "ix_zones_world_key",
                table: "zones",
                column: "world_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abilities");

            migrationBuilder.DropTable(
                name: "admin_audit");

            migrationBuilder.DropTable(
                name: "character_quests");

            migrationBuilder.DropTable(
                name: "characters");

            migrationBuilder.DropTable(
                name: "content_audit");

            migrationBuilder.DropTable(
                name: "item_instances");

            migrationBuilder.DropTable(
                name: "item_templates");

            migrationBuilder.DropTable(
                name: "mob_templates");

            migrationBuilder.DropTable(
                name: "quests");

            migrationBuilder.DropTable(
                name: "room_exits");

            migrationBuilder.DropTable(
                name: "spawners");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "zones");

            migrationBuilder.DropTable(
                name: "worlds");
        }
    }
}
