# The builder assist's models

`docker-compose.truenas.yml` runs Ollama on the NAS for the builder AI assist (PLAN.md §13). This
directory holds what turns the stock model into the one the assist actually talks to, and the
reasoning behind the one number that matters.

## Why this exists at all

Ollama's default context is **4096 tokens**. The design puts a large, static canon prefix in front
of every request so its KV cache is built once and reused — which is what `OLLAMA_KEEP_ALIVE: -1`
and `OLLAMA_NUM_PARALLEL: 1` in the compose file are there to protect.

That prefix does not fit in 4096, and this is the shape of the problem rather than a near miss:

| source | chars | tokens |
|---|---:|---:|
| `docs/WORLD.md` §1–9 — the canon proper | 33,970 | **10,183** |
| `docs/WORLD.md` §10 — authoring process, not canon | 10,188 | ~3,050 |
| a whole zone bundle (`content/ossara/gatetown.json`) | 107,346 | ~35,800 |
| the room schema this repo generates | 2,840 | 946 |

The first row is measured — `prompt_eval_count` from a real call, not an estimate. Gemma 3
tokenises this prose at **3.34 chars/token**, denser than the 3.8 first assumed, so the canon is
14% larger in tokens than first written down here. Rows without a bold figure are still estimates
at the measured ratio.

An over-long prompt is **truncated, not refused**. So the failure mode is not an error anybody
sees — it is a model that has apparently read the world and misremembers two thirds of it, which
is indistinguishable from the model simply being bad at the job. That is the whole reason this was
the blocker: every claim the design makes about consistency rests on the prefix surviving.

## Measured, on Ollama 0.32.15

Run locally on a 20-thread desktop CPU, no GPU. **The NAS has 4 cores of a 9600K, so treat every
rate here as an optimistic ceiling** — the memory figures transfer, the speeds do not.

| | gemma3:4b | gemma3:12b |
|---|---:|---:|
| loaded size at 16k ctx (`ollama ps`) | 3.1 GB | **8.4 GB** |
| prefill | 134 tok/s | 55 tok/s |
| generation | — | **1.3–1.8 tok/s** |
| canon prefix, cold | — | 10,176 tok in **187 s** |
| canon prefix, cached | — | 10,197 tok in **4.4 s** |

Three things follow, and all three were open questions before.

**Sliding-window attention is active.** 8.4 GB loaded against ~8.1 GB of weights leaves a few
hundred MB of KV cache, not the 6 GB a full-window 16k would need. The memory question below is
settled in favour of the cheap row: 16k is comfortable inside `mem_limit: 12g`, and 32k (~2.4 GiB
of KV) would now fit too, if the canon ever outgrows the budget.

**Prefix caching works, and it is worth every setting spent on it.** The same prefix costs 187 s
cold and 4.4 s warm — **42×**. `OLLAMA_KEEP_ALIVE: -1` is not a tuning preference; without it the
first builder to click Suggest after an idle period waits three minutes here and considerably
longer on the NAS.

**Generation is the bottleneck, and it is slower than estimated.** 1.3–1.8 tok/s, against an
earlier guess of ~4.8. A 900-character description is ~230 tokens, so **one room takes about three
minutes on this machine and should be assumed worse on the NAS**. That is not a button with a
spinner behind it; it is a queued job with the result arriving later.

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

## The memory question, now settled

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
the container OOM-killed; the bottom-left is 8.8 GiB and is comfortable. **Measured on 0.32.15, it
is the bottom row**: a 12B at 16k reports 8.4 GB loaded. The check, if it ever needs repeating on
different hardware or a newer Ollama:

```sh
docker exec dikuweb-ollama ollama ps
```

The `SIZE` column includes the KV cache, and `CONTEXT` shows the window actually loaded — which is
a better check than `ollama show --parameters`, because it is what the runner did rather than what
the model declared. Weights plus ~1.3 GiB means SWA is working and there is room to spare; weights
plus ~6 GiB means it is not, and 16k is running without margin — in which case, before dropping
`num_ctx`, halve the cache instead, in the compose file's `ollama` service:

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
