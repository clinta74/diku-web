-- Export the authored world as a re-runnable SQL script.
--
-- MAINTENANCE HAZARD - and it is now guarded, which the rest of this paragraph is the reason for.
-- Every column list below is a hand-written copy of the schema, and it has drifted **five times**:
--
--   1. `spawners.sentinel` was renamed to `wanders` and this went on emitting `sentinel`, so the
--      export failed to load and every existing backup stopped applying.
--   2. `quests.auto_start` was never listed, so a restore reset every quest's auto-start.
--   3. `quests.reward_flag_key` was never listed, so a restore silently cleared the four
--      attunement gates (BUGS.md #6, #7) - the game's only progression lock.
--   4. `item_templates` never listed `is_lore`, `is_no_drop`, `is_light_source` or `paths`, so a
--      restore un-bound every epic reward and put out every lamp.
--   5. `room_exits` never listed `required_flag_key`, `required_item_key` or `refusal_message`, so
--      a restore opened every locked door and portal in the game (§4.15).
--
-- Every one of them was silent, every one was found by diffing this list against the schema rather
-- than by reading it, and four of the five were found by somebody who had come here to do something
-- else. So that diff is now a test: `ExportScriptCompletenessTests` parses this file and fails if
-- any column of the nine tables below is missing from its INSERT. **Add or rename a column and you
-- must edit this file in the same commit** - but the test will say so rather than a restore.
--
-- PLAN.md §6 makes Postgres the only source of truth for content: there are no world files, so
-- everything a builder authored lives in these nine tables and nowhere else. That is fine until
-- the database has to be thrown away - a migration squash, a schema experiment, a fresh start -
-- at which point the world goes with it and the only record of the Sunken Crypt is somebody's
-- memory of having dug it.
--
-- Phase 6's world export/import now exists, and for moving content *between environments* it is
-- the better tool: GET /api/builder/export closes a zone over the templates it needs, and
-- POST /api/builder/import replays the result through the game loop, so live players see it and
-- every entity leaves an audit row. This script is still the right answer to the other question -
-- getting content out when the server will not start, or when what you want back is the exact
-- rows rather than the world they describe. It is deliberately *SQL*, not a dump: the output is
-- readable, diffable, and can be applied to a database that has already been seeded, with no
-- running application in the picture at all.
--
-- What it exports: worlds, zones, rooms, room_exits, mob_templates, item_templates, abilities,
-- spawners, quests. What it does not: accounts, characters, item_instances, character_quests, and
-- the two audit tables - those are player data and history, not content, and a content restore
-- that resurrected deleted characters would be a bug rather than a feature.
--
-- Abilities used to be excluded here too, because `ReconcileAbilitiesAsync` rebuilt them from
-- `AbilityCatalogue` on every startup and an exported row would have been overwritten on the next
-- boot. That reconcile now only plants what is missing, so the table is authoritative and an
-- ability is content like everything else above.
--
-- Every statement is an upsert, which is what makes the output order-independent against the
-- seeder. Apply it to a freshly seeded database and the twelve Millbrook rooms are updated in
-- place with whatever they had actually become; apply it to a bare migrated one and they are
-- inserted. Neither needs to know which happened.
--
--   docker exec dikuweb-postgres psql -U dikuweb -d dikuweb -q -f /path/export-content.sql
--
-- or, from the repo, `tools/export-content.ps1`, which is the same command.
--
-- **`-q` is not optional.** Without it psql echoes "Output format is unaligned." from the \pset
-- below as the first line of the output, and that line is not SQL - re-applying the export then
-- fails immediately with a syntax error, before a single row is restored. The header here
-- documented `-At` for a long time and would have produced exactly that file. Found by running
-- the round trip rather than by reading, which is the argument §6.1 makes for rehearsing a
-- restore instead of believing in one.

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
    'INSERT INTO room_exits (from_room_key, direction, to_room_key, required_flag_key, '
    || 'required_item_key, refusal_message) '
    || 'VALUES (%L, %L, %L, %L, %L, %L) ON CONFLICT (from_room_key, direction) DO UPDATE SET '
    || 'to_room_key = EXCLUDED.to_room_key, required_flag_key = EXCLUDED.required_flag_key, '
    || 'required_item_key = EXCLUDED.required_item_key, refusal_message = EXCLUDED.refusal_message;',
    from_room_key, direction, to_room_key, required_flag_key,
    required_item_key, refusal_message)
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
    'INSERT INTO item_templates (key, name, description, icon, slots, is_two_handed, weight, '
    || 'base_value, base_stats, attack_delay_pulses, attack_verb, is_quest_item, is_lore, '
    || 'is_no_drop, is_light_source, paths) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) '
    || 'ON CONFLICT (key) DO UPDATE SET '
    || 'name = EXCLUDED.name, description = EXCLUDED.description, icon = EXCLUDED.icon, '
    || 'slots = EXCLUDED.slots, is_two_handed = EXCLUDED.is_two_handed, weight = EXCLUDED.weight, '
    || 'base_value = EXCLUDED.base_value, base_stats = EXCLUDED.base_stats, '
    || 'attack_delay_pulses = EXCLUDED.attack_delay_pulses, attack_verb = EXCLUDED.attack_verb, '
    || 'is_quest_item = EXCLUDED.is_quest_item, is_lore = EXCLUDED.is_lore, '
    || 'is_no_drop = EXCLUDED.is_no_drop, is_light_source = EXCLUDED.is_light_source, '
    || 'paths = EXCLUDED.paths;',
    key, name, description, icon, slots, is_two_handed, weight,
    base_value, base_stats, attack_delay_pulses, attack_verb, is_quest_item, is_lore,
    is_no_drop, is_light_source, paths)
