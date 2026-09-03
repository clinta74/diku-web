#!/usr/bin/env python3
"""Checks that no builder form offers a key the engine has stopped reading.

    python tools/check-builder-keys.py

**A one-off sweep, kept for re-running by hand. Not part of the toolchain.** Everything else under
tools/ that a contributor needs is being moved to C# or TypeScript, so building and checking this
repo needs only .NET and Node - the two it already needs. This one stays in Python deliberately: it
is not a tool others need, it is a sweep that found three bugs (below), and porting it would buy
nothing. Do not wire it into CI, and do not assume it has been kept in step with the engine since
the last time somebody ran it.

The builder's field lists are a **transcription** of engine contracts written in another language,
and a wrong entry fails in the quietest way this codebase has: the value is typed by a builder,
stored, exported, re-imported, and never consulted. Nothing throws, nothing logs, and the number
sits in the database looking authoritative.

That has happened three times:

  * The armour rework retired `armorFlat`, `armorPercent` and `armorMultiplier` for a single
    `armor` rating. Both the item and mob editors went on offering the dead three and none of the
    live one, so an imported cap showed `armor = 3` under "carried through unchanged" while three
    inert boxes sat above it labelled as armour.
  * `DefenseEffect` reads `mitigation`; the ability editor offered `armorFlat` for both
    `buff.defense` and `debuff.expose`, so the absorb half of every guard authored in the browser
    was silently zero.
  * `healthMultiplier`, `focusMultiplier` and `staminaMultiplier` were offered on items and have
    never been read by anything, in any version.

No test can catch this from either side alone - the C# cannot see the TypeScript and the
TypeScript cannot see the C#. So this reads both, the way `check-bundle.py` already reads
`RoomFlags.cs` rather than keeping a copy of it.

**One direction only, deliberately.** It asks "does the engine mention this key at all", not "does
the form offer everything the engine reads". The second question needs an exact inventory of every
lookup shape in the engine - `TryReadInt`, `GetIntFromStats`, a `string[]` of scaled fields, an
effect's `Read` - and a miss there produces a *false* report of a dead key, which is the one
failure that teaches people to ignore a checker. A key the engine reads and no form offers is a
missing feature; a key the form offers and nothing reads is a lie. Only the second is caught here.

Comments are stripped before harvesting, because a retired key usually survives in the paragraph
explaining why it was retired - and `armorFlat` is named twice in `EquipmentResolver`'s own remarks.

Exit status is 1 if anything is reported.
"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# Where the engine names the keys it reads off a stat bag or an effect's parameters.
ENGINE_SOURCES = [
    ('src', 'Muwbta.Domain', 'Combat'),
    ('src', 'Muwbta.Domain', 'Inhabitants'),
    ('src', 'Muwbta.Domain', 'Abilities', 'Effects'),
    ('src', 'Muwbta.Engine', 'Spawning'),
    ('src', 'Muwbta.Engine', 'Systems'),
]

# The forms whose keys are checked, and the shape their entries take.
FORMS = [
    ('item editor', ('client', 'src', 'builder', 'items', 'stats.ts')),
    ('mob editor', ('client', 'src', 'builder', 'mobs', 'mobStats.ts')),
    ('ability effects', ('client', 'src', 'builder', 'effects.ts')),
]

FORM_KEY = re.compile(r"key: '([a-zA-Z]+)'")

# Identifiers that are plainly not stat keys, so a form naming one is not evidence of anything.
IGNORED = {'name'}

COMMENTS = re.compile(r'//[^\n]*|/\*.*?\*/', re.S)
LITERAL = re.compile(r'"([a-z][a-zA-Z]*)"')


def engine_keys():
    """Every lowerCamel string literal the engine's stat and effect code contains.

    Deliberately generous: this is the set a form key must appear in, so a key harvested by
    accident only ever makes the check quieter, never wrong.
    """
    keys = set()
    seen_any = False

    for parts in ENGINE_SOURCES:
        directory = REPO.joinpath(*parts)
        if not directory.is_dir():
            continue

        for path in sorted(directory.rglob('*.cs')):
            seen_any = True
            keys |= set(LITERAL.findall(COMMENTS.sub(' ', path.read_text(encoding='utf-8'))))

    return keys if seen_any else None


def main():
    engine = engine_keys()
    if engine is None:
        print('could not read any engine source; nothing checked')
        return 1

    problems = []
    for label, parts in FORMS:
        path = REPO.joinpath(*parts)
        try:
            text = path.read_text(encoding='utf-8')
        except OSError:
            problems.append('%s: %s could not be read' % (label, path.name))
            continue

        offered = set(FORM_KEY.findall(text)) - IGNORED
        if not offered:
            problems.append('%s: no keys found; has the file changed shape?' % label)
            continue

        print('%-16s %s' % (label, ', '.join(sorted(offered))))

        for key in sorted(offered - engine):
            problems.append(
                '%s offers %r, and no engine source names it - the value would be stored '
                'and never read' % (label, key))

    print()
    for problem in problems:
        print('  ERROR  ' + problem)

    print('FAILED' if problems else 'OK')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
