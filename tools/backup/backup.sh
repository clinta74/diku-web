#!/bin/sh
# Take one pg_dump, prove it restores, then prune the old ones.
#
# PLAN.md §6 makes Postgres the only source of truth for content, and §10 names losing the world as
# the price of that choice. Phase 6 asks for "scheduled pg_dump backups + a rehearsed restore
# drill", and the second half is the half that matters: an untested backup is a belief about a
# file. This script does not separate the two - it dumps and then immediately restores what it
# dumped into a scratch database and counts the rows. A dump that cannot be restored is deleted
# rather than kept, because a file that looks like a backup and is not one is worse than no file
# at all: it is the thing you find out about at the worst possible moment.
#
# Runs as a sidecar in docker-compose.prod.yml against the postgres service. It is a plain
# postgres:18 container rather than a backup image from the registry - it needs pg_dump,
# pg_restore, psql and a clock, and it already has all four. Nothing joins the dependency graph
# for this (the same argument EngineMetrics makes about exporters).
#
# Run one by hand at any time:
#   docker compose -f docker-compose.prod.yml exec backup /scripts/backup.sh --once
#
# Every knob is an environment variable, listed with its default below.

set -eu

PGHOST="${PGHOST:-postgres}"
PGUSER="${PGUSER:-muwbta}"
PGDATABASE="${PGDATABASE:-muwbta}"
BACKUP_DIR="${BACKUP_DIR:-/backups}"
# Dumps to keep. The whole world is well under a megabyte compressed, so this is generous on
# purpose: the cost of a month of history is nothing and the cost of not having yesterday's is the
# whole world.
BACKUP_KEEP="${BACKUP_KEEP:-30}"
# Hour (UTC) to run at. The loop sleeps until the next occurrence rather than sleeping 24h in a
# circle, so a container restart does not permanently shift when backups happen.
BACKUP_AT_HOUR="${BACKUP_AT_HOUR:-3}"
# The scratch database the verify restores into. Dropped afterwards, always.
VERIFY_DB="${VERIFY_DB:-muwbta_verify}"

# Only files this script wrote are ever pruned. A plain 'muwbta-*.dump' also matches dumps taken
# by hand - backups/ here already held a 'dikuweb-full-…' one - and retention that silently
# deletes somebody's manual pre-migration snapshot is the sort of helpfulness nobody asked for.
# The stamp shape is the signature: yyyy-mm-ddThhmmssZ.
DUMP_GLOB='muwbta-????-??-??T??????Z.dump'

log() { echo "[backup $(date -u '+%Y-%m-%dT%H:%M:%SZ')] $*"; }

# Exact row counts for every table in `public`, as sorted `name=count` lines.
#
# Discovered from the catalogue rather than from a hand-written table list, deliberately.
# The retired SQL export carried a hand-copied column list and drifted from the schema five times,
# each drift silent; a verify step that only checked the nine tables somebody remembered would pass
# while silently ignoring a tenth.
# query_to_xml is the standard way to get a real count(*) per table in one statement - n_live_tup
# is an estimate and would make this a vibe check rather than a comparison.
row_counts() {
    psql -U "$PGUSER" -d "$1" -At -c "
        select c.relname || '=' || (
            xpath(
                '/row/c/text()',
                query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname),
                             false, true, ''))
        )[1]::text
        from pg_class c
        join pg_namespace n on n.oid = c.relnamespace
        where c.relkind = 'r' and n.nspname = 'public'
        order by c.relname;"
}

# Refuse to write backups into the container's own filesystem.
#
# Found the hard way while testing this script: the dev postgres container predates the
# `./backups:/backups` line in the compose file, so /backups existed, was writable, and every dump
# landed in the container's writable layer - where it looks completely normal from inside and
# disappears the moment the container is recreated. `docker inspect` showed one mount, for pgdata.
#
# A backup written somewhere that does not survive the container is not a backup, and this is the
# quietest possible way to have none: the log says "wrote /backups/…", the verify passes, and the
# file is real right up until you need it. So this is a hard failure rather than a warning.
#
# Compared by device number rather than with `mountpoint`, which is not in this image. A bind
# mount and a named volume both land on a different device from /; the container layer does not.
require_real_volume() {
    mkdir -p "$BACKUP_DIR"
    if [ "$(stat -c %d "$BACKUP_DIR")" = "$(stat -c %d /)" ]; then
        log "REFUSING TO RUN: $BACKUP_DIR is inside the container, not on a volume."
        log "  Anything written there is lost when the container is recreated."
        log "  Add a bind mount or named volume for $BACKUP_DIR and recreate this service."
        return 1
    fi
}

