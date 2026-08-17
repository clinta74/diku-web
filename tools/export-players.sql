-- Export one account and its characters as a re-runnable SQL script.
--
-- The complement of export-content.sql, which says in its own header that it deliberately does
-- *not* carry accounts, characters, item_instances or character_quests, because those are player
-- data rather than content. This is the other half, and the two are kept apart on purpose: content
-- moves between environments constantly, and a character moves once.
--
-- MAINTENANCE HAZARD, the same one export-content.sql opens with: every column list below is a
-- hand-written copy of the schema and nothing compiles or tests it. Add or rename a column on
-- accounts, characters, item_instances or character_quests and you must edit this file in the same
-- commit. The failure mode is quiet - a column that stops being emitted restores as its default,
-- which is how `quests.auto_start` was silently reset on every restore for months.
--
-- WHAT IT CARRIES
--   accounts           the one row named by :account
--   characters         every character on it, deleted ones included (see below)
--   item_instances     everything those characters own, walked recursively through containers
--   character_quests   their journals, progress and repeat counts
--
-- WHAT IT DOES NOT
--   Content of any kind. Rooms, templates, quests-as-authored, spawners and abilities are the
--   other script's job, or `POST /api/builder/import`. **Run the world in first.** A character
--   whose room_key names a room the target does not have is relocated to the configured starting
--   room on entry (GameLoop logs RelocatedFromMissingRoom), which is graceful but is not what you
--   wanted; an item whose template_key is missing keeps its resolved stats and loses its rules.
--
--   Items lying on the ground. Those belong to the world's population, not to a player, and the
--   spawners on the target will make their own.
--
-- DELETED CHARACTERS TRAVEL. `deleted_at` is a soft delete and the row is the record of it; a move
-- that dropped them would let a retired name be taken again on the target and would lose the
-- history. They stay invisible in the character list exactly as they were here.
--
-- THE THREE THINGS THIS REFUSES TO DO, all enforced in the generated SQL rather than here, so they
-- still hold when somebody applies the file by hand:
--
--   1. Overwrite a character belonging to somebody else. `characters.name` is unique, so a name
--      already taken on the target under a *different* id is a hard stop. Silently updating it
--      would hand one player another player's character.
--   2. Overwrite an account that is not this one. Same argument for `email` and `username`, both
--      unique.
--   3. Half-apply. Everything is inside one transaction, so a target that refuses at any point is
--      left exactly as it was.
--
-- IT IS A MOVE, NOT A MERGE, AND THAT IS DESTRUCTIVE ON RE-RUN. Applying this replaces the moved
-- characters' items and quests with the ones in this file - scoped strictly to the character ids
-- being moved, and to nothing else on the target. That is what makes a second run produce the same
-- state rather than a pile of ghosts: an item sold here after the first run would otherwise still
-- be equipped there. If those characters have been *played* on the target since, their progress
-- since is what you are throwing away.
--
--   docker exec dikuweb-postgres psql -U dikuweb -d dikuweb -q \
--     -v account=clint -f /path/export-players.sql
--
-- or, from the repo, `tools/export-players.ps1`, which is the same command and writes the file.
--
-- **`-q` is not optional**, for the reason the content export learned the hard way: without it psql
-- echoes "Output format is unaligned." from the \pset below as the first line of the output, that
-- line is not SQL, and re-applying the export fails on it before a single row is restored.

\if :{?account}
\else
\warn 'export-players.sql: pass -v account=<username or email>'
\quit 1
\endif

\pset tuples_only on
\pset format unaligned

-- Everything below reads from these two, so the account is resolved exactly once. A temp table
-- rather than repeating the predicate: the guards and the deletes have to name the same set of
-- characters the inserts do, and a copy-pasted WHERE clause is how they stop agreeing.
create temporary table _move_account as
select * from accounts
where username = :'account' or email = :'account';

create temporary table _move_characters as
select c.* from characters c join _move_account a on a.id = c.account_id;

-- Walked recursively, so an item inside a bag inside a chest comes too. A contained item has a
-- null owner_character_id - the check constraint allows exactly one of the three locations - so a
-- flat join on ownership would carry the bag and leave everything in it behind.
create temporary table _move_items as
with recursive owned as (
    select i.* from item_instances i join _move_characters c on c.id = i.owner_character_id
    union all
    select i.* from item_instances i join owned o on i.container_item_id = o.id
)
select * from owned;

