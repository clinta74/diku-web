#!/usr/bin/env python3
"""Checks a WorldBundle JSON file before it is imported.

    python tools/check-bundle.py content/ossara/gatetown.json

This is a *pre-flight* check, not a replacement for `POST /api/builder/import?dryRun=true`.
The dry run is authoritative: it knows what is already in the target database, and it is the
same code path a real import takes. This runs with no server and no database, which is what
makes it useful in an editor loop and in CI.

Three of the checks here are ones the dry run deliberately does not make, because they are
authoring mistakes rather than import failures:

  - **Reciprocity.** An import applies `SetExit` per edge and never invents the return, since an
    export already carries both halves. So a bundle that only says `north` produces a one-way
    corridor, which imports perfectly and reads as a bug the first time somebody walks it.
  - **Connectivity.** A room with no path to the rest of its zone imports fine and is reachable
    only by `goto`.
  - **Room inside its own zone.** `ossara.gatetown.x` declaring `zoneKey: ossara.brackenfell`
    is legal to the engine and is almost always a copy-paste.

Everything else here overlaps the importer's own validation and exists to catch it a minute
earlier. Exit status is 1 if anything is reported as an error.
"""
import json
import re
import sys
from pathlib import Path

SEGMENT = re.compile(r'^[a-z0-9]([a-z0-9-]*[a-z0-9])?$')
MAX_KEY = 128

OPPOSITE = {
    'north': 'south', 'south': 'north', 'east': 'west', 'west': 'east',
    'up': 'down', 'down': 'up',
    'northeast': 'southwest', 'southwest': 'northeast',
    'northwest': 'southeast', 'southeast': 'northwest',
}

# The one format version this repo's server reads. Kept in step with
# WorldBundle.CurrentFormatVersion by hand, because a mismatch is the single hard refusal in the
# import path and finding it here beats finding it in an HTTP 400. 8 added item restrictions.
FORMAT_VERSION = 9

REPO = Path(__file__).resolve().parent.parent


def registered_flags():
    """The flag keys from RoomFlags.cs, so this cannot drift from the registry."""
    source = REPO / 'src' / 'DikuWeb.Domain' / 'Worlds' / 'RoomFlags.cs'
    try:
        text = source.read_text(encoding='utf-8')
    except OSError:
        return None
    return set(re.findall(r'^\s*"([a-zA-Z][a-zA-Z0-9]*)",\s*"', text, re.MULTILINE)) or None


def non_placeable_tiles():
    """The tile names nothing is drawn on, read out of RoomLayoutService rather than copied.

    Read from source for the same reason the flags are: a second transcription is a second thing
    to forget. A legend calling a pillar "column" puts a rat inside it, and nothing at runtime
    complains - the mob is simply placed somewhere a player can see it should not be.
    """
    source = REPO / 'src' / 'DikuWeb.Engine' / 'Presentation' / 'RoomLayoutService.cs'
    try:
        text = source.read_text(encoding='utf-8')
    except OSError:
        return None

    block = re.search(r'NonPlaceableTiles\s*=\s*new\([^)]*\)\s*\{(.*?)\};', text, re.S)
    return set(re.findall(r'"([a-z]+)"', block.group(1))) if block else None


def dialogue_keys():
    """The dialogue keys QuestCommands actually looks up, read out of the source.

    This is the check that would have caught the largest content defect found so far. Quest
    dialogue is a free `Dictionary<string, string>` and passes through the importer, the writer and
    the applier untouched, so no layer of the round trip is in a position to notice a key nobody
    reads. All 35 authored quests used `offer` / `progress` / `complete` / `already` against an
    engine reading `giverOffer` / `giverInProgress` / `giverComplete` / `turninReady` - zero
    overlap, ~137 lines of prose replaced by four generic templates, and every test green.

    Read from source rather than listed here for the reason the flags and tiles are: a second
    transcription is a second thing to forget.
    """
    source = REPO / 'src' / 'DikuWeb.Engine' / 'Commands' / 'QuestCommands.cs'
    try:
        text = source.read_text(encoding='utf-8')
    except OSError:
        return None
    return set(re.findall(r'Dialogue\.TryGetValue\("([a-zA-Z]+)"', text)) or None


def behavior_keys():
    """The mob behavior-bag keys the engine reads, harvested from MobBehavior.

    Same argument, one bag over. `MobBehavior` is the single place the engine names these, so a
    key that appears in content and not here reaches nothing.
    """
    source = REPO / 'src' / 'DikuWeb.Engine' / 'Inhabitants' / 'MobBehavior.cs'
    try:
        text = source.read_text(encoding='utf-8')
    except OSError:
        return None
    return set(re.findall(r'"([a-z][a-zA-Z]*)"', text)) or None