from item_templates order by key;

select '';
select '-- abilities';
-- Carried since abilities became authoritative in the table rather than rebuilt from
-- AbilityCatalogue on every startup. Before that an exported ability was noise: the next boot
-- overwrote whatever was restored.
select format(
    'INSERT INTO abilities (key, path, unlock_level, name, description, cost_type, cost_value, '
    || 'cooldown_pulses, cooldown_group, cast_time_pulses, targeting_type, effects) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (key) DO UPDATE SET '
    || 'path = EXCLUDED.path, unlock_level = EXCLUDED.unlock_level, name = EXCLUDED.name, '
    || 'description = EXCLUDED.description, cost_type = EXCLUDED.cost_type, '
    || 'cost_value = EXCLUDED.cost_value, cooldown_pulses = EXCLUDED.cooldown_pulses, '
    || 'cooldown_group = EXCLUDED.cooldown_group, '
    || 'cast_time_pulses = EXCLUDED.cast_time_pulses, targeting_type = EXCLUDED.targeting_type, '
    || 'effects = EXCLUDED.effects;',
    key, path, unlock_level, name, description, cost_type, cost_value,
    cooldown_pulses, cooldown_group, cast_time_pulses, targeting_type, effects)
from abilities order by path, unlock_level, key;

select '';
select '-- spawners';
-- Keyed by id rather than by content, so re-applying an export does not double a population.
select format(
    'INSERT INTO spawners (id, zone_key, template_key, template_kind, room_keys, target_count, '
    || 'respawn_seconds, wanders, fights_at_level) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (id) DO UPDATE SET '
    || 'zone_key = EXCLUDED.zone_key, template_key = EXCLUDED.template_key, '
    || 'template_kind = EXCLUDED.template_kind, room_keys = EXCLUDED.room_keys, '
    || 'target_count = EXCLUDED.target_count, respawn_seconds = EXCLUDED.respawn_seconds, '
    || 'wanders = EXCLUDED.wanders, fights_at_level = EXCLUDED.fights_at_level;',
    id, zone_key, template_key, template_kind, room_keys, target_count,
    respawn_seconds, wanders, fights_at_level)
from spawners order by zone_key, template_key;

select '';
select '-- quests';
select format(
    'INSERT INTO quests (key, zone_key, name, summary, description, giver_mob_key, turnin_mob_key, '
    || 'required_item_key, required_count, reward_xp, reward_gold, reward_item_key, '
    || 'reward_item_count, reward_flag_key, prerequisite_quest_keys, is_repeatable, auto_start, '
    || 'paths, dialogue, sort_order) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) '
    || 'ON CONFLICT (key) DO UPDATE SET '
    || 'zone_key = EXCLUDED.zone_key, name = EXCLUDED.name, summary = EXCLUDED.summary, '
    || 'description = EXCLUDED.description, giver_mob_key = EXCLUDED.giver_mob_key, '
    || 'turnin_mob_key = EXCLUDED.turnin_mob_key, required_item_key = EXCLUDED.required_item_key, '
    || 'required_count = EXCLUDED.required_count, reward_xp = EXCLUDED.reward_xp, '
    || 'reward_gold = EXCLUDED.reward_gold, reward_item_key = EXCLUDED.reward_item_key, '
    || 'reward_item_count = EXCLUDED.reward_item_count, '
    || 'reward_flag_key = EXCLUDED.reward_flag_key, '
    || 'prerequisite_quest_keys = EXCLUDED.prerequisite_quest_keys, '
    || 'is_repeatable = EXCLUDED.is_repeatable, auto_start = EXCLUDED.auto_start, '
    || 'paths = EXCLUDED.paths, '
    || 'dialogue = EXCLUDED.dialogue, sort_order = EXCLUDED.sort_order;',
    key, zone_key, name, summary, description, giver_mob_key, turnin_mob_key,
    required_item_key, required_count, reward_xp, reward_gold, reward_item_key,
    reward_item_count, reward_flag_key, prerequisite_quest_keys, is_repeatable, auto_start,
    paths, dialogue, sort_order)
from quests order by key;

select '';
select 'COMMIT;';
