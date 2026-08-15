# Authored content

`WorldBundle` JSON for the Reaches, one file per zone, laid out `content/<world>/<zone>.json`.
The design these transcribe is [docs/WORLD.md](../docs/WORLD.md).

One file per zone rather than one per world, because that is the granularity the export endpoint
already offers (`GET /api/builder/export?zone=ossara.gatetown`) and the granularity a review can
actually be read at. A zone-scoped bundle carries the world row above it, so any one file is enough
to stand its zone up in an empty database.

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

Postgres is. The intended loop is to author geography in-game with `dig` — which makes the exit
graph correct by construction, and mints spawner ids for you — do prose and tuning in the builder
panel where `/validate` and `/preview` run, then **export over the file and commit that**. What is
here now was hand-authored because the world had to start somewhere; treat a hand edit to these
files as the exception, and re-export after any session in the builder.

The reason is on record: the `warden.last-stand` retune lived only in the database for a day while
`AbilityCatalogue` stayed stale, and the planned `DROP DATABASE` would have silently reverted it.
A file that is only written by hand drifts the same way, in the other direction.