# The fewest open cells a room may leave. Entities are placed only on open ground and are simply
# not drawn when there is none, so a room that is all water is a room whose occupants vanish.
MIN_OPEN_CELLS = 40


def check(path):
    errors, warnings = [], []

    def error(message):
        errors.append(message)

    def warn(message):
        warnings.append(message)

    bundle = json.loads(Path(path).read_text(encoding='utf-8'))

    if bundle.get('formatVersion') != FORMAT_VERSION:
        error('formatVersion is %r; this server reads %d'
              % (bundle.get('formatVersion'), FORMAT_VERSION))

    worlds = {w['key'] for w in bundle.get('worlds') or []}
    zones = {z['key'] for z in bundle.get('zones') or []}
    items = {i['key'] for i in bundle.get('itemTemplates') or []}
    mobs = {m['key'] for m in bundle.get('mobTemplates') or []}
    rooms = {r['key'] for r in bundle.get('rooms') or []}

    for zone in bundle.get('zones') or []:
        if not zone['key'].startswith(zone['worldKey'] + '.'):
            error('zone %s must begin with its world key %r plus a dot'
                  % (zone['key'], zone['worldKey']))
        if zone['worldKey'] not in worlds:
            warn('zone %s names world %s, which this bundle does not carry'
                 % (zone['key'], zone['worldKey']))
        if zone.get('minLevel', 1) > zone.get('maxLevel', 1):
            error('zone %s has minLevel above maxLevel' % zone['key'])

    for room in bundle.get('rooms') or []:
        key = room['key']

        if len(key) > MAX_KEY:
            error('room key is %d characters, over the %d limit: %s' % (len(key), MAX_KEY, key))

        segments = key.split('.')
        if len(segments) != 3:
            error('room key %s has %d segments; a RoomKey is exactly 3' % (key, len(segments)))
        else:
            for segment in segments:
                if not SEGMENT.match(segment):
                    error('room key %s has an illegal segment %r '
                          '(lowercase, digits and inner hyphens only)' % (key, segment))

        if room['zoneKey'] not in zones:
            warn('room %s names zone %s, which this bundle does not carry' % (key, room['zoneKey']))
        elif not key.startswith(room['zoneKey'] + '.'):
            error('room %s declares zone %s but does not live in it' % (key, room['zoneKey']))

    edges = set()
    for room in bundle.get('rooms') or []:
        directions = set()
        for exit_ in room.get('exits') or []:
            direction, target = exit_['direction'], exit_['to']

            if direction in directions:
                error('room %s has two %s exits' % (room['key'], direction))
            directions.add(direction)

            if direction not in OPPOSITE:
                error('room %s has an unknown direction %r' % (room['key'], direction))
            if target not in rooms:
                warn('room %s exit %s points at %s, which this bundle does not carry'
                     % (room['key'], direction, target))

            if exit_.get('requiredItemKey') and exit_['requiredItemKey'] not in items:
                warn('room %s exit %s requires item %s, which this bundle does not carry'
                     % (room['key'], direction, exit_['requiredItemKey']))

            edges.add((room['key'], direction, target))

    # Reciprocity. An import writes each edge as given and never invents the return.
    for source, direction, target in edges:
        if target in rooms and direction in OPPOSITE:
            if (target, OPPOSITE[direction], source) not in edges:
                warn('one-way exit: %s --%s--> %s has no %s coming back'
                     % (source, direction, target, OPPOSITE[direction]))

    # Connectivity, as an undirected graph: anything in its own island is `goto`-only.
    if rooms:
        neighbours = {key: set() for key in rooms}
        for source, _, target in edges:
            if source in rooms and target in rooms:
                neighbours[source].add(target)
                neighbours[target].add(source)

        start = sorted(rooms)[0]
        seen, stack = {start}, [start]
        while stack:
            for neighbour in neighbours[stack.pop()]:
                if neighbour not in seen:
                    seen.add(neighbour)
                    stack.append(neighbour)

        for orphan in sorted(rooms - seen):
            error('room %s has no path to the rest of the bundle' % orphan)

    spawner_ids = set()
    for spawner in bundle.get('spawners') or []:
        identifier = spawner['id']
        if identifier in spawner_ids:
            error('two spawners share the id %s; re-importing would double the population'
                  % identifier)
        spawner_ids.add(identifier)

        if spawner['zoneKey'] not in zones:
            warn('spawner %s names zone %s, which this bundle does not carry'
                 % (identifier, spawner['zoneKey']))

        known = mobs if spawner['templateKind'] == 'Mob' else items
        if spawner['templateKey'] not in known:
            warn('spawner %s places %s, which this bundle does not carry'
                 % (identifier, spawner['templateKey']))

        if spawner['templateKind'] == 'Item' and spawner.get('fightsAtLevel') is not None:
            error('spawner %s is an item spawner with fightsAtLevel set' % identifier)

        for room_key in spawner.get('roomKeys') or []:
            if room_key not in rooms:
                warn('spawner %s places into room %s, which this bundle does not carry'
                     % (identifier, room_key))

    # A key the engine does not read is content that will never be seen, and it is silent in both
    # directions: the bag accepts anything and the fallback is plausible prose. This is the check
    # the quest dialogue mismatch argued for - see dialogue_keys().
    known_behavior = behavior_keys()

    for mob in bundle.get('mobTemplates') or []:
        behavior = mob.get('behavior') or {}
        for stocked in behavior.get('sells') or []:
            if stocked not in items:
                warn('%s sells %s, which this bundle does not carry' % (mob['key'], stocked))
        if behavior.get('shopkeeper') and not behavior.get('sells'):
            warn('%s is flagged shopkeeper but stocks nothing' % mob['key'])

        if known_behavior:
            for key in sorted(set(behavior) - known_behavior):
                error('%s has behavior key %r, which no engine source reads'
                      % (mob['key'], key))

    known_dialogue = dialogue_keys()

    for quest in bundle.get('quests') or []:
        for field, known, label in (
            ('giverMobKey', mobs, 'giver'),
            ('turninMobKey', mobs, 'turn-in'),
            ('requiredItemKey', items, 'required item'),
            ('rewardItemKey', items, 'reward item'),
        ):
            value = quest.get(field)
            if value and value not in known:
                warn('quest %s names %s %s, which this bundle does not carry'
                     % (quest['key'], label, value))

        # An error rather than a warning, because there is no reading of an unread dialogue key
        # under which the content works. The line is authored, stored, exported, re-imported, and
        # never spoken.
        if known_dialogue:
            for key in sorted(set(quest.get('dialogue') or {}) - known_dialogue):
                error('quest %s has dialogue key %r, which no engine source reads; '
                      'the engine reads %s'
                      % (quest['key'], key, ', '.join(sorted(known_dialogue))))

    # Room terrain. All four of these are silent at runtime: an unlisted character draws a tile the
    # map legend cannot explain, a ragged grid renders as a ragged room, and a room with nowhere to
    # stand renders with its occupants missing entirely.
    solid = non_placeable_tiles()
    for room in bundle.get('rooms') or []:
        grid = room.get('grid') or []
        legend = room.get('legend') or {}

        if not grid:
            if legend:
                warn('room %s has a legend and no grid' % room['key'])
            continue

        width = len(grid[0])
        if any(len(row) != width for row in grid):
            error('room %s has rows of differing length' % room['key'])
            continue

        used = {ch for row in grid for ch in row}

        for ch in sorted(used - set(legend)):
            error('room %s draws %r with nothing in its legend' % (room['key'], ch))

        for ch in sorted(set(legend) - used):
            warn('room %s legends %r and never draws it' % (room['key'], ch))

        if solid:
            open_cells = sum(
                1 for row in grid for ch in row
                if legend.get(ch) and legend[ch] not in solid)

            if open_cells < MIN_OPEN_CELLS:
                error('room %s leaves %d cells to stand on, under the %d minimum'
                      % (room['key'], open_cells, MIN_OPEN_CELLS))

    known_flags = registered_flags()
    if known_flags:
        for collection, label in (
            (bundle.get('worlds') or [], 'world'),
            (bundle.get('zones') or [], 'zone'),
            (bundle.get('rooms') or [], 'room'),
        ):
            for entity in collection:
                for flag in entity.get('flags') or {}:
                    if flag not in known_flags:
                        error('%s %s sets %r, which is not in the RoomFlags registry'
                              % (label, entity['key'], flag))

    print('%s: %d rooms, %d exits, %d mobs, %d items, %d spawners, %d quests'
          % (Path(path).name, len(rooms), len(edges), len(mobs), len(items),
             len(bundle.get('spawners') or []), len(bundle.get('quests') or [])))

    for message in warnings:
        print('  warn   ' + message)
    for message in errors:
        print('  ERROR  ' + message)

    return len(errors)


if __name__ == '__main__':
    if len(sys.argv) < 2:
        sys.exit(__doc__)

    failures = sum(check(argument) for argument in sys.argv[1:])
    print('FAILED' if failures else 'OK')
    sys.exit(1 if failures else 0)
