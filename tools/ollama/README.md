# The builder assist's models

`docker-compose.truenas.yml` runs Ollama on the NAS for the builder AI assist (PLAN.md §13). This
directory holds what turns the stock model into the one the assist actually talks to, and the
reasoning behind the one number that matters.

## Why this exists at all

Ollama's default context is **4096 tokens**. The design puts a large, static canon prefix in front
of every request so its KV cache is built once and reused — which is what `OLLAMA_KEEP_ALIVE: -1`
and `OLLAMA_NUM_PARALLEL: 1` in the compose file are there to protect.

That prefix does not fit in 4096, and this is the shape of the problem rather than a near miss:

| source | chars | ~tokens |
|---|---:|---:|
| `docs/WORLD.md` §1–9 — the canon proper | 33,970 | **~8,940** |
| `docs/WORLD.md` §10 — authoring process, not canon | 10,188 | ~2,681 |
| whole file | 44,158 | ~11,621 |
| a whole zone bundle (`content/ossara/gatetown.json`) | 107,346 | ~35,782 |

(Estimated at 3.8 chars/token for prose and 3.0 for JSON. Close enough to size a window; not close
enough to size a budget to the token.)

An over-long prompt is **truncated, not refused**. So the failure mode is not an error anybody
sees — it is a model that has apparently read the world and misremembers two thirds of it, which
is indistinguishable from the model simply being bad at the job. That is the whole reason this is
the blocker: every claim the design makes about consistency rests on the prefix surviving.

## Applying it

On the NAS, from the directory holding the compose file:

```sh
sh tools/ollama/create-models.sh
```

It pulls the base, builds `dikuweb-builder` from `Modelfile.builder`, and then **asserts** that
`num_ctx` came back as 16384 rather than trusting that it did. Rerun it whenever the Modelfile
changes; that is also how a changed parameter is applied.

Point the assist at `dikuweb-builder`, not at `gemma3:12b`. Requesting the base by name gets you
4096 again.

## The memory question, which is not settled

16384 was chosen against a token budget (written out in `Modelfile.builder`). The reason it is not
32768 is RAM, and the arithmetic is worth having on hand because it decides whether the container
survives.

Gemma 3 12B: 48 layers, 8 KV heads, head dim 256. So per token, per layer, at f16:

```
2 (K and V) x 8 heads x 256 dim x 2 bytes  =  8 KiB
```

Where that lands depends entirely on whether the runtime gives you Gemma 3's sliding-window
attention, in which only every 6th layer attends to the full window and the other 40 are capped at
1024 tokens:

| | 16k ctx | 32k ctx |
|---|---:|---:|
| all 48 layers full (no SWA) | **6.0 GiB** | 12.0 GiB |
| 8 global full + 40 local capped (SWA) | **1.3 GiB** | 2.4 GiB |

Against `mem_limit: 12g` with ~7.5 GiB of weights resident, the top-left cell is 13.5 GiB and gets
the container OOM-killed; the bottom-left is 8.8 GiB and is comfortable. **I could not verify from
here which one this Ollama build does**, and the difference is the whole margin, so treat it as
something to check rather than something decided:

```sh
docker exec dikuweb-ollama ollama ps
```

The `SIZE` column on a loaded model includes its KV cache. Weights plus ~1.3 GiB means SWA is
working and there is room to spare. Weights plus ~6 GiB means it is not, and 16k is running
without margin — in which case, before dropping `num_ctx`, try halving the cache instead, in the
compose file's `ollama` service:

```yaml
      OLLAMA_FLASH_ATTENTION: '1'
      OLLAMA_KV_CACHE_TYPE: q8_0
```

Quantized KV needs flash attention, which is why both go together. This is a cheaper trade than a
smaller window: q8_0 costs very little quality on a cache and buys back half the memory, whereas
cutting `num_ctx` costs the thing the prefix exists for.

## What is deliberately not in the Modelfile

**The canon prefix.** It could go in `SYSTEM`, and that would make it byte-identical across
requests by construction, which is exactly what prefix caching wants. It stays server-side anyway,
for two reasons: PLAN.md §13 wants the prompt to be "one thing in one place to tune", and baking it
means every canon edit is a model rebuild — with the failure being a *stale* world that still
answers, which is the same silent-wrongness this file exists to remove. The requirement it leaves
behind is real and belongs with whoever assembles it: **the prefix must be byte-stable across
requests**, or the cache misses and the minutes are spent again.

**Sampling parameters** beyond a sober default. `temperature`, `top_p` and friends are applied per
request and cost nothing to vary, so prose and schema-constrained calls can disagree about them.
Only load-time parameters belong here, because those are the ones a differing request silently
*reloads* on — and a reload throws away the prefix cache.

## For the schema work

One number above is a constraint on it: a whole zone bundle is ~36k tokens, so few-shot examples
cannot be whole bundles at any window this machine can afford. Exemplars have to be extracted
rooms, not files.
