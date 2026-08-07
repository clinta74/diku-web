using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DikuWeb.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StandardizeColumnNamingToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Sentinel",
                table: "Spawners",
                newName: "sentinel");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Spawners",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ZoneKey",
                table: "Spawners",
                newName: "zone_key");

            migrationBuilder.RenameColumn(
                name: "TemplateKind",
                table: "Spawners",
                newName: "template_kind");

            migrationBuilder.RenameColumn(
                name: "TemplateKey",
                table: "Spawners",
                newName: "template_key");

            migrationBuilder.RenameColumn(
                name: "TargetCount",
                table: "Spawners",
                newName: "target_count");

            migrationBuilder.RenameColumn(
                name: "RoomKeys",
                table: "Spawners",
                newName: "room_keys");

            migrationBuilder.RenameColumn(
                name: "RespawnSeconds",
                table: "Spawners",
                newName: "respawn_seconds");

            migrationBuilder.RenameIndex(
                name: "IX_Spawners_ZoneKey",
                table: "Spawners",
                newName: "IX_Spawners_zone_key");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "MobTemplates",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Loot",
                table: "MobTemplates",
                newName: "loot");

            migrationBuilder.RenameColumn(
                name: "Level",
                table: "MobTemplates",
                newName: "level");

            migrationBuilder.RenameColumn(
                name: "Icon",
                table: "MobTemplates",
                newName: "icon");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "MobTemplates",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Behavior",
                table: "MobTemplates",
                newName: "behavior");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "MobTemplates",
                newName: "key");

            migrationBuilder.RenameColumn(
                name: "WanderIntervalPulses",
                table: "MobTemplates",
                newName: "wander_interval_pulses");

            migrationBuilder.RenameColumn(
                name: "BaseXp",
                table: "MobTemplates",
                newName: "base_xp");

            migrationBuilder.RenameColumn(
                name: "BaseStats",
                table: "MobTemplates",
                newName: "base_stats");

            migrationBuilder.RenameColumn(
                name: "BaseGold",
                table: "MobTemplates",
                newName: "base_gold");

            migrationBuilder.RenameColumn(
                name: "Vitals_Stamina",
                table: "Mobs",
                newName: "vitals_stamina");

            migrationBuilder.RenameColumn(
                name: "Vitals_Health",
                table: "Mobs",
                newName: "vitals_health");

            migrationBuilder.RenameColumn(
                name: "Vitals_Focus",
                table: "Mobs",
                newName: "vitals_focus");

            migrationBuilder.RenameColumn(
                name: "State",
                table: "Mobs",
                newName: "state");

            migrationBuilder.RenameColumn(
                name: "Level",
                table: "Mobs",
                newName: "level");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Mobs",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "TemplateName",
                table: "Mobs",
                newName: "template_name");

            migrationBuilder.RenameColumn(
                name: "TemplateKey",
                table: "Mobs",
                newName: "template_key");

            migrationBuilder.RenameColumn(
                name: "SpawnerId",
                table: "Mobs",
                newName: "spawner_id");

            migrationBuilder.RenameColumn(
                name: "SpawnMultipliers",
                table: "Mobs",
                newName: "spawn_multipliers");

            migrationBuilder.RenameColumn(
                name: "RoomKey",
                table: "Mobs",
                newName: "room_key");

            migrationBuilder.RenameColumn(
                name: "ResolvedXp",
                table: "Mobs",
                newName: "resolved_xp");

            migrationBuilder.RenameColumn(
                name: "ResolvedStats",
                table: "Mobs",
                newName: "resolved_stats");

            migrationBuilder.RenameColumn(
                name: "ResolvedGold",
                table: "Mobs",
                newName: "resolved_gold");

            migrationBuilder.RenameColumn(
                name: "CurrentTarget",
                table: "Mobs",
                newName: "current_target");

            migrationBuilder.RenameColumn(
                name: "CombatState",
                table: "Mobs",
                newName: "combat_state");

            migrationBuilder.RenameColumn(
                name: "Vitals_StaminaMax",
                table: "Mobs",
                newName: "vitals_stamina_max");

            migrationBuilder.RenameColumn(
                name: "Vitals_HealthMax",
                table: "Mobs",
                newName: "vitals_health_max");

            migrationBuilder.RenameColumn(
                name: "Vitals_FocusMax",
                table: "Mobs",
                newName: "vitals_focus_max");

            migrationBuilder.RenameIndex(
                name: "IX_Mobs_RoomKey",
                table: "Mobs",
                newName: "IX_Mobs_room_key");

            migrationBuilder.RenameColumn(
                name: "Weight",
                table: "ItemTemplates",
                newName: "weight");

            migrationBuilder.RenameColumn(
                name: "Slot",
                table: "ItemTemplates",
                newName: "slot");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ItemTemplates",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Icon",
                table: "ItemTemplates",
                newName: "icon");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "ItemTemplates",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "ItemTemplates",
                newName: "key");

            migrationBuilder.RenameColumn(
                name: "BaseValue",
                table: "ItemTemplates",
                newName: "base_value");

            migrationBuilder.RenameColumn(
                name: "BaseStats",
                table: "ItemTemplates",
                newName: "base_stats");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Abilities",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Abilities",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Key",
                table: "Abilities",
                newName: "key");

            migrationBuilder.RenameColumn(
                name: "TargetingType",
                table: "Abilities",
                newName: "targeting_type");

            migrationBuilder.RenameColumn(
                name: "EffectParams",
                table: "Abilities",
                newName: "effect_params");

            migrationBuilder.RenameColumn(
                name: "EffectKey",
                table: "Abilities",
                newName: "effect_key");

            migrationBuilder.RenameColumn(
                name: "CostValue",
                table: "Abilities",
                newName: "cost_value");

            migrationBuilder.RenameColumn(
                name: "CostType",
                table: "Abilities",
                newName: "cost_type");

            migrationBuilder.RenameColumn(
                name: "CooldownPulses",
                table: "Abilities",
                newName: "cooldown_pulses");

            migrationBuilder.RenameColumn(
                name: "CastTimePulses",
                table: "Abilities",
                newName: "cast_time_pulses");

            migrationBuilder.RenameIndex(
                name: "IX_Abilities_TargetingType",
                table: "Abilities",
                newName: "IX_Abilities_targeting_type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "sentinel",
                table: "Spawners",
                newName: "Sentinel");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Spawners",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "zone_key",
                table: "Spawners",
                newName: "ZoneKey");

            migrationBuilder.RenameColumn(
                name: "template_kind",
                table: "Spawners",
                newName: "TemplateKind");

            migrationBuilder.RenameColumn(
                name: "template_key",
                table: "Spawners",
                newName: "TemplateKey");

            migrationBuilder.RenameColumn(
                name: "target_count",
                table: "Spawners",
                newName: "TargetCount");

            migrationBuilder.RenameColumn(
                name: "room_keys",
                table: "Spawners",
                newName: "RoomKeys");

            migrationBuilder.RenameColumn(
                name: "respawn_seconds",
                table: "Spawners",
                newName: "RespawnSeconds");

            migrationBuilder.RenameIndex(
                name: "IX_Spawners_zone_key",
                table: "Spawners",
                newName: "IX_Spawners_ZoneKey");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "MobTemplates",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "loot",
                table: "MobTemplates",
                newName: "Loot");

            migrationBuilder.RenameColumn(
                name: "level",
                table: "MobTemplates",
                newName: "Level");

            migrationBuilder.RenameColumn(
                name: "icon",
                table: "MobTemplates",
                newName: "Icon");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "MobTemplates",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "behavior",
                table: "MobTemplates",
                newName: "Behavior");

            migrationBuilder.RenameColumn(
                name: "key",
                table: "MobTemplates",
                newName: "Key");

            migrationBuilder.RenameColumn(
                name: "wander_interval_pulses",
                table: "MobTemplates",
                newName: "WanderIntervalPulses");

            migrationBuilder.RenameColumn(
                name: "base_xp",
                table: "MobTemplates",
                newName: "BaseXp");

            migrationBuilder.RenameColumn(
                name: "base_stats",
                table: "MobTemplates",
                newName: "BaseStats");

            migrationBuilder.RenameColumn(
                name: "base_gold",
                table: "MobTemplates",
                newName: "BaseGold");

            migrationBuilder.RenameColumn(
                name: "vitals_stamina",
                table: "Mobs",
                newName: "Vitals_Stamina");

            migrationBuilder.RenameColumn(
                name: "vitals_health",
                table: "Mobs",
                newName: "Vitals_Health");

            migrationBuilder.RenameColumn(
                name: "vitals_focus",
                table: "Mobs",
                newName: "Vitals_Focus");

            migrationBuilder.RenameColumn(
                name: "state",
                table: "Mobs",
                newName: "State");

            migrationBuilder.RenameColumn(
                name: "level",
                table: "Mobs",
                newName: "Level");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "Mobs",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "template_name",
                table: "Mobs",
                newName: "TemplateName");

            migrationBuilder.RenameColumn(
                name: "template_key",
                table: "Mobs",
                newName: "TemplateKey");

            migrationBuilder.RenameColumn(
                name: "spawner_id",
                table: "Mobs",
                newName: "SpawnerId");

            migrationBuilder.RenameColumn(
                name: "spawn_multipliers",
                table: "Mobs",
                newName: "SpawnMultipliers");

            migrationBuilder.RenameColumn(
                name: "room_key",
                table: "Mobs",
                newName: "RoomKey");

            migrationBuilder.RenameColumn(
                name: "resolved_xp",
                table: "Mobs",
                newName: "ResolvedXp");

            migrationBuilder.RenameColumn(
                name: "resolved_stats",
                table: "Mobs",
                newName: "ResolvedStats");

            migrationBuilder.RenameColumn(
                name: "resolved_gold",
                table: "Mobs",
                newName: "ResolvedGold");

            migrationBuilder.RenameColumn(
                name: "current_target",
                table: "Mobs",
                newName: "CurrentTarget");

            migrationBuilder.RenameColumn(
                name: "combat_state",
                table: "Mobs",
                newName: "CombatState");

            migrationBuilder.RenameColumn(
                name: "vitals_stamina_max",
                table: "Mobs",
                newName: "Vitals_StaminaMax");

            migrationBuilder.RenameColumn(
                name: "vitals_health_max",
                table: "Mobs",
                newName: "Vitals_HealthMax");

            migrationBuilder.RenameColumn(
                name: "vitals_focus_max",
                table: "Mobs",
                newName: "Vitals_FocusMax");

            migrationBuilder.RenameIndex(
                name: "IX_Mobs_room_key",
                table: "Mobs",
                newName: "IX_Mobs_RoomKey");

            migrationBuilder.RenameColumn(
                name: "weight",
                table: "ItemTemplates",
                newName: "Weight");

            migrationBuilder.RenameColumn(
                name: "slot",
                table: "ItemTemplates",
                newName: "Slot");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "ItemTemplates",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "icon",
                table: "ItemTemplates",
                newName: "Icon");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "ItemTemplates",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "key",
                table: "ItemTemplates",
                newName: "Key");

            migrationBuilder.RenameColumn(
                name: "base_value",
                table: "ItemTemplates",
                newName: "BaseValue");

            migrationBuilder.RenameColumn(
                name: "base_stats",
                table: "ItemTemplates",
                newName: "BaseStats");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "Abilities",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "Abilities",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "key",
                table: "Abilities",
                newName: "Key");

            migrationBuilder.RenameColumn(
                name: "targeting_type",
                table: "Abilities",
                newName: "TargetingType");

            migrationBuilder.RenameColumn(
                name: "effect_params",
                table: "Abilities",
                newName: "EffectParams");

            migrationBuilder.RenameColumn(
                name: "effect_key",
                table: "Abilities",
                newName: "EffectKey");

            migrationBuilder.RenameColumn(
                name: "cost_value",
                table: "Abilities",
                newName: "CostValue");

            migrationBuilder.RenameColumn(
                name: "cost_type",
                table: "Abilities",
                newName: "CostType");

            migrationBuilder.RenameColumn(
                name: "cooldown_pulses",
                table: "Abilities",
                newName: "CooldownPulses");

            migrationBuilder.RenameColumn(
                name: "cast_time_pulses",
                table: "Abilities",
                newName: "CastTimePulses");

            migrationBuilder.RenameIndex(
                name: "IX_Abilities_targeting_type",
                table: "Abilities",
                newName: "IX_Abilities_TargetingType");
        }
    }
}
