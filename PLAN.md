# diku-web — Build Plan

A browser-played, text-driven multi-user dungeon. Original world and rules design,
DikuMUD only in spirit. C# / .NET 10 server, PostgreSQL for data, React client.
Server→client push over **Server-Sent Events**; client→server commands over **HTTP POST**.

Rooms render as a small ASCII map so the client feels semi-graphical — but **play is
classic MUD**: you type commands, you move room to room, position never affects the rules.

The world is built through a **web-based builder** in the same app. Zones carry fractional
multipliers that scale base monsters and treasure, so difficulty tiers are a new zone with new
numbers rather than a new set of hand-authored content.

Status: planning. Nothing built yet.

---

## 1. Decisions already made

| Decision | Choice | Why |
|---|---|---|
| Server language | C# / .NET 10 | Installed, strong typing, real threads for the tick loop |
| Transport | SSE (down) + HTTP POST (up) | No WebSocket; debuggable with `curl`, native browser reconnect |
| Database | PostgreSQL 18 | Supported to Nov 2030; native `uuidv7()` for append-ordered keys |
| World design | Fully original | Not vnums/zones/THAC0 — own model and math |
| Hierarchy | World → Zone → Room | Multiple worlds supported (planes, continents, event realms) |
| Room map | Cosmetic ASCII grid | Occupants and contents carry x,y + icon **for rendering only** |
| World authoring | Web builder, Postgres-only | No content files; the editor is the single authoring path |
| Edit application | Live immediate | Saves reach the running world at once; no draft/publish gate |
| Difficulty scaling | Fractional zone multipliers | One template library, many power tiers |
| Logging | `ILogger<T>` + `[LoggerMessage]` | Zero-allocation on the hot loop; no Serilog |
| First milestone | Vertical slice | Thin but real end-to-end before breadth |
| Client | React + Vite + TypeScript | Game panels plus the builder, one app, shared auth |

### Open questions (decide before the phase that needs them)

- **Q1 (Phase 4):** Is player-vs-player combat in scope at all, or PvE only?
- **Q2 (Phase 4):** Permadeath, XP loss, or corpse-retrieval on death?
- **Q3 (Phase 5):** Are the four Paths (§4.5) fixed at creation or respec-able?

---

## 2. Architecture

### 2.1 The one rule that shapes everything

**A single game-loop thread owns all mutable world state.** Nothing else ever touches it.
HTTP handlers do not mutate the world; they hand messages to the loop. This removes every
lock, race, and deadlock from the game logic and makes ticks deterministic and testable.

**Builder edits obey this too.** A room save is not a direct database write — it is a
`WorldMutation` queued into the same inbound channel as player commands, applied by the loop,
then written through to Postgres. One writer, always.

```
                                  ASP.NET Core (Kestrel, HTTP/2)
   ┌──────────┐  POST /api/game/command   ┌──────────────────────────┐
   │          │ ────────────────────────► │  CommandController       │
   │  Browser │       202 Accepted        │  validate + enqueue only │
   │  (React) │ ◄──────────────────────── └────────────┬─────────────┘
   │          │                                        │
   │  game    │  PATCH /api/builder/rooms ┌────────────┴─────────────┐
   │    +     │ ────────────────────────► │  BuilderController       │
   │ builder  │   200 + applied entity    │  role check + enqueue,   │
   │          │ ◄──────────────────────── │  await loop ack (§7.3)   │
   │          │                           └────────────┬─────────────┘
   │          │                                        │
   │          │              Channel<Inbound>  (commands + mutations, bounded)
   │          │                                        ▼
   │          │                        ┌───────────────────────────────┐
   │          │                        │      GAME LOOP THREAD         │
   │          │                        │   250 ms pulse, single owner  │
   │          │                        │                               │
   │          │                        │  drain commands + mutations   │
   │          │                        │  dispatch → handlers          │
   │          │                        │  run scheduled systems:       │
   │          │                        │    combat / regen / spawn /   │
   │          │                        │    mob AI / affects / decay   │
   │          │                        │  emit outbound events         │
   │          │                        │  emit persistence jobs        │
   │          │                        └───┬───────────────────────┬───┘
   │          │                            │                       │
   │          │                    ┌───────▼────────┐              │
   │          │                    │ LAYOUT SERVICE │  presentation only —
   │          │                    │ entity → x,y   │  assigns map cells,
   │          │                    │ (cosmetic)     │  never read by rules
   │          │                    └───────┬────────┘              │
   │          │      Channel<OutboundEvent> per session      Channel<PersistJob>
   │          │                            │                       │
   │          │  GET /api/game/stream      ▼                       ▼
   │          │ ◄──────────────────  ┌───────────┐        ┌──────────────────┐
   │EventSource│    text/event-stream │SSE writer │        │ Persistence      │
   └──────────┘                      │  per conn │        │ worker (batched) │
                                     └───────────┘        └────────┬─────────┘
                                                                   ▼
                                                            ┌─────────────┐
                                                            │ PostgreSQL  │
                                                            └─────────────┘
```

**No database call, HTTP call, or file read ever happens on the game loop thread.**
The loop reads from an in-memory world snapshot loaded at boot and writes through a queue.
A watchdog logs any pulse exceeding its budget.

### 2.2 Projects

```
diku-web/
├─ DikuWeb.sln
├─ Directory.Build.props            # shared TFM (net10.0), nullable, warnings-as-errors
├─ docker-compose.yml               # postgres:18 + adminer
├─ PLAN.md
├─ src/
│  ├─ DikuWeb.Domain/               # entities, rules, scaling math, validation.
│  │                                # ZERO external deps. NO coordinates.
│  ├─ DikuWeb.Engine/               # game loop, systems, dispatch, world state
│  │  ├─ Mutations/                 # builder edits applied on the loop
│  │  └─ Presentation/              # RoomLayoutService — the only place x,y exists
│  ├─ DikuWeb.Persistence/          # EF Core 10 + Npgsql, migrations, repos, seeder
│  └─ DikuWeb.Server/               # ASP.NET Core: auth, REST, SSE, builder API, DI
├─ client/                          # React 19 + Vite + TypeScript
│  └─ src/
│     ├─ game/{map,room,scrollback,input,vitals}/
│     ├─ builder/{tree,zone,room,grid,templates,spawners,canvas}/
│     └─ {net,state}/
└─ tests/
   ├─ DikuWeb.Domain.Tests/
   ├─ DikuWeb.Engine.Tests/
   └─ DikuWeb.Server.Tests/
```

Dependency direction is one-way: `Server → Engine → Domain`, `Server → Persistence → Domain`.
Domain never references anything.

There is no content project and no `content/` directory — the builder replaced them (§7).

### 2.3 Timing

