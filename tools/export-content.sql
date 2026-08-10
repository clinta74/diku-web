-- Export the authored world as a re-runnable SQL script.
--
-- PLAN.md §6 makes Postgres the only source of truth for content: there are no world files, so
-- everything a builder authored lives in these eight tables and nowhere else. That is fine until
-- the database has to be thrown away - a migration squash, a schema experiment, a fresh start -
-- at which point the world goes with it and the only record of the Sunken Crypt is somebody's
-- memory of having dug it.
--
-- This is the stopgap until Phase 6's world export/import (JSON) exists. It is deliberately
-- *SQL*, not a dump: the output is readable, diffable, and can be applied to a database that has
-- already been seeded.
--
-- What it exports: worlds, zones, rooms, room_exits, mob_templates, item_templates, spawners,
-- quests. What it does not: accounts, characters, item_instances, character_quests, and the two
-- audit tables - those are player data and history, not content, and a content restore that
-- resurrected deleted characters would be a bug rather than a feature. Abilities are absent for
-- a different reason: `ReconcileAbilitiesAsync` rebuilds them from `AbilityCatalogue` on every
-- startup, so exporting them would restore rows the next boot only has to correct.
--
-- Every statement is an upsert, which is what makes the output order-independent against the
-- seeder. Apply it to a freshly seeded database and the twelve Millbrook rooms are updated in
-- place with whatever they had actually become; apply it to a bare migrated one and they are
-- inserted. Neither needs to know which happened.
--
--   docker exec dikuweb-postgres psql -U dikuweb -d dikuweb -At -f /path/export-content.sql
--
-- or, from the repo, `tools/export-content.ps1`.

\pset tuples_only on
\pset format unaligned

select '-- diku-web content export, ' || now()::timestamptz(0);
select '-- Re-runnable: every statement is an upsert.';
select 'BEGIN;';

select '';
select '-- worlds';
select format(
    'INSERT INTO worlds (key, name, description, sort_order, multipliers, flags) '
    || 'VALUES (%L, %L, %L, %L, %L, %L) ON CONFLICT (key) DO UPDATE SET '
    || 'name = EXCLUDED.name, description = EXCLUDED.description, '
    || 'sort_order = EXCLUDED.sort_order, multipliers = EXCLUDED.multipliers, '
    || 'flags = EXCLUDED.flags;',
    key, name, description, sort_order, multipliers, flags)
from worlds order by key;

select '';
select '-- zones';
select format(
    'INSERT INTO zones (key, world_key, name, description, min_level, max_level, multipliers, flags) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (key) DO UPDATE SET '
    || 'world_key = EXCLUDED.world_key, name = EXCLUDED.name, description = EXCLUDED.description, '
    || 'min_level = EXCLUDED.min_level, max_level = EXCLUDED.max_level, '
    || 'multipliers = EXCLUDED.multipliers, flags = EXCLUDED.flags;',
    key, world_key, name, description, min_level, max_level, multipliers, flags)
from zones order by key;

select '';
select '-- rooms';
select format(
    'INSERT INTO rooms (key, zone_key, title, description, flags, grid, legend, editor_x, editor_y) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (key) DO UPDATE SET '
    || 'zone_key = EXCLUDED.zone_key, title = EXCLUDED.title, description = EXCLUDED.description, '
    || 'flags = EXCLUDED.flags, grid = EXCLUDED.grid, legend = EXCLUDED.legend, '
    || 'editor_x = EXCLUDED.editor_x, editor_y = EXCLUDED.editor_y;',
    key, zone_key, title, description, flags, grid, legend, editor_x, editor_y)
from rooms order by key;

select '';
select '-- room_exits';
-- to_room_key is deliberately not a foreign key (§6), so an exit may point at a room that does
-- not exist yet. Restoring one is therefore always safe, whatever order the rooms arrived in.
select format(
    'INSERT INTO room_exits (from_room_key, direction, to_room_key) '
    || 'VALUES (%L, %L, %L) ON CONFLICT (from_room_key, direction) DO UPDATE SET '
    || 'to_room_key = EXCLUDED.to_room_key;',
    from_room_key, direction, to_room_key)
from room_exits order by from_room_key, direction;