-- Stop here rather than writing a file with nothing in it. An export that "succeeded" and carries
-- no rows is the one failure that looks like success right up until somebody applies it.
select case when count(*) = 1 then 'true' else 'false' end as matched from _move_account
\gset

\if :matched
\else
\warn 'export-players.sql: no account matched, or more than one did'
\quit
\endif

select '-- diku-web player export, ' || now()::timestamptz(0);
select '-- account: ' || username || ' <' || email || '>' from _move_account;
select format('-- %s characters, %s items, %s quest rows',
    (select count(*) from _move_characters),
    (select count(*) from _move_items),
    (select count(*) from character_quests q
        join _move_characters c on c.id = q.character_id));
select '-- Re-runnable. Replaces the moved characters'' items and quests; touches nothing else.';
select '';
select 'BEGIN;';

-- ---------------------------------------------------------------------------
-- Guards. First, because the point of them is that nothing has happened yet.
-- ---------------------------------------------------------------------------

select '';
select '-- Refuse if a name on this server belongs to somebody else.';
-- Every fragment carries its own E prefix. Postgres applies the escape-string flag per literal, so
-- `E'a\n' || 'b\n'` puts a real newline after a and a backslash-n after b — and a backslash-n
-- inside a dollar-quoted plpgsql body is a syntax error at the far end, in a file nothing here
-- will parse again.
select format(
    E'DO $guard$\n'
    || E'DECLARE clash text;\n'
    || E'BEGIN\n'
    || E'  SELECT string_agg(name, '', '' ORDER BY name) INTO clash FROM characters\n'
    || E'   WHERE name IN (%s) AND id NOT IN (%s);\n'
    || E'  IF clash IS NOT NULL THEN\n'
    || E'    RAISE EXCEPTION ''refusing: character name(s) already held by another account here: %%'', clash;\n'
    || E'  END IF;\n'
    || E'END $guard$;',
    (select string_agg(format('%L', name), ', ' order by name) from _move_characters),
    (select string_agg(format('%L', id), ', ' order by id) from _move_characters));

select '';
select '-- Refuse if this account''s email or username belongs to a different account here.';
-- The values go in as RAISE arguments rather than being interpolated into the message. Written
-- the other way, %L put a quoted literal inside an already-quoted string and closed it early:
-- RAISE EXCEPTION 'refusing: 'clint@example.com' or ...' does not parse.
select format(
    E'DO $guard$\n'
    || E'BEGIN\n'
    || E'  IF EXISTS (SELECT 1 FROM accounts\n'
    || E'              WHERE (email = %L OR username = %L) AND id <> %L) THEN\n'
    || E'    RAISE EXCEPTION ''refusing: %% or %% already belongs to a different account here'', %L, %L;\n'
    || E'  END IF;\n'
    || E'END $guard$;',
    email, username, id, email, username)
from _move_account;

-- ---------------------------------------------------------------------------
-- The account. Left alone if it is already here.
-- ---------------------------------------------------------------------------

select '';
select '-- account (DO NOTHING on conflict: the target''s own password and role win)';
select format(
    'INSERT INTO accounts (id, email, username, password_hash, password_changed_at, role, '
    || 'created_at, last_login_at, is_banned, ban_reason, muted_until) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) ON CONFLICT (id) DO NOTHING;',
    id, email, username, password_hash, password_changed_at, role,
    created_at, last_login_at, is_banned, ban_reason, muted_until)
from _move_account;

-- ---------------------------------------------------------------------------
-- Characters
-- ---------------------------------------------------------------------------

select '';
select '-- characters';
select format(
    'INSERT INTO characters (id, account_id, name, path, level, xp, room_key, rest_state, '
    || 'combat_state, current_target, respawn_room_key, gold, flags, created_at, last_played_at, '
    || 'playtime_seconds, deleted_at, attributes, vitals) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) '
    || 'ON CONFLICT (id) DO UPDATE SET '
    || 'account_id = EXCLUDED.account_id, name = EXCLUDED.name, path = EXCLUDED.path, '
    || 'level = EXCLUDED.level, xp = EXCLUDED.xp, room_key = EXCLUDED.room_key, '
    || 'rest_state = EXCLUDED.rest_state, combat_state = EXCLUDED.combat_state, '
    || 'current_target = EXCLUDED.current_target, respawn_room_key = EXCLUDED.respawn_room_key, '
    || 'gold = EXCLUDED.gold, flags = EXCLUDED.flags, created_at = EXCLUDED.created_at, '
    || 'last_played_at = EXCLUDED.last_played_at, playtime_seconds = EXCLUDED.playtime_seconds, '
    || 'deleted_at = EXCLUDED.deleted_at, attributes = EXCLUDED.attributes, '
    || 'vitals = EXCLUDED.vitals;',
    id, account_id, name, path, level, xp, room_key, rest_state,
    combat_state, current_target, respawn_room_key, gold, flags, created_at, last_played_at,
    playtime_seconds, deleted_at, attributes, vitals)
