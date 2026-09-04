#!/bin/sh
# Builds the derived models the builder assist talks to, and proves the context window took.
#
# Run from the repository root, on whichever machine is running the ollama container:
#
#   sh tools/ollama/create-models.sh
#
# Nothing in here is deployment-specific - it works entirely through `docker exec` on $CONTAINER,
# so the same command builds the model on the NAS (docker-compose.truenas.yml) and on a dev box
# (docker compose --profile assist up -d).
#
# Idempotent: `ollama create` overwrites a model of the same name, so this is also how a changed
# Modelfile is applied. It is a deploy step rather than a compose service on purpose - the model
# only needs rebuilding when this directory changes, and a service that reran on every boot would
# be a several-minute no-op standing between a restart and a working assist.
#
# NOTE: creating the model evicts whatever is loaded, so the first request afterwards pays the
# full prefill for the canon prefix - minutes on this CPU. Do it when nobody is building.

set -e

CONTAINER=${CONTAINER:-muwbta-ollama}
MODEL=${MODEL:-dikuweb-builder}
EMBED=${EMBED:-nomic-embed-text}
HERE=$(dirname "$0")

say() { printf '\n== %s\n' "$1"; }

say "Checking $CONTAINER is up"
docker exec "$CONTAINER" ollama list >/dev/null

# The base named in Modelfile.builder. Read from the file rather than repeated here, so the two
# cannot drift into pulling one model and building on another.
BASE=$(awk '/^FROM /{print $2; exit}' "$HERE/Modelfile.builder")

say "Pulling base $BASE"
docker exec "$CONTAINER" ollama pull "$BASE"

say "Pulling embedding model $EMBED"
docker exec "$CONTAINER" ollama pull "$EMBED"

say "Creating $MODEL"
docker cp "$HERE/Modelfile.builder" "$CONTAINER:/tmp/Modelfile.builder"
# `cd` first, and -f relative. Verified against Ollama 0.32.15: an absolute path here fails with
# "no Modelfile or safetensors files found" even though the file is plainly there and readable -
# `create` resolves -f against the client's working directory and does not accept an absolute one.
docker exec "$CONTAINER" sh -c "cd /tmp && ollama create '$MODEL' -f Modelfile.builder"
docker exec "$CONTAINER" rm -f /tmp/Modelfile.builder

# The whole point of the file, asserted rather than assumed. `ollama show --parameters` prints
# what the model was built with; if num_ctx is absent or 4096 here, the create did not take and
# every request after this will silently truncate the canon.
say "Verifying num_ctx"
PARAMS=$(docker exec "$CONTAINER" ollama show "$MODEL" --parameters)
printf '%s\n' "$PARAMS"

WANT=$(awk '/^PARAMETER num_ctx /{print $3; exit}' "$HERE/Modelfile.builder")
GOT=$(printf '%s\n' "$PARAMS" | awk '/^num_ctx/{print $2; exit}')

if [ "$GOT" != "$WANT" ]; then
  printf '\nFAILED: num_ctx is "%s", expected "%s".\n' "$GOT" "$WANT" >&2
  exit 1
fi

say "num_ctx is $GOT. Warming the model"
# Loads it and leaves it loaded (OLLAMA_KEEP_ALIVE is -1), so the first real request is not the
# one that pays for the load.
docker exec "$CONTAINER" ollama run "$MODEL" '' >/dev/null 2>&1 || true

say "Done. $MODEL and $EMBED are ready."
