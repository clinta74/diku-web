# Recovery Runbook

What to do when something has already gone wrong. [DOCKER.md](DOCKER.md) and
[DEPLOY_NO_ENV.md](../DEPLOY_NO_ENV.md) cover setup — the half that gets written because it is the
half somebody needs while things are going well.

Every procedure here has been run. Where a step is untested, it says so.

---

## 1. Backups

### What runs

A `backup` sidecar in `docker-compose.prod.yml`, on the same network as `postgres`. It is a plain
`postgres:18` container running [tools/backup/backup.sh](../tools/backup/backup.sh) — same image as
the server, so `pg_dump` is never older than the database it is dumping.

Nightly at **03:00 UTC** (`BACKUP_AT_HOUR`), keeping **30** dumps (`BACKUP_KEEP`), in `./backups`
on the host as `dikuweb-<ISO-stamp>.dump` (`pg_dump -Fc`, so `pg_restore` can be selective).

Each run dumps, **restores what it just dumped into a scratch database and compares exact row
counts for every table**, then prunes. A dump that does not restore is **deleted, not kept** — a
file that looks like a backup and is not one is worse than no file, because you find out during
the incident.

Two guards worth knowing about, both from failures hit while building this:

- **It refuses to run if `/backups` is not on a volume.** The dev container predated the mount, so
  dumps were landing in the container's writable layer: writable, plausible, and gone on the next
  `docker compose up`. From inside, that failure is invisible.
- **Pruning only touches files it wrote** (`dikuweb-<stamp>.dump`). Hand-made dumps with other
  names are left alone.

### Take one now

```bash
docker compose -f docker-compose.prod.yml exec backup /scripts/backup.sh --once
```

Do this **before** every deploy that carries a migration. It costs seconds.

### Is the backup healthy?

```bash
cat backups/last-verified                              # stamp of the last verified run
docker compose -f docker-compose.prod.yml logs backup  # "ok", or the reason it was not
ls -la backups/
```

`last-verified` only advances on a run that dumped *and* restored. A stale stamp with a running
container means backups are failing — read the log.

---

## 2. The restore drill

```powershell
tools/restore-drill.ps1                                        # newest scheduled dump
tools/restore-drill.ps1 -Dump backups/dikuweb-2026-08-14T144408Z.dump
```

Restores into a scratch database, **starts the server against it**, waits for `/health/ready`, and
reads the room count out of the loop's own startup line. Drops the database and stops the server on
the way out of every branch. Nothing it does touches the live database.

The sidecar already proves a dump restores. The drill answers a different question — *does the
application start against what came out of it?* — and that question has its own failure mode:

> **A dump can restore perfectly and still be unrecoverable.**

Which is not hypothetical here. See §3.

Run it after any migration, and whenever you want to believe the backups.

---

## 3. Restoring for real

### 3a. The normal case

```bash
docker compose -f docker-compose.prod.yml stop web        # single writer; nothing else may be up
docker cp backups/<dump> dikuweb-postgres:/tmp/restore.dump
docker exec dikuweb-postgres psql -U dikuweb -d postgres -c 'drop database dikuweb;'
docker exec dikuweb-postgres psql -U dikuweb -d postgres -c 'create database dikuweb;'
docker exec dikuweb-postgres pg_restore -U dikuweb -d dikuweb --no-owner --no-privileges /tmp/restore.dump
docker compose -f docker-compose.prod.yml start web
docker compose -f docker-compose.prod.yml logs -f web     # expect "Game loop starting with N rooms"
```

Stop `web` first and confirm it is down. §2.1 makes the loop the single writer, and a restore under
a live loop is two writers.

`--no-owner --no-privileges` because a recovery onto a rebuilt host is not performed by the role
that took the dump.

**The last line is the check.** "The container is up" and "the world came back" are different
claims; `Game loop starting with N rooms` is the second one.

### 3b. A dump older than a migration squash — **verified hazard**

`backups/dikuweb-full-2026-08-10.dump` restores with no errors and the server **will not start
against it**:

```
Applying migration '20260810145400_InitialCreate'.
Npgsql.PostgresException: 42P07: relation "abilities" already exists
```