from _move_characters order by created_at;

-- ---------------------------------------------------------------------------
-- Items. Cleared first, so a re-run is the same move rather than an accumulation.
-- ---------------------------------------------------------------------------

select '';
select '-- items: cleared and rewritten, scoped to these characters only';

-- Containers before their contents on the way in, and the reverse on the way out. There is no
-- foreign key on container_item_id to enforce it - the ordering is for a reader following what
-- happened, and for the day somebody adds the constraint.
select format(
    'DELETE FROM item_instances WHERE owner_character_id IN (%s);',
    (select string_agg(format('%L', id), ', ' order by id) from _move_characters));

select format(
    'INSERT INTO item_instances (id, template_key, spawner_id, template_name, icon, '
    || 'resolved_stats, spawn_multipliers, value, owner_character_id, container_item_id, '
    || 'room_key, equipped_slot, state) '
    || 'VALUES (%L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L, %L) '
    || 'ON CONFLICT (id) DO UPDATE SET '
    || 'template_key = EXCLUDED.template_key, spawner_id = EXCLUDED.spawner_id, '
    || 'template_name = EXCLUDED.template_name, icon = EXCLUDED.icon, '
    || 'resolved_stats = EXCLUDED.resolved_stats, '
    || 'spawn_multipliers = EXCLUDED.spawn_multipliers, value = EXCLUDED.value, '
    || 'owner_character_id = EXCLUDED.owner_character_id, '
    || 'container_item_id = EXCLUDED.container_item_id, room_key = EXCLUDED.room_key, '
    || 'equipped_slot = EXCLUDED.equipped_slot, state = EXCLUDED.state;',
    id, template_key, spawner_id, template_name, icon,
    resolved_stats, spawn_multipliers, value, owner_character_id, container_item_id,
    room_key, equipped_slot, state)
from _move_items order by (container_item_id is not null), id;

-- ---------------------------------------------------------------------------
-- Quests
-- ---------------------------------------------------------------------------

select '';
select '-- quest journals: cleared and rewritten, scoped to these characters only';
select format(
    'DELETE FROM character_quests WHERE character_id IN (%s);',
    (select string_agg(format('%L', id), ', ' order by id) from _move_characters));

select format(
    'INSERT INTO character_quests (character_id, quest_key, status, started_at, completed_at, '
    || 'times_completed) VALUES (%L, %L, %L, %L, %L, %L) '
    || 'ON CONFLICT (character_id, quest_key) DO UPDATE SET '
    || 'status = EXCLUDED.status, started_at = EXCLUDED.started_at, '
    || 'completed_at = EXCLUDED.completed_at, times_completed = EXCLUDED.times_completed;',
    q.character_id, q.quest_key, q.status, q.started_at, q.completed_at, q.times_completed)
from character_quests q
join _move_characters c on c.id = q.character_id
order by q.character_id, q.quest_key;

-- ---------------------------------------------------------------------------
-- What the target should say afterwards, so applying it reports rather than being silent.
-- ---------------------------------------------------------------------------

select '';
select format(
    E'DO $report$\n'
    || E'BEGIN\n'
    || E'  RAISE NOTICE ''moved %%: %% characters, %% items, %% quest rows'',\n'
    || E'    (SELECT username FROM accounts WHERE id = %L),\n'
    || E'    (SELECT count(*) FROM characters WHERE account_id = %L),\n'
    || E'    (SELECT count(*) FROM item_instances i JOIN characters c ON c.id = i.owner_character_id\n'
    || E'      WHERE c.account_id = %L),\n'
    || E'    (SELECT count(*) FROM character_quests q JOIN characters c ON c.id = q.character_id\n'
    || E'      WHERE c.account_id = %L);\n'
    || E'END $report$;',
    id, id, id, id)
from _move_account;

select '';
select 'COMMIT;';