select '';
select '-- mob_templates';
select format(
    'INSERT INTO mob_templates (key, name, description, icon, level, wander_interval_pulses, '
    || 'base_stats, base_xp, base_gold, behavior, loot, attacks) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (key) DO UPDATE SET '
    || 'name = EXCLUDED.name, description = EXCLUDED.description, icon = EXCLUDED.icon, '
    || 'level = EXCLUDED.level, wander_interval_pulses = EXCLUDED.wander_interval_pulses, '
    || 'base_stats = EXCLUDED.base_stats, base_xp = EXCLUDED.base_xp, base_gold = EXCLUDED.base_gold, '
    || 'behavior = EXCLUDED.behavior, loot = EXCLUDED.loot, attacks = EXCLUDED.attacks;',
    key, name, description, icon, level, wander_interval_pulses,
    base_stats, base_xp, base_gold, behavior, loot, attacks)
from mob_templates order by key;

select '';
select '-- item_templates';
select format(
    'INSERT INTO item_templates (key, name, description, icon, slot, weight, base_value, '
    || 'base_stats, attack_delay_pulses, attack_verb, is_quest_item) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (key) DO UPDATE SET '
    || 'name = EXCLUDED.name, description = EXCLUDED.description, icon = EXCLUDED.icon, '
    || 'slot = EXCLUDED.slot, weight = EXCLUDED.weight, base_value = EXCLUDED.base_value, '
    || 'base_stats = EXCLUDED.base_stats, attack_delay_pulses = EXCLUDED.attack_delay_pulses, '
    || 'attack_verb = EXCLUDED.attack_verb, is_quest_item = EXCLUDED.is_quest_item;',
    key, name, description, icon, slot, weight, base_value,
    base_stats, attack_delay_pulses, attack_verb, is_quest_item)
from item_templates order by key;

select '';
select '-- spawners';
-- Keyed by id rather than by content, so re-applying an export does not double a population.
select format(
    'INSERT INTO spawners (id, zone_key, template_key, template_kind, room_keys, target_count, '
    || 'respawn_seconds, sentinel) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (id) DO UPDATE SET '
    || 'zone_key = EXCLUDED.zone_key, template_key = EXCLUDED.template_key, '
    || 'template_kind = EXCLUDED.template_kind, room_keys = EXCLUDED.room_keys, '
    || 'target_count = EXCLUDED.target_count, respawn_seconds = EXCLUDED.respawn_seconds, '
    || 'sentinel = EXCLUDED.sentinel;',
    id, zone_key, template_key, template_kind, room_keys, target_count,
    respawn_seconds, sentinel)
from spawners order by zone_key, template_key;

select '';
select '-- quests';
select format(
    'INSERT INTO quests (key, zone_key, name, summary, description, giver_mob_key, turnin_mob_key, '
    || 'required_item_key, required_count, reward_xp, reward_gold, reward_item_key, '
    || 'reward_item_count, prerequisite_quest_keys, is_repeatable, dialogue, sort_order) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) '
    || 'ON CONFLICT (key) DO UPDATE SET '
    || 'zone_key = EXCLUDED.zone_key, name = EXCLUDED.name, summary = EXCLUDED.summary, '
    || 'description = EXCLUDED.description, giver_mob_key = EXCLUDED.giver_mob_key, '
    || 'turnin_mob_key = EXCLUDED.turnin_mob_key, required_item_key = EXCLUDED.required_item_key, '
    || 'required_count = EXCLUDED.required_count, reward_xp = EXCLUDED.reward_xp, '
    || 'reward_gold = EXCLUDED.reward_gold, reward_item_key = EXCLUDED.reward_item_key, '
    || 'reward_item_count = EXCLUDED.reward_item_count, '
    || 'prerequisite_quest_keys = EXCLUDED.prerequisite_quest_keys, '
    || 'is_repeatable = EXCLUDED.is_repeatable, dialogue = EXCLUDED.dialogue, '
    || 'sort_order = EXCLUDED.sort_order;',
    key, zone_key, name, summary, description, giver_mob_key, turnin_mob_key,
    required_item_key, required_count, reward_xp, reward_gold, reward_item_key,
    reward_item_count, prerequisite_quest_keys, is_repeatable, dialogue, sort_order)
from quests order by key;

select '';
select 'COMMIT;';
