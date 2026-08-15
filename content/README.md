# Authored content

`WorldBundle` JSON for the Reaches. The design these transcribe is
[docs/WORLD.md](../docs/WORLD.md).

**The whole world is here**: five realms, eighteen zones, 224 rooms, 67 mob templates, 70 items,
90 spawners and all five acts. Import them in realm order — `ossara`, `grask`, `azhen`, `nemhal`,
`the-unlit` — because a realm's gate names a room in the next one, and a dangling exit is a warning
you would rather not have to read past.

| File | Zones |
|---|---|
| `ossara/gatetown.json` | Gatetown, hand-authored first and kept separate |
| `ossara/the-reaches.json` | the Terraces, Brackenfell, the Rimwalk |
| `grask/the-reaches.json` | the Landing, the Cutting, Stiltmarsh, the Owing |
| `azhen/the-reaches.json` | the Camp, Ummath, Serrivet, Thessivar |
| `nemhal/the-reaches.json` | the Hold, Vurrach, Olmenneth, Keshvaun |
| `the-unlit/the-reaches.json` | the Crossing, the Regard |

A bundle carries the world row above its zones, so any one file is enough to stand its realm up in
an empty database.

**Progression is four conditional exits and nothing else.** Each realm's story chain ends in a
quest whose reward is a character flag, and the gate onward requires it (`PLAN.md` §4.15):
`attuned.grask`, `attuned.azhen`, `attuned.nemhal`, `attuned.the-unlit`. The gates are one-way
gated — you can always walk back the way you came, including out of the Unlit, where `recall` does
not work and walking is the only way home.

## Applying one

```
POST /api/builder/import?dryRun=true    # what would happen; changes nothing
POST /api/builder/import                # do it
```

Both require the Builder role and cookie authentication, so scripting it means logging in first.
Always dry-run a zone-sized bundle: an import is **not atomic** — one entity is one loop round trip
and one transaction, so a failure part way through leaves everything before it applied.

Before either, and without needing a server:

```
python tools/check-bundle.py content/ossara/gatetown.json
```

That catches the three mistakes the dry run does not treat as failures — a one-way exit, a room with
no path to the rest of its zone, and a room declaring a zone it does not live in. See the script's
own docstring for why each is worth a separate pass.

## Three properties of the import path that shape how these are written

- **`formatVersion` must be exactly 6.** It is the one hard refusal in the whole path. Author against
  `WorldBundle.CurrentFormatVersion`, not against this sentence.
- **Import is a merge, not a mirror.** Deleting an entity from a file does not delete it from the
  world, and renaming a room key produces *both* rooms on the next import. Removals are explicit
  `DELETE` calls.
- **Spawner GUIDs are content.** Re-importing is idempotent only because each spawner carries its
  own `id`. Minting a fresh one doubles that zone's population; never regenerate them.

## These files are an artifact, not the source of truth

Postgres is. From here the loop is to edit in the builder — where `/validate`, `/preview` and the
storyline graph actually run — and then **export over the file and commit that**. Re-export after
any session in the builder rather than hand-editing the JSON back into agreement.

Spawner ids in these files are `uuid5` over `zone|kind|template|rooms` rather than random, so the
generator that produced them is reproducible and a re-import cannot double a zone's population. An
export will replace them with whatever the database holds, which is fine — what matters is that
they are stable, not what they are.

The reason is on record: the `warden.last-stand` retune lived only in the database for a day while
`AbilityCatalogue` stayed stale, and the planned `DROP DATABASE` would have silently reverted it.
A file that is only written by hand drifts the same way, in the other direction.