run_once() {
    require_real_volume || return 1

    stamp=$(date -u '+%Y-%m-%dT%H%M%SZ')
    final="$BACKUP_DIR/muwbta-$stamp.dump"
    # Written to .part and renamed only once pg_dump has exited cleanly. A container killed
    # mid-dump otherwise leaves a truncated file with a plausible name and a plausible size, which
    # is exactly the kind of backup that is discovered to be useless during an incident.
    partial="$final.part"

    mkdir -p "$BACKUP_DIR"

    log "dumping $PGDATABASE from $PGHOST"
    # -Fc: the custom format, so pg_restore can be selective during a real recovery (one table, or
    # schema before data). A plain .sql dump can only be replayed whole.
    if ! pg_dump -U "$PGUSER" -d "$PGDATABASE" -Fc -f "$partial"; then
        log "pg_dump failed"
        rm -f "$partial"
        return 1
    fi
    mv "$partial" "$final"
    log "wrote $final ($(wc -c < "$final") bytes)"

    verify "$final" || {
        # Deleted, not kept and flagged. See the header: a bad backup left on disk is a trap.
        log "removing $final because it did not restore"
        rm -f "$final"
        return 1
    }

    prune
    printf '%s\n' "$stamp" > "$BACKUP_DIR/last-verified"
    log "ok"
}

verify() {
    dump="$1"
    log "verifying by restoring into $VERIFY_DB"

    # -f- with --if-exists so a leftover scratch database from a killed run does not fail the next
    # one. This is the only database this script ever drops, and its name is not the live one.
    dropdb -U "$PGUSER" --if-exists "$VERIFY_DB"
    createdb -U "$PGUSER" "$VERIFY_DB"

    # --no-owner --no-privileges: the drill has to work when the restoring role is not the role
    # that made the dump, which is the situation on a rebuilt host. A restore that only works as
    # the original owner is a restore that will fail on the day it is needed.
    if ! pg_restore -U "$PGUSER" -d "$VERIFY_DB" --no-owner --no-privileges "$dump" 2>/tmp/restore.err; then
        log "pg_restore reported errors:"
        sed 's/^/    /' /tmp/restore.err
        dropdb -U "$PGUSER" --if-exists "$VERIFY_DB"
        return 1
    fi

    # Written to files rather than compared as shell variables so the difference can be diffed.
    # /bin/sh here is dash, which has no process substitution - `diff <(echo …)` would be a syntax
    # error that only fires on the day a backup actually goes wrong.
    row_counts "$PGDATABASE" > /tmp/counts.source
    row_counts "$VERIFY_DB" > /tmp/counts.restored
    dropdb -U "$PGUSER" --if-exists "$VERIFY_DB"

    if ! diff -u /tmp/counts.source /tmp/counts.restored > /tmp/counts.diff; then
        # Not necessarily a broken dump - a write that landed between the dump and this comparison
        # produces the same symptom, and on a live server that is the common case. Reported with
        # both sides so the difference can be read rather than guessed at, and treated as a
        # failure because the alternative is a verify step that passes on anything.
        log "row counts differ between source and restore:"
        sed 's/^/    /' /tmp/counts.diff
        return 1
    fi

    tables=$(wc -l < /tmp/counts.source)
    rows=$(awk -F= '{ n += $2 } END { print n }' /tmp/counts.source)
    log "restored $tables tables, $rows rows, counts identical"
}

prune() {
    # Keeps the newest BACKUP_KEEP *verified* dumps: a failed run has already deleted its own file,
    # so anything still here restored at least once.
    total=$(find "$BACKUP_DIR" -maxdepth 1 -name "$DUMP_GLOB" | wc -l)
    if [ "$total" -le "$BACKUP_KEEP" ]; then
        log "$total dumps kept, under the limit of $BACKUP_KEEP"
        return 0
    fi

    # Sorted by name, which is chronological because the stamp is ISO 8601. Not by mtime: a file
    # copied onto this volume during a recovery would sort as new and evict a real backup.
    find "$BACKUP_DIR" -maxdepth 1 -name "$DUMP_GLOB" | sort | head -n "$((total - BACKUP_KEEP))" |
        while read -r old; do
            log "pruning $old"
            rm -f "$old"
        done
}

seconds_until_next_run() {
    now_h=$(date -u '+%-H')
    now_m=$(date -u '+%-M')
    now_s=$(date -u '+%-S')
    target=$((BACKUP_AT_HOUR * 3600))
    current=$((now_h * 3600 + now_m * 60 + now_s))
    delta=$((target - current))
    [ "$delta" -le 0 ] && delta=$((delta + 86400))
    echo "$delta"
}

if [ "${1:-}" = "--once" ]; then
    run_once
    exit $?
fi

log "sidecar started; backing up $PGDATABASE daily at ${BACKUP_AT_HOUR}:00 UTC, keeping $BACKUP_KEEP"

while true; do
    wait_for=$(seconds_until_next_run)
    log "next run in $((wait_for / 3600))h $(((wait_for % 3600) / 60))m"
    sleep "$wait_for"
    # Never exits on a failed backup. A crash-looping sidecar would restart, sleep until tomorrow,
    # and quietly stop backing up; the failure is loud in the log and the next run still happens.
    run_once || log "this run failed; the next one is still scheduled"
done