The migration history was squashed on 2026-08-10. That dump carries the six pre-squash IDs
(`20260807140817_InitialCreate` … `20260809135355_AddAccountMutedUntil`); the repo now starts at a
single `20260810145400_InitialCreate`. EF recognises none of the recorded IDs, concludes nothing has
been applied, and tries to build the schema from scratch on top of a fully populated one.

The rows are all fine. The history is what is wrong, so that is what to repair — restore as in §3a,
then **before starting `web`**:

```sql
-- ProductVersion must match the other rows in a current database (10.0.10 as of 2026-08-14).
DELETE FROM "__EFMigrationsHistory";
INSERT INTO "__EFMigrationsHistory" VALUES ('20260810145400_InitialCreate', '10.0.10');
```

Start `web`. The migrations after the baseline apply in order and the loop comes up.

Only valid when the pre-squash schema equals the squashed baseline — true for this squash, and
**tested**: the six later migrations applied cleanly and the loop started with 15 rooms.

**If you squash again, every existing backup inherits this problem.** Note the new baseline ID here
in the same commit.

---

## 4. A startup migration fails

**Symptom.** `web` restart-loops; nginx answers 502; the log ends in a `PostgresException` under
`StartupMigrator.RunAsync`. `StartupMigrator` retries transient failures with backoff, so a
connection blip recovers on its own — a loop that persists is not transient.

**The schema is not half-migrated.** No migration in this repo suppresses its transaction, so each
is atomic under Npgsql: a failure leaves the schema exactly at the previous migration. That makes
this a rollback, not a recovery.

1. `docker compose -f docker-compose.prod.yml stop web`
2. Read the actual `PostgresException`. It names the table and the constraint.
3. Point `web` back at the previous image tag and start it. The old build's migrations are already
   applied, so it starts.
4. Fix the migration, and rehearse it against a drill database (§2) before deploying again.

**Do not restore from backup for a failed migration.** The database is intact; restoring throws away
every write since the last dump to solve a problem that a redeploy solves.

**Do not hand-edit `__EFMigrationsHistory` to skip a failing migration.** It marks work as done that
was never done, and the next migration builds on a schema that does not exist. §3b is the one
exception, and it inserts a baseline rather than skipping anything.

---

## 5. Triage

| Symptom | Likely cause | First move |
|---|---|---|
| 502 from nginx, `web` restarting | Startup migration failed, or the database is down | `logs web`; §4 |
| 502, `web` healthy | Port mismatch — the client image proxies to `http://web:8080` | Check the `web` port comment in `docker-compose.prod.yml` |
| Players connected, commands do nothing | Loop wedged or stopped | `logs web` for the slow-pulse watchdog; restart `web`. Progress is saved (§11) |
| Everyone disconnected, page loads | SSE buffering somewhere in front | `proxy_buffering off`; `X-Accel-Buffering: no` on the stream |
| Content missing after a deploy | Migration data loss, or someone deleted it | `content_audit` has before/after per mutation. Prefer that over a restore |
| Auth failing for everyone | Rate limit — behind a proxy every caller shares one address | Known weakness, §6 below |
| `last-verified` stale | Backups failing | `logs backup` |

---

## 6. Known gaps

Named rather than hidden.

- **The auth rate limit is site-wide behind nginx.** Every caller shares the proxy's address, so a
  single client can exhaust the bucket for everyone. Needs forwarded headers and a trusted-proxy
  list this repo does not have.
- **Backups are on the same host as the database.** A dump on the volume next to the cluster
  survives a bad migration and does not survive losing the host. Copying `./backups` off-box is not
  automated.
- **A ban or role change waits for revalidation** (~60s) rather than taking effect instantly.
- **No off-hours alerting.** The backup sidecar logs a failure; nothing pages anyone. §7's Grafana
  dashboard is where that would go.
- **The restore drill is Windows/PowerShell.** It runs where development happens, not on the
  production host.

---

## 7. Metrics

`EngineMetrics` publishes six instruments on the `DikuWeb.Engine` meter; `docker-compose.prod.yml`
runs Prometheus and Grafana against them. Dashboard at `:3000`, and the four §11 targets are the
four panels.

`/metrics` is deliberately **not** proxied — nginx forwards only `/api/` and `/health`, so the
endpoint is reachable from inside the compose network and not from the internet. Keep it that way;
it is unauthenticated.
