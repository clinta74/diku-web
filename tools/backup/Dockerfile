# The nightly dump sidecar, with its script baked in.
#
# **This image exists because a bind mount could not carry the script.** A TrueNAS custom app
# renders its compose file standalone, with no repository beside it, so `./tools/backup:/scripts`
# resolved to a path that did not exist - and Docker creates a missing bind source as an empty
# directory rather than failing. The entrypoint is `/bin/sh /scripts/backup.sh`, the script was not
# there, and the sidecar never started. Backups had never run once, silently, for as long as the
# deployment had existed.
#
# Baking it in removes the class of problem: the script arrives with the image or the image does
# not build.
#
# **postgres:18 and not a smaller base.** The job needs `pg_dump`, and a dump taken by an older
# pg_dump than the server it dumps is a restore that may not work. This tracks the server's major
# version deliberately - when postgres moves, this moves with it.
FROM postgres:18

# /scripts rather than somewhere tidier, because RUNBOOK.md documents
# `exec backup /scripts/backup.sh --once` and that command should keep working.
COPY --chmod=0755 backup.sh /scripts/backup.sh

ENTRYPOINT ["/bin/sh", "/scripts/backup.sh"]
