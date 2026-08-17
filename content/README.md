# Authored content

`WorldBundle` JSON for the Reaches. The design these transcribe is
[docs/WORLD.md](../docs/WORLD.md).

**The whole world is here**: five realms, eighteen zones, 224 rooms, 68 mob templates, 92 items,
100 spawners and all five acts.

**Merge them and import once** — see [Applying them](#applying-them). Importing the six files one at
a time works, but it has to be done in realm order (`ossara`, `grask`, `azhen`, `nemhal`,
`the-unlit`) and still produces dangling-exit warnings, because a realm's gate names a room in the
next one and each file's exits are applied before the next file's rooms exist.

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

## Applying them

Merge the six into one bundle, check it, then import it once:

```
python tools/merge-bundles.py content -o build/the-reaches.json
python tools/check-bundle.py build/the-reaches.json
POST /api/builder/import?dryRun=true    # what would happen; changes nothing
POST /api/builder/import                # do it
```

The import endpoint requires the Builder role and cookie authentication, so scripting it means
logging in first. **Always dry-run first**: an import is **not atomic** — one entity is one loop
round trip and one transaction, so a failure part way through leaves everything before it applied.
Merging does not change that; what it changes is that every intermediate state is *valid*, and that
one dry run covers the whole world instead of six.

**`build/` is gitignored on purpose.** The merged file is derived, byte-for-byte reproducible from
these six, and six times the same words — committing it would make it a second source of truth that
drifts, invisibly, inside a diff nobody reads. Rebuild it when you import.

`check-bundle.py` catches the three mistakes the dry run does not treat as failures — a one-way exit,
a room with no path to the rest of its zone, and a room declaring a zone it does not live in. It runs
without a server, so it belongs in an editor loop.

**Run it on the merged file, not the parts.** It can only judge an exit whose target is in the same
bundle, so per file the four cross-realm gates are the one part of the world it cannot check at all.
Run against the merge, it found one on the first attempt: the Grask→Azhen gate leaves `west` and
comes back `north`, where every other gate is a proper south/north pair.

## Three properties of the import path that shape how these are written

- **`formatVersion` must match the server exactly.** It is the one hard refusal in the whole path.
  Author against `WorldBundle.CurrentFormatVersion` — these files are at **11** — not against this
  sentence, which has now been wrong twice. `check-bundle.py` reads the number out of the C# and is
  the thing to trust; `merge-bundles.py` refuses a set of files that disagree with each other.
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
