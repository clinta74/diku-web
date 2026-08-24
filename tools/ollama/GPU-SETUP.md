# Giving the builder assist a GPU

Step-by-step for putting a card in front of Ollama on the TrueNAS SCALE box. Written for the
GTX 1060 6 GB that is going in, but the procedure is the same for any NVIDIA card; only the
"what to expect" section changes.

**Nothing here is urgent.** Since the startup warm-up landed (`AssistWarmUp`), the half-hour canon
prefill happens once, at boot, with nobody waiting on it, and drafts run at about three minutes on
CPU alone. A card makes that pleasant. It is no longer the difference between the feature working
and not working.

---

## 0. Before you start: what a 6 GB card actually buys

The weights are **8.1 GB** (Gemma 3 12B, Q4_K_M). That single number decides everything:

- **Generation is memory-bandwidth bound.** Every token reads every active weight. If the weights
  do not fit in VRAM, the layers that did not fit are evaluated on the CPU for every token, and the
  speed-up is roughly proportional to the fraction that did fit.
- **Prefill is compute bound.** It benefits from a card whether or not the model fits, which is why
  even a partial offload should take a serious bite out of the warm-up.

| card | VRAM | bandwidth | holds 8.1 GB? |
|---|---|---|---|
| GT 1030 2 GB | 2 GB | 48 GB/s | no — not worth the slot |
| GTX 1060 6 GB | 6 GB | 192 GB/s | no — roughly 26–30 of 48 layers |
| RTX 3050 6 GB | 6 GB | 168 GB/s | no, and *less* bandwidth than the 1060 |
| Arc Pro B50 | 16 GB | 224 GB/s | yes — but confirm Ollama's Intel support first |
| RTX 3060 12 GB | 12 GB | 360 GB/s | yes |

So with the 1060, expect the warm-up to shorten noticeably and drafts to improve modestly. Full
residency — generation jumping from ~0.93 tok/s to the teens — needs 12 GB, not 6.

---

## 1. Host: install the NVIDIA driver

TrueNAS SCALE **ships** the NVIDIA driver but does **not install it by default**.

1. Shut down, seat the card, boot.
2. In the web UI: **Apps → Settings → Install NVIDIA Drivers** (wording varies slightly by release).
   This also installs the container toolkit, which is the part that lets Docker hand a device to a
   container.
3. Reboot if it asks.

Verify at a host shell before touching anything else:

```sh
nvidia-smi                       # the card, its driver version, and 0 MiB in use
docker info | grep -i runtime    # `nvidia` must appear among the runtimes
```

If either fails, stop here — the rest of this document will only produce a container that refuses
to start, which is a confusing way to discover the driver is missing.

---

## 2. Apply the overlay

The GPU settings live in [`docker-compose.truenas.gpu.yml`](../../docker-compose.truenas.gpu.yml),
**an overlay, not an edit to the base file**. A device reservation the host cannot satisfy does not
degrade gracefully — the container refuses to start — so the base file has to keep working on a box
with no card.

```sh
cd /path/to/diku-web
docker compose -f docker-compose.truenas.yml -f docker-compose.truenas.gpu.yml up -d ollama
```

Check the merge first if you want to see exactly what compose will do:

```sh
docker compose -f docker-compose.truenas.yml -f docker-compose.truenas.gpu.yml config
```

**Every later compose command for this stack needs both `-f` flags.** Leave one off and compose
recreates the container without the reservation. If that gets tedious, set it once per shell:

```sh
export COMPOSE_FILE=docker-compose.truenas.yml:docker-compose.truenas.gpu.yml
```

### What the overlay contains

- `deploy.resources.reservations.devices` — one NVIDIA device with the `gpu` capability. This is
  the modern spelling; `runtime: nvidia` still works and is what older guides show, but it is
  deprecated and cannot say *which* devices or how many.
- `OLLAMA_FLASH_ATTENTION=1` and `OLLAMA_KV_CACHE_TYPE=q8_0` — halves the KV cache, which on a
  small card buys about four more of the 48 layers. Quantised KV requires flash attention, which is
  why they are set together. On a card that fits the whole model they buy nothing and can come out.

### What it deliberately leaves alone

`cpus: 4.0`, `OLLAMA_NUM_THREAD` and `mem_limit` stay exactly as the base file sets them. **A card
that cannot hold the whole model does not stop the CPU working** — the layers that did not fit are
still evaluated on it for every token. The reason those limits exist, which is keeping a generation
from taking all six cores away from the game loop, applies exactly as it did before. Revisit them
only if a card ever holds the model outright.

---

## 3. Verify

```sh
docker exec dikuweb-ollama nvidia-smi
```
The card, from inside the container. If this fails, the reservation did not take and nothing below
will tell you anything useful.

```sh
docker exec dikuweb-ollama ollama ps
```
Load a model first — ask for a draft in the builder, or run `create-models.sh`. **The PROCESSOR
column is the real answer:**

- `100% CPU` — the card is present but unused. Something is wrong.
- `38%/62% CPU/GPU` — partial offload. **This is what to expect on the 1060.**
- `100% GPU` — the whole model is resident.

```sh
docker logs dikuweb-web | grep -i "assist warm"
```
What it was actually worth. The warm-up is pure prefill, and prefill is where a GPU helps most.
Compare against the CPU-only baseline of roughly half an hour.

---

## 4. Rolling back

Drop the second `-f` and bring it up again:

```sh
docker compose -f docker-compose.truenas.yml up -d ollama
```

Nothing persistent changes, so this is safe at any point. The model files live in the `ollama`
volume and are untouched by any of this.

---

## Troubleshooting

**Container will not start, "could not select device driver ... with capabilities: [[gpu]]"**
The container toolkit is missing or the driver did not load. Back to step 1: `docker info | grep -i
runtime` must list `nvidia`.

**`nvidia-smi` works on the host but not in the container**
The reservation is not being applied — almost always a compose command run without both `-f` flags.
Check with `docker inspect dikuweb-ollama | grep -i -A5 devicerequest`.

**PROCESSOR says `100% CPU` with the card visible**
The model could not be laid out on the GPU at all. Usually VRAM already in use by something else —
`nvidia-smi` on the host will show the other process. A display manager on the same card counts.

**It got *slower***
Possible on a very small card: if only a handful of layers fit, the PCIe round-trip per token can
cost more than it saves. `OLLAMA_NUM_GPU` can pin the layer count explicitly if you want to
experiment, but the honest answer for a 2 GB card is not to bother.

---

## See also

- [`README.md`](README.md) — the measured CPU numbers, the memory arithmetic, and why the canon
  prefix is not baked into the Modelfile.
- [`docker-compose.truenas.gpu.yml`](../../docker-compose.truenas.gpu.yml) — the overlay itself,
  with the same prerequisites and verification steps in its comments.