One **pulse** = 250 ms. Systems run on pulse multiples:

| System | Every | Real time |
|---|---|---|
| Command + mutation drain | 1 pulse | 250 ms |
| Combat round | 8 pulses | 2 s |
| Mob AI / wander | 16 pulses | 4 s |
| Spawn sweep | 60 pulses | 15 s |
| Regen + affect expiry | 240 pulses | 60 s |
| Autosave (staggered) | 1200 pulses | 5 min |

Time comes from an injected `IGameClock`; tests substitute a manual clock and step pulses by
hand, so a "ten minutes of combat" test runs in milliseconds. All randomness comes from an
injected `IRandomSource` seeded per test — no `Random.Shared` in Domain or Engine.

### 2.4 Logging

`ILogger<T>` at every call site, with all messages declared as **`[LoggerMessage]`
source-generated methods** — no inline template strings.

```csharp
internal static partial class GameLoopLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Pulse {Pulse} took {ElapsedMs} ms (budget {BudgetMs} ms)")]
    public static partial void SlowPulse(ILogger logger, long pulse, double elapsedMs, double budgetMs);
}
```

This is a performance requirement, not a style rule. A conventional
`_logger.LogDebug("...{A}...", a)` allocates a `params object[]` and boxes value types **at the
call site**, before the provider decides the level is disabled. At 4 pulses/sec across 200
sessions that is constant allocation pressure on the loop thread, and the resulting GC pauses
land directly on the 25 ms p99 pulse budget (§11). The source generator emits an `IsEnabled`
check first and returns before doing any work — zero allocation when the level is off. It also
validates templates against parameters at compile time.

Providers: built-in console in development, OpenTelemetry exporter in production (Phase 6).
**No Serilog** — with OTel already handling structured log export, it would be a second pipeline
doing the same job. The only thing it would add is a rolling file sink, which is worth revisiting
only if we want local log files without running a collector.

Never logged: passwords, session cookies, password-reset tokens. Player command input goes to
`command_log` for moderation, not to the application log.

---

## 3. Transport design

### 3.1 Why SSE works here

MUD traffic is asymmetric: the client sends one small command every few seconds, the server
pushes a continuous stream. That is exactly SSE's shape. Over WebSocket it wins on being plain
HTTP (proxies, curl, and logging all just work), on `EventSource` reconnecting automatically
with `Last-Event-ID`, and on needing no framing library at either end.

### 3.2 Endpoints

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/auth/register` | Create account |
| `POST` | `/api/auth/login` | Sets HttpOnly session cookie |
| `POST` | `/api/auth/logout` | |
| `GET`  | `/api/characters` | List account's characters |
| `POST` | `/api/characters` | Create character |
| `POST` | `/api/game/enter` | Bind session to a character, put it in the world |
| `GET`  | `/api/game/stream` | **SSE**, long-lived |
| `POST` | `/api/game/command` | `{ "input": "north" }` → `202 Accepted`, empty body |
| — | `/api/builder/**` | Builder API, see §7.3 |
| `GET`  | `/health` | Liveness/readiness |

**Auth must be cookie-based.** The browser's native `EventSource` cannot set request headers,
so a bearer token in an `Authorization` header is impossible on the stream. Use an HttpOnly,
`Secure`, `SameSite=Lax` session cookie. Do not put tokens in the query string — they land in
access logs.

### 3.3 The command POST returns nothing

`POST /api/game/command` responds `202 Accepted` with an empty body. **All** output — including
the direct result of your own command — comes back over the SSE stream.

This is deliberate. If some output came over the POST response and some over SSE, the two would
interleave unpredictably and the scrollback would show events out of order. One ordered channel
means what you read is what happened, in the order it happened.

Builder endpoints are the exception and return their result synchronously (§7.3) — they are rare,
and a builder needs to know the save succeeded.

### 3.4 SSE stream mechanics

```
GET /api/game/stream
Accept: text/event-stream

retry: 3000

id: 1041
event: text
data: {"spans":[{"t":"You walk north."}]}

id: 1042
event: room
data: {"key":"aldenmoor.millbrook.north-gate","title":"The North Gate",
       "description":"A weathered portcullis...","exits":["north","east","south"]}

id: 1043
event: map
data: {"w":11,"h":6,"terrain":["###########","#.........#", ...],
       "entities":[{"id":"c_7","icon":"@","x":4,"y":2,"label":"you"},
                   {"id":"m_31","icon":"k","x":7,"y":2,"label":"a kobold sentry"}]}

: ping
```

Required server behavior:

- `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`.
- **Disable response buffering** (`IHttpResponseBodyFeature.DisableBuffering()`) and flush after
  every event, or nothing reaches the browser until the buffer fills.
- Heartbeat comment (`: ping`) every 15 s so proxies and load balancers don't reap the connection.
- Monotonic `id:` per session and a **250-event ring buffer** per session, so a reconnect
  carrying `Last-Event-ID` replays what it missed instead of dropping output.
- **Serve over HTTP/2.** Browsers cap HTTP/1.1 at ~6 connections per origin; a player with
  several tabs open would starve. HTTP/2 multiplexes and the limit disappears.
- Kestrel `KeepAliveTimeout` and any proxy read-timeout must exceed the heartbeat interval.

### 3.5 Event types (server → client)

| `event:` | Payload | Notes |
|---|---|---|
| `text` | `{ spans: [{ t, style? }] }` | Styled markup, **not** raw ANSI — client owns the theme |
| `room` | key, title, description, exits | Title/description panel |
| `map` | w, h, terrain rows, entity list with icon + x,y | Full snapshot, sent on room entry |
| `mapdelta` | added / removed / moved entities | Sent when the room's contents change |
| `contents` | occupants and ground items, icon + label | The "Here:" list beside the map |
| `vitals` | hp / focus / stamina / xp / level | Status bar |
| `inventory`, `equipment` | item lists | Sent on change, not polled |
| `combat` | attacker, target, verb, damage band | Lets the UI flash; prose still arrives via `text` |
| `sys` | connection notices, link-dead warnings, forced logout | |

A builder edit to an occupied room pushes fresh `room` / `map` events to everyone standing in
it, so live edits are visible without relogging.

### 3.6 Disconnect handling

If the SSE stream drops, the character does **not** vanish. It is marked link-dead and stays in
the world for 90 s (and can still be attacked — classic MUD risk). A reconnect inside that
window rebinds the session and replays the ring buffer. After 90 s the character is saved and
removed. A `sys` event warns the player as the window runs out.

---

## 4. World and game design v0

Starting proposal, deliberately not Diku. Revisable until Phase 4 begins.

### 4.1 Hierarchy

**World → Zone → Room.**

- **World** — a top-level realm: `aldenmoor`, `the-underdark`, a seasonal event realm. Worlds
  are real partitions; travel between them is deliberate and rare (portals, not walking).
  Carries its own multipliers, applied on top of every zone inside it.
- **Zone** — an authored area within a world: `millbrook`, `sunken-crypt`. The unit of content
  ownership, spawning, level range, **and difficulty scaling**.
- **Room** — the atomic location. Addressed by a stable, human-readable key
  `world.zone.room` (e.g. `aldenmoor.millbrook.north-gate`).

### 4.2 The room map is cosmetic — and structurally so

Every room has a small terrain grid, and every occupant and ground item is drawn at an x,y with
an icon. **This is presentation only.** It exists so the client can render a room semi-graphically
instead of as a wall of prose.

The rules of play are untouched and stay classic MUD:

| | |
|---|---|
| Movement | `north` moves you to the **next room**. There is no stepping within a room. |
| Combat | Anyone in the room can engage anyone else. No range, no adjacency, no line of sight. |
| Interaction | `get sword`, `kill kobold` work on anything in the room, wherever it's drawn. |
| Pathing | Mobs wander room to room. They never path across cells. |

This is enforced structurally, not by discipline: **Domain entities have no x,y field at all.**
Coordinates live only in `DikuWeb.Engine/Presentation/RoomLayoutService`, which sits downstream
of the rules and is consulted only when building `map` and `mapdelta` events. Game logic
*cannot* read a position, because from Domain's perspective positions do not exist.

An architecture test asserts this: no type in `DikuWeb.Domain` may declare a coordinate, and no
command handler may reference `RoomLayoutService`.

*(The builder stores separate `editor_x, editor_y` on rooms for its zone canvas — §7.2. That is
layout metadata for the editing UI, equally invisible to the rules.)*

### 4.3 How positions get assigned

Since position carries no meaning, the server picks it — builders don't hand-place every kobold.

```
cell(entity) = probe(hash(roomKey, entityId) mod walkableCells, occupied)
```

A stable hash of the entity id picks a candidate walkable cell; linear probing finds the next
free one if taken. Properties that fall out of this:

- **Stable.** The same kobold sits in the same spot across reconnects and server restarts —
  it's a pure function, so nothing needs persisting.
- **Consistent.** Every client viewing the room draws it identically.
- **Free.** No coordinate columns, no migrations, no save/load path.

Two refinements: a character arriving from an exit is placed on the nearest free cell to that
exit tile, so movement reads coherently; and authored props (altar, fountain, signpost) may
declare a fixed cell.

Terrain is decoration and spawn-placement surface only. Walls don't block anything — there is
nothing to block. Exits are room-to-room edges and are **not** tied to grid cells.

### 4.4 Zone multipliers — the difficulty dial

Templates define **one** baseline monster or item. Zones scale it. A new difficulty tier is a
new zone with bigger numbers, not a new set of hand-built content.

All multipliers are fractional, default `1.0`, and apply as `world × zone`:

| Multiplier | Scales | Notes |
|---|---|---|
| `strength` | Monster health **and** damage together | The master difficulty dial |
| `health` | Monster health | Fine-tune on top of `strength` |
| `damage` | Monster damage | Fine-tune on top of `strength` |
| `xp` | XP awarded on kill | Independent — a zone can be hard but stingy |
| `gold` | Coin drops | |
| `itemValue` | Vendor value of items | |
| `itemPower` | Item stat bonuses (weapon damage, armor) | |
| `spawnDensity` | Spawner target counts | Makes a zone crowded, not just tougher |

Resolution:

```
effective = round(base × world.mult × zone.mult × zone.statMult)

health, damage:      max(1, …)     — never scale to nothing
xp, gold, itemValue: max(0, …)     — a multiplier of 0.0 legitimately means "none"

rounding: half away from zero
```

Worked example — one `kobold-sentry` template with base 40 hp, 4–7 damage, 120 xp, 25 gold:

| Zone | `strength` | `gold` | `xp` | Result |
|---|---|---|---|---|
| `millbrook` | 1.0 | 1.0 | 1.0 | 40 hp, 4–7 dmg, 120 xp, 25 gold |
| `sunken-crypt` | 2.5 | 3.0 | 1.0 | 100 hp, 10–18 dmg, 120 xp, 75 gold |
| `the-deep` | 6.0 | 8.0 | 2.0 | 240 hp, 24–42 dmg, 240 xp, 200 gold |

**Multipliers bake in at spawn time**, not read time. When a spawner creates a mob it resolves
the arithmetic once and stores concrete values on the instance, along with a
`spawn_multipliers jsonb` snapshot recording what was applied — so "why does this kobold have
137 hp?" is answerable from the row.

Two things follow. First, combat math (§4.6) never learns that zones exist; it stays a pure
function of two combatants. Second, editing a multiplier affects **future** spawns only —
already-living mobs keep the numbers they were born with. The builder therefore gets an explicit
*Respawn zone* action to see a change immediately.

### 4.5 Attributes, vitals, Paths

Five attributes replacing the classic six: **Might, Agility, Vitality, Insight, Resolve.**
Range 1–20, modifier = `(value − 10) / 2` rounded down.

Three vitals: **Health** (damage pool, zero = death), **Focus** (powers abilities — the mana
analogue), **Stamina** (movement and heavy attacks).

Four Paths chosen at creation: **Warden** (armored frontline), **Adept** (focus-caster),
**Shade** (stealth/burst), **Channeler** (support/control). A Path grants an ability list and
shapes stat growth; it does not hard-gate equipment.

### 4.6 Combat math

Explicitly not THAC0. Each round, per attack:

```
attackRoll  = d20 + attackRating           attackRating  = level/2 + MightMod + weaponBonus
defenseVal  = 10 + defenseRating           defenseRating = AgilityMod + armorDefense + shield

miss   if attackRoll <  defenseVal
hit    if attackRoll >= defenseVal
crit   if natural 20, or beats defenseVal by 10+   → damage dice rolled twice

damage = weaponDice + MightMod (+ ability riders)
final  = max(1, (damage − armorFlat) × (1 − armorPercent))
```

No positional terms and no zone terms anywhere in the formula — the whole thing is a pure
function of the two combatants, which is exactly what makes it unit-testable in isolation.

### 4.7 Progression

Levels 1–50. XP from kills, quest completion, and first-time room discovery — rewarding
exploration, which is what a MUD is for. Each level grants attribute and ability points spent
deliberately: point-buy rather than use-based improvement, because use-based systems are
notoriously hard to balance and invite grinding.

### 4.8 Content model

- **Template → Instance.** `MobTemplate`/`ItemTemplate` hold the baseline; `Mob`/`Item` are
  runtime instances with concrete, multiplier-resolved stats. Replaces Diku "resets".
- **Spawner.** A declarative rule on a zone: *maintain N of template X across these rooms,
  respawn D seconds after death.* A population target, not an imperative reset script.
- Templates are **global**, not zone-scoped. That is what makes the multiplier design pay off —
  the same `kobold-sentry` appears in a starter zone and an endgame zone at wildly different
  power, with one definition.

### 4.9 Quests and storylines

The quest loop is deliberately one shape: **talk to an NPC, fetch an item, deliver it.**
Every quest is that. Storylines are built by *chaining* quests through prerequisites, not by
making any single quest multi-step — so the engine stays a four-state machine no matter how
long the story runs.

```
   talk <giver>          kill a mob / find a ground spawn        give <item> <turn-in>
        │                              │                                  │
        ▼                              ▼                                  ▼
   ┌─────────┐                   ┌──────────┐                      ┌───────────┐
   │ (no row)│ ────────────────► │  Active  │ ───────────────────► │ Completed │
   │ Available│  prerequisites   └──────────┘   has required item  └───────────┘
   └─────────┘   met, row created                                        │
        ▲                                                                │
        └──────── unlocks the next quest in the chain ────────────────────┘
```

The giver and the turn-in NPC **may be different mobs** — that is what turns a fetch into a
story ("take this to the captain at the gate"). When they are the same mob, it reads as an
ordinary errand.

**No accept step.** Talking to the giver starts the quest outright. An accept/decline prompt
adds a round trip and a state (`Offered`) that earns nothing here, since accepting a fetch
quest costs the player nothing.

**Turn-in is strict.** Handing over the item only completes a quest the character has
*Active*. A player who stumbles across the ledger before ever meeting Kaelen cannot skip
straight to the reward — otherwise chains could be completed out of order and the story would
read as nonsense.

**NPCs refuse what they do not want**, with the item staying in the player's inventory. An NPC
that silently swallowed a mis-targeted item would destroy player property on a typo.

**Dialogue is state-dependent**, which is what makes NPCs feel like they remember you. Four
strings per quest, stored as `jsonb`:

| Speaker | State | Example |
|---|---|---|
| Giver | `offer` | *"My ledger was taken when the kobolds came through. Find it?"* |
| Giver | `inProgress` | *"Still no ledger? They fled north, toward the gate."* |
| Giver | `complete` | *"You have my thanks. The captain should hear of this."* |
| Turn-in | `ready` | *"That ledger — Kaelen sent you? Give it here."* |

**Rewards** are XP, gold, and optionally an item. XP and gold pass through the quest's zone
multipliers (§4.4) exactly as kill rewards do, so a storyline running through a high-tier zone
pays out at that tier without re-authoring numbers.

**Quest items are ordinary items with one flag.** They come from the normal spawner and loot
systems — no separate quest-item pipeline. `questItem: true` only means *cannot be sold or
destroyed*; they can still be dropped. Soft-locking is mostly prevented by the spawner system
already: kill the mob again, or wait for the ground spawn to respawn, and the item is
obtainable again. The flag exists so a player cannot vendor the ledger and then wait out a
respawn timer for no reason.

**Commands:** `talk <npc>`, `give <item> <npc>` (already Phase 3), `quests` for the journal, and
`quest <name>` for detail on one.

**Objectives are one item type plus a count**, which covers both "find the ledger" and "bring
me five wolf pelts". Multi-objective quests (kill N of X *and* visit room Y) would need an
objectives child table; that is a deliberate later extension, not a Phase 5 goal.

---

## 5. Game client layout

Five regions. The map is a monospace `<pre>` grid; everything else is ordinary DOM.

```
┌──────────────────────────────┬────────────────────────────────────┐
│                              │  The North Gate                    │
│      ###########             │  ────────────────────────────────  │
│      #.........#             │  A weathered portcullis stands     │
│      #...@..k..#             │  half-raised above the road, its   │
│      #....+....#             │  iron teeth furred with rust.      │
│      #.......$.#             │  Cart ruts run north into hills.   │
│      #####.#####             │                                    │
│                              │  Exits: north, east, south         │
│   ROOM MAP (cosmetic)        ├────────────────────────────────────┤
│                              │  Here:                             │
│   @ you                      │    k  a kobold sentry              │
│   k a kobold sentry          │    $  a pile of copper coins       │
│   + a mossy altar            │    +  a mossy altar                │
│   $ a pile of copper coins   │                     CONTENTS       │
├──────────────────────────────┴────────────────────────────────────┤
│  You walk north.                                                  │
│  A kobold sentry snarls and bares its teeth.                      │
│  Kael says, 'watch the gate, I'll take the road'                  │
│                                              SCROLLBACK           │
├───────────────────────────────────────────────────────────────────┤
│  > kill kobold▌                                        INPUT      │
├───────────────────────────────────────────────────────────────────┤
│  HP ████████░░ 42/60    FO ██████████ 30/30    ST ███████░░░ 88/100│
│  Warden · level 7 · 12,480 xp                            VITALS   │
└───────────────────────────────────────────────────────────────────┘
```

- **Map** — redrawn from `map` / `mapdelta`. Icon legend underneath, so a `k` is identifiable
  without hovering.
- **Room** — title, description, exits. Driven by the `room` event.
- **Contents** — occupants and ground items with their icons, so the map and the list read as
  one thing. Clicking an entry inserts its keyword into the input box.
- **Scrollback** — the authoritative game text. Everything that happens appears here, even when
  it's also reflected on the map. A player who ignores the map misses nothing.
- **Input** — command line with history (↑/↓), tab-completion of visible keywords, and aliases.
- **Vitals** — bars for Health, Focus, Stamina plus Path, level, and XP.

Responsive: below ~900 px the map and room panels stack above the scrollback, and the map
collapses to a toggle so the text still dominates on mobile.

---

## 6. Data model

**Postgres is the only source of truth.** There are no content files. The builder writes the
world; `pg_dump` is the backup; `content_audit` is the history.

### Core tables

```
accounts            id, email citext unique, username citext unique, password_hash,
                    role, created_at, last_login_at, is_banned, ban_reason

characters          id, account_id → accounts, name citext unique, path, level, xp,
                    attributes jsonb, vitals jsonb, room_key, created_at,
                    last_played_at, playtime_seconds, deleted_at null

worlds              key pk, name, description, multipliers jsonb, sort_order
zones               key pk, world_key → worlds, name, description,
                    level_range int4range, multipliers jsonb
rooms               key pk, zone_key → zones, title, description, flags jsonb,
                    grid text[], legend jsonb,        -- terrain art + icon map
                    editor_x, editor_y                 -- builder canvas only, nullable
room_exits          from_room_key, direction, to_room_key, door jsonb
                    PK(from_room_key, direction)
                    -- to_room_key intentionally NOT a FK: live editing must tolerate
                    -- an exit pointing at a room that does not exist yet (§7.4)

mob_templates       key pk, name, description, icon, level, base_stats jsonb,
                    base_xp, base_gold, behavior jsonb, loot jsonb
item_templates      key pk, name, description, icon, slot, weight,
                    base_value, base_stats jsonb
spawners            id, zone_key → zones, template_key, template_kind,
                    room_keys text[], target_count, respawn_seconds

item_instances      id, template_key, state jsonb, resolved_stats jsonb,
                    spawn_multipliers jsonb,
                    -- exactly one location must be non-null:
                    owner_character_id null, container_item_id null, room_key null,
                    equipped_slot null
                    CHECK (num_nonnulls(owner_character_id, container_item_id, room_key) = 1)

content_audit       id, account_id, entity_kind, entity_key, action,
                    before jsonb, after jsonb, at
                    -- replaces git history for world content

quests              key pk, zone_key → zones, name, summary, description,
                    giver_mob_key, turnin_mob_key,
                    required_item_key, required_count,
                    reward_xp, reward_gold, reward_item_key, reward_item_count,
                    prerequisite_quest_keys text[], is_repeatable, dialogue jsonb,
                    sort_order
                    -- giver/turnin/required/reward keys are NOT foreign keys: a builder
                    -- wires a quest before creating the mob or item it references, and
                    -- live editing must tolerate that (§7.4)

character_quests    character_id → characters, quest_key, status,
                    started_at, completed_at, times_completed
                    PK(character_id, quest_key)
                    -- no row means "not started". Status is only Active or Completed.

character_discovery character_id, room_key, discovered_at      PK(character_id, room_key)
character_abilities character_id, ability_key, rank
command_log         id, character_id, input, at
```

Notes:

- **No entity x,y columns.** Positions are derived (§4.3). `rooms.editor_x/y` is the one stored
  coordinate and belongs to the builder canvas, not the game.
- `multipliers jsonb` on both `worlds` and `zones` — a small map of the §4.4 keys. `jsonb`
  because the set will grow as we find new dials worth having.
- `room_exits.to_room_key` is deliberately **not** a foreign key. Live editing means a builder
  links an exit before creating its destination; a FK would reject the save. Dangling exits are
  a validation warning, and in-game they fail closed (§7.4).
- `citext` for names and emails, so `Kael` and `kael` can't both be registered.
- Indexes: `item_instances(owner_character_id)`, `item_instances(room_key)`,
  `characters(account_id)`, `rooms(zone_key)`, `zones(world_key)`,
  `spawners(zone_key)`, `content_audit(entity_kind, entity_key, at desc)`,
  `command_log(character_id, at desc)`,
  `quests(giver_mob_key)` and `quests(turnin_mob_key)` — both are hit on every `talk` and
  every `give`, so they must not be sequential scans,
  `character_quests(character_id) where status = 'Active'` — a partial index, since the
  journal and every turn-in check only ever read active rows.
- **PostgreSQL 18**, pinned in `docker-compose.yml` and in the Testcontainers image tag so dev,
  test, and prod agree. Chosen over 16 for the support window (Nov 2030 vs Nov 2028 — free on a
  greenfield project) and for `uuidv7()`.
- **UUID primary keys are UUIDv7**, not UUIDv4. UUIDv4 is random, so inserts scatter across the
  whole B-tree, splitting pages and bloating the index. UUIDv7 embeds a timestamp and sorts, so
  inserts land append-mostly. This matters most on the append-heavy tables — `command_log`,
  `content_audit`, `item_instances` — and `command_log`'s `(character_id, at desc)` index
  benefits directly.
  Generated **in .NET** via `Guid.CreateVersion7()`, with `DEFAULT uuidv7()` on the column as a
  backstop for rows inserted by hand or by script. App-side generation is the primary path
  because it gives an entity its key before insert, so relationships can be wired up in memory
  and EF does not need a `RETURNING` round-trip to learn the id it just created.
- Passwords: ASP.NET Core `PasswordHasher<T>` (PBKDF2) minimum; Argon2id preferred.
- Migrations via EF Core, checked in, applied explicitly on deploy — never `EnsureCreated`.

---

## 7. The world builder

A second surface in the same React app at `/builder`, behind the `Builder` role. Shares auth,
API client, and types with the game.

### 7.1 What it edits

| Screen | Edits |
|---|---|
| **World tree** | Worlds → zones → rooms, searchable. Create, rename, delete, move. |
| **World** | Name, description, world-level multipliers. |
| **Zone** | Name, description, level range, **multipliers with live preview** (§7.5). |
| **Room** | Title, description, flags, ASCII grid painter, legend, exits. |
| **Zone canvas** | Rooms as boxes, exits as lines. Drag to arrange, drag between boxes to link. |
| **Item templates** | Name, description, icon, slot, weight, base value, base stats. |
| **Mob templates** | Name, description, icon, level, base stats, base xp/gold, behavior, loot. |
| **Spawners** | Template, target rooms, count, respawn seconds. |
| **Quests** | Giver mob, turn-in mob, required item and count, rewards, prerequisites, and the four dialogue strings (§4.9). |
| **Storyline graph** | Quests as nodes, prerequisites as edges. Shows the chain, flags cycles and unreachable quests. |

Placing a one-off object in a room is just a spawner with `target_count: 1` — the builder offers
it as *Place item here* rather than making you think about spawners.

### 7.2 The grid painter and zone canvas

The room grid is edited as ASCII art: pick a legend character, click or drag to paint cells,
resize the rectangle from its edges. The legend maps characters to tile kinds and props.

The zone canvas needs rooms to have positions *within the zone* — this is what `editor_x/y` is
for. Rooms without stored coordinates get auto-laid-out from the exit graph on first open, and
hand-arranging persists. Explicitly builder-only data: players never see a zone map, and no
rule reads it.

### 7.3 Builder API

```
GET  POST PATCH DELETE  /api/builder/worlds[/{key}]
GET  POST PATCH DELETE  /api/builder/zones[/{key}]
GET  POST PATCH DELETE  /api/builder/rooms[/{key}]
GET  POST PATCH DELETE  /api/builder/rooms/{key}/exits[/{direction}]
GET  POST PATCH DELETE  /api/builder/mob-templates[/{key}]
GET  POST PATCH DELETE  /api/builder/item-templates[/{key}]
GET  POST PATCH DELETE  /api/builder/spawners[/{id}]
GET  POST PATCH DELETE  /api/builder/quests[/{key}]

GET  /api/builder/quests/{key}/reachability  -- can the required item actually be obtained?
GET  /api/builder/storyline?zone={key}       -- quest graph: chain order, cycles, dead ends

POST /api/builder/rooms/{key}/dig         -- create + link a neighbouring room (§7.6)
     { direction, reciprocal: true, zoneKey?: string, newRoomKey?: string }

POST /api/builder/zones/{key}/respawn     -- despawn + respawn, to see multiplier changes now
GET  /api/builder/zones/{key}/preview     -- resolved stats for every template in this zone
GET  /api/builder/zones/{key}/validate    -- advisory warnings, never blocking
GET  /api/builder/zones/{key}/unfinished  -- rooms still flagged unfinished, the build to-do list
```

Every mutation is enqueued as a `WorldMutation` on the game loop's inbound channel, preserving
the single-writer rule (§2.1). Unlike player commands these are **request/response**: the
enqueued message carries a `TaskCompletionSource` that the loop completes after applying, so the
HTTP call returns `200` with the applied entity — or the validation error. Builder traffic is
rare enough that awaiting a pulse (≤250 ms) costs nothing.

All mutations write a `content_audit` row with before/after.

### 7.4 Live editing means the world must tolerate being broken

With no draft/publish gate, a builder cannot create a room and link its exit atomically. The
world is therefore **allowed to be invalid**, and the engine must never throw on the loop thread
because of it. Failure is always closed and always narrated:

| Broken state | In-game behavior |
|---|---|
| Exit points at a nonexistent room | **Player:** movement fails, *"The way is blocked."* Logged once per room, not per attempt. **Builder:** *"There is no room north yet. Type `dig north` to create it."* (§7.6) |
| Room deleted with occupants inside | Occupants are moved to the zone entrance with a `sys` notice. Never orphaned. |
| Spawner references a deleted template | Spawner goes dormant and reports a warning. No crash, no empty mobs. |
| Quest references a deleted mob or item | Quest goes dormant: the giver stops offering it, and an already-Active copy stays in the journal marked *unavailable* rather than being deleted from the character. Never silently wipe player progress. |
| Quest prerequisites form a cycle | Every quest in the cycle is unstartable. Reported by `/storyline`, not enforced at save time. |
| Room has no description | Renders a placeholder; the room still works. |
| Grid rows ragged or legend incomplete | Map falls back to a plain floor rectangle of the same size. |
| Zone deleted with players inside | Blocked outright — the one destructive edit that requires the zone be empty. |

Validation is **advisory**: `/validate` reports dangling exits, orphan rooms, empty spawners,
unreachable areas, and **quest items that nothing drops or spawns** as warnings in the builder
UI. It never rejects a save.

That last check is worth calling out. An unobtainable quest item is the classic quest bug: the
quest looks correct in the editor, the dialogue reads fine, and it is simply impossible to
finish because no mob's loot table and no spawner ever produces the item. Nothing in the normal
flow surfaces it — the player just wanders. So `/reachability` walks loot tables and spawners to
prove each required item has at least one source, and does the same for reward items. This is the direct
cost of choosing live-immediate over draft/publish, and paying it in engine robustness is
cheaper than paying it in builder friction.

### 7.5 Multiplier preview

The reason the whole feature exists, so it deserves real UI. Editing a zone's multipliers shows
a live table of what every template resolves to in that zone:

```
Zone: sunken-crypt      strength 2.5    gold 3.0    xp 1.0    spawnDensity 1.5

  kobold-sentry     40 hp  →  100 hp      4-7 dmg  →  10-18 dmg     25g → 75g
  cave-lurker       85 hp  →  213 hp     9-14 dmg  →  23-35 dmg     60g → 180g
  rusted-blade                            3-6 dmg  →   3-6 dmg      12g → 36g
```

Sliders update the right-hand column instantly. *Respawn zone* applies it to living mobs.

### 7.6 Walk-and-build

The fastest way to lay out geography is to walk it. A builder moves through the world with
ordinary movement commands and creates rooms in the direction they want to go, so the exit graph
is correct **by construction** rather than wired up afterward in a form.

**Two operations, one endpoint.** They differ by whether an exit already exists:

| Situation | `dig north` does |
|---|---|
| **Materialize** — an exit north exists but its target room doesn't (a dangling link, §7.4) | Creates the room using **the key the exit already names**, so the existing link resolves. |
| **Dig** — there is no exit north at all | Creates a room with a generated key *and* the exit to reach it. |

Both go through `POST /api/builder/rooms/{key}/dig`; the server picks the behavior from current
state. That keeps one code path and means a builder never has to know which case they're in.

**Reciprocal exits are the default.** Digging north also links the new room back south. Passages
that only work one way — chutes, trapdoors, one-way portals — pass `reciprocal: false`. Two-way
is right often enough that the inverse should be the deliberate act.

**Generated keys are provisional.** A dug room gets `<zone>.room-<n>` with the lowest free `n`.
Nobody is prompted for a slug mid-walk; rename later in the room editor, which rewrites inbound
exit references in the same mutation. Materialized rooms keep the key the exit already named.

**New rooms are born unfinished.** Title *"An Unfinished Room"*, a placeholder description, no
grid art (so §7.4's default rectangle renders), and `flags.unfinished = true`. That flag is the
build to-do list: `/unfinished` returns everything still stubbed in a zone, and the zone canvas
draws those rooms hatched. Editing the title and description clears it.

**Zone defaults to the source room's zone.** Digging never silently crosses a zone boundary;
`zoneKey` in the body places the new room elsewhere for deliberate border work.

**Canvas placement is automatic.** The new room's `editor_x/y` (§7.2) is offset one step from
the source in the dug direction — north is `y−1`, east is `x+1`, and up/down reuse the source
cell with a level marker. Walk-building therefore produces a zone canvas that already reads like
a map, with no dragging.

**Follow mode ties the two surfaces together.** This is what makes "edit the room you're standing
in" work properly: the builder panel tracks the character's current room and re-targets its form
on every move. Navigate spatially with `north`/`south` — far faster than clicking a tree — and
edit in a real form, because nobody should type a four-sentence room description onto a command
line. In-game commands stay for things that are one short argument:

```
dig <dir> [into <zone>]     create + link a room (or materialize a dangling exit)
link <dir> <room-key>       point an exit at an existing room
unlink <dir>                remove an exit
rtitle <text>               set the title of the room you're in
goto <room-key>             jump anywhere, no exits required
```

**Safety.** Builder role only — players never auto-create anything, and a player hitting a
dangling exit still just gets *"The way is blocked."* `dig` is rate-limited to guard against a
key-repeat sending a walk command forty times, every dig writes a `content_audit` row, and
deleting a room dug by mistake is an ordinary delete gated on the room being empty.

---

## 8. Phases

The builder lands early — with no content files, it is the only way to author a world, so it
gates all content work after Phase 1.

### Phase 0 — Foundation ✅ **complete**
*Done when: `docker compose up`, `dotnet run`, and `npm run dev` all work, and `/health` is 200.*

- [x] Solution + all projects, `Directory.Build.props` (net10.0, nullable, warnings as errors)
- [x] Central package management in `Directory.Packages.props`
- [x] `docker-compose.yml` with postgres:18 + adminer, credentials in `.env`
- [x] EF Core + Npgsql wired, `accounts` + `characters`, initial migration applied
- [x] Logging per §2.4; config via `appsettings.*.json` + user-secrets
- [x] React + Vite client scaffold, dev proxy to the API
- [x] `IGameClock` + `IRandomSource` from the first commit (§9)
- [x] Test projects: 33 .NET tests + 3 client tests; Testcontainers on postgres:18
- [x] `.editorconfig`, `.gitignore`, README with local setup steps
- [x] Verified end to end: browser → Vite proxy → Kestrel → Postgres

Notes from the build:

- **PG 18 changed its Docker volume path.** The data directory moved to a major-version
  subdirectory, so the volume must mount at `/var/lib/postgresql`, not
  `/var/lib/postgresql/data` as it was through PG 17. Mounting the old path makes the
  container refuse to start outright.
- `Roll(...)` lives in `RandomSourceExtensions` rather than as a default interface member,
  because default members are only callable through the interface — implementations would not
  expose them, making every call site and test awkward.

### Phase 1 — Vertical slice ✅ **complete**
*Done when: two browsers log in, walk a code-seeded zone, see each other on the map, and talk.*

- [x] Register / login / logout, HttpOnly session cookie
- [x] Character creation (name, Path) and character list
- [x] Session registry: session ↔ character ↔ outbound channel
- [x] Game loop hosted service: 250 ms pulse, command drain, slow-pulse watchdog
- [x] SSE endpoint with heartbeat, `id:`, ring buffer, `Last-Event-ID` replay
- [x] Command parser: verb + args, prefix abbreviation (`n`, `lo`, `sa`), unknown-verb help
- [x] Commands: `look`, `north/south/east/west/up/down`, `say`, `who`, `quit`, `help`
- [x] World → Zone → Room model + `world.zone.room` key resolution
- [x] `RoomLayoutService`: deterministic cell assignment, `map` events
- [x] Architecture test: no coordinates in Domain, no layout access from command handlers
- [x] **Code seeder**: 12-room starter zone (Aldenmoor / Millbrook)
- [x] Client: all five game panels (§5), input history, icon legend
- [x] Save on quit and autosave; restore room on re-entry
- [x] Link-dead handling with 90 s grace window
- [x] 139 .NET tests + 8 client tests; two-player flow verified end to end

Notes from the build:

- **`mapdelta` was not needed yet.** With only players in the world, a full `map` on change is
  a few hundred bytes and simpler than diffing. Revisit in Phase 3 when mobs and ground items
  make rooms busy enough for the delta to earn its keep.
- **Zone multipliers (§4.4) are deliberately absent from the schema.** They are Phase 3 work and
  nothing in Phase 1 reads them; adding the column now would be speculative. It arrives as a
  migration alongside the code that uses it.
- **Saves hand over an immutable `CharacterSnapshot`, not the live entity.** The loop keeps
  mutating a `Character` while the save worker runs on another thread, so passing the object
  itself would be a data race — and the symptom would be a character saved with the room from
  one moment and the vitals from another.
- **`RoomKey` is a validated value type**, not a bare string. Two-segment keys and stray
  uppercase now fail at the boundary rather than becoming a room that silently never resolves.
- **Overflow placement stacks rather than drops.** A room with fewer walkable cells than
  occupants used to omit the extras from the map entirely — present in the room and the contents
  list, invisible on the grid. Overlapping icons are the better failure.

### Phase 2 — World builder: geography
*Done when: a new zone can be built end to end in the browser, with no SQL and no seeder edits.*

- [ ] Roles (player / builder / moderator / admin) + authorization policies **(moved up from Ops)**
- [ ] `WorldMutation` path: enqueue → loop applies → persist → notify occupants
- [ ] Request/response mutation pattern with loop ack (§7.3)
- [ ] World / zone / room / exit CRUD API + `content_audit` on every write
- [ ] Graceful degradation for every broken state in §7.4 — with tests for each row
- [ ] Advisory `/validate`: dangling exits, orphan rooms, unreachable areas
- [ ] Builder UI: world tree, world editor, zone editor, room editor
- [ ] ASCII grid painter with legend management
- [ ] Zone canvas: auto-layout from the exit graph, drag to arrange, drag to link
- [ ] Live push of edits to players standing in an edited room
- [ ] **Walk-and-build (§7.6):** `dig` endpoint covering both materialize and dig cases
- [ ] Reciprocal exits, provisional key generation, automatic `editor_x/y` placement
- [ ] `flags.unfinished` on new rooms, `/unfinished` to-do list, hatched on the canvas
- [ ] In-game builder commands: `dig`, `link`, `unlink`, `rtitle`, `goto`
- [ ] Follow mode: builder panel re-targets the room the character walks into
- [ ] Room rename rewrites inbound exit references in the same mutation
- [ ] Dig rate limiting; builder-only gating with player fallback to "The way is blocked."

### Phase 3 — Objects, inhabitants, and multipliers
*Done when: a zone's difficulty is a slider, and the same kobold is trivial in one zone and lethal in another.*

- [ ] Item templates + instances, weight and capacity limits
- [ ] `get`, `drop`, `give`, `put`, `inventory`, `examine`
- [ ] Equipment slots: `wear`, `wield`, `remove`; containers with depth limit
- [ ] Mob templates, spawners, population maintenance
- [ ] **Multiplier resolution at spawn time** (§4.4), `spawn_multipliers` recorded per instance
- [ ] World + zone multiplier storage and `world × zone` composition
- [ ] Builder: mob template, item template, and spawner editors
- [ ] Builder: multiplier panel with live preview table (§7.5) and *Respawn zone*
- [ ] Mob AI v1: idle emotes, room-to-room wandering, `sentinel` flag
- [ ] Ground items and mobs appear on the map with their icons
- [ ] `emote` and a social table

### Phase 4 — Combat and progression
*Done when: you can kill something, loot its corpse, and level up — and the multipliers visibly matter.*

- [ ] Combat state machine on the 2 s round system
- [ ] `kill`, `flee`, `consider`, auto-attack continuation
- [ ] Damage model per §4.6, injectable RNG, full unit coverage of the formula
- [ ] Death: corpse creation, item transfer, respawn point, penalty (**resolve Q2**)
- [ ] XP awards with zone `xp` multiplier, leveling, point spend
- [ ] Regen tied to Vitality and rest state (`sleep`, `rest`, `stand`)
- [ ] Aggressive mobs, assist behavior, target selection
- [ ] Resolve **Q1** (PvP scope) before writing target-validation rules

### Phase 5 — Depth
- [ ] Ability system: cost, cooldown, cast time, targeting rules
- [ ] Per-Path ability trees (**resolve Q3** on respec)
- [ ] Buffs/debuffs with duration, stacking, expiry on the 60 s tick
- [ ] Shops, currency, `buy` / `sell` / `list` — priced through `itemValue`
- [ ] **Quest engine (§4.9):** `quests` + `character_quests`, Active/Completed only
- [ ] `talk <npc>`: starts a quest when prerequisites are met, state-dependent dialogue
- [ ] `give` hook: completes a quest on a strict match, otherwise the NPC refuses and keeps nothing
- [ ] Rewards through the quest zone's `xp` and `gold` multipliers
- [ ] `questItem` flag: not sellable, not destroyable, still droppable
- [ ] Prerequisite chains — the mechanism storylines are built from
- [ ] `quests` journal and `quest <name>` detail
- [ ] Builder: quest editor, storyline graph, `/reachability` and cycle detection
- [ ] Dormant-quest handling when a referenced mob or item is deleted (§7.4)
- [ ] Communication: `tell`, channels, `group`/party, party XP split
- [ ] A second world reachable by portal, with its own world-level multipliers

### Phase 6 — Operations
- [ ] Admin commands: `goto`, `teleport`, `stat`, `set`, `mute`, `kick`, `ban`
- [ ] Rate limiting per session; command flood protection; builder API throttling
- [ ] OpenTelemetry: pulse duration p50/p99, sessions, commands/s, queue depths
- [ ] Scheduled `pg_dump` backups + a rehearsed restore drill
- [ ] World export/import (JSON) for moving content between environments
- [ ] Deployment: container + reverse proxy with SSE buffering off

---

## 9. Testing

| Layer | Approach |
|---|---|
| Domain | Pure unit tests. Combat formula, **multiplier resolution and rounding**, stat math, parser — no mocks, no I/O. |
| Engine | Manual clock + seeded RNG. Step N pulses, assert world state. Fully deterministic. |
| Architecture | Assert Domain declares no coordinates and command handlers never touch `RoomLayoutService` — the guardrail that keeps the map cosmetic as the codebase grows. |
| Layout | Same room + same occupants ⇒ identical cells across restarts; no two blocking entities share a cell. |
| Robustness | One test per row of §7.4. Delete a room out from under a player, point an exit at nothing, orphan a spawner — the loop must survive every one. |
| Multipliers | Boundary cases: `0.0` yields none, tiny fractions still yield ≥1 health, world × zone composes, rounding is half-away-from-zero. |
| Quests | The full loop: talk → drop → give → rewards. Plus the refusals — wrong NPC, no active quest, wrong item, insufficient count — each leaves the item in the player's inventory. Chains unlock in order and cannot be short-circuited by pre-holding the item. Deleting a referenced mob leaves an Active quest in the journal rather than wiping it. |
| Builder | Mutation → loop → persist → occupants notified, end to end. Audit row written on every write. |
| Server | `WebApplicationFactory` + Testcontainers Postgres, including an SSE test that opens the stream, POSTs a command, and asserts events arrive in order. |
| Client | Vitest for the protocol/state layer; Playwright for login → move → see-map, and build-a-room → walk-into-it. |

Determinism is not polish here — a game loop you cannot replay exactly is a game loop you cannot
debug. `IGameClock` and `IRandomSource` go in from the first commit.

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| Reverse proxy buffers SSE; output arrives in bursts or never | `X-Accel-Buffering: no`, disable Kestrel response buffering, flush per event, verify against the real proxy early |
| HTTP/1.1 6-connection cap starves multi-tab players | Serve over HTTP/2 |
| Blocking I/O sneaks onto the game loop and stalls the world | All DB work through the persistence channel; watchdog logs over-budget pulses; a test asserts the loop makes no DB calls |
| **World content has no version history now that files are gone** | `content_audit` with before/after on every mutation; scheduled `pg_dump`; Phase 6 JSON export. Note honestly: this is weaker than git, and it is the price of the Postgres-only choice |
| **A bad live edit is visible to players instantly, with no rollback** | Advisory validation surfaced in the UI, `content_audit` to identify and hand-revert, destructive deletes gated on the room/zone being empty |
| **Multiplier misconfiguration ships an unkillable zone** | Preview table before saving; `strength` on a sane slider range with a warning past ~10×; multipliers apply only on respawn, so there is a window to notice |
| **An unfinishable quest — required item that nothing drops or spawns** | `/reachability` walks loot tables and spawners before it ships. This fails silently in play: the quest reads correctly and the player just wanders, so it must be caught in the editor |
| **A content edit strands players mid-storyline** | Deleting a referenced mob or item makes the quest dormant, never deletes `character_quests` rows. Progress survives the content churn that live editing invites |
| Map creep — someone adds range checks and the cosmetic grid quietly becomes a rules system | Coordinates physically absent from Domain; architecture test fails the build; §4.2 is the contract to point at in review |
| Idle SSE connections reaped by infrastructure | 15 s heartbeat, keep-alive timeouts tuned above it |
| Missed events on flaky mobile connections | `Last-Event-ID` + 250-event ring buffer replay |
| Grid art for every room becomes a chore | Terrain is optional — a room with no grid renders a default rectangle. Art is an upgrade, not a tax |
| Design churn breaks the schema repeatedly | `jsonb` for in-flux stat blocks and multipliers; promote to columns once stable |
| Command flooding as cheap DoS | Bounded inbound channel, per-session rate limit, backpressure returns 429 |
| Scaling past one process (loop is single-threaded by design) | Shard by zone across processes later; keep every cross-entity interaction zone-local now so that stays possible |

---

## 11. Non-functional targets (v1)

- 200 concurrent sessions on one process
- Pulse duration p99 < 25 ms (10% of the 250 ms budget)
- Command → first SSE byte, p95 < 150 ms
- Builder mutation round-trip p95 < 400 ms (one pulse + persist)
- Map redraw < 16 ms on a mid-range laptop
- Zero character data loss on graceful shutdown; ≤ 5 min on hard crash (autosave interval)

---

## 12. Next step

Phase 0 is unblocked and needs no further decisions. Say the word and I'll scaffold the solution,
projects, `docker-compose.yml`, first EF migration, and the Vite client.
