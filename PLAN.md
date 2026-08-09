# diku-web — Build Plan

A browser-played, text-driven multi-user dungeon. Original world and rules design,
DikuMUD only in spirit. C# / .NET 10 server, PostgreSQL for data, React client.
Server→client push over **Server-Sent Events**; client→server commands over **HTTP POST**.

Rooms render as a small ASCII map so the client feels semi-graphical — but **play is
classic MUD**: you type commands, you move room to room, position never affects the rules.

The world is built through a **web-based builder** in the same app. Zones carry fractional
multipliers that scale base monsters and treasure, so difficulty tiers are a new zone with new
numbers rather than a new set of hand-authored content.

Play is **PvE by default**; player-versus-player is opt-in per room, through the same extensible
room-flag registry that carries every other room property (§4.10).

Status: **Phase 5 complete (0–4 and 5.1–5.3). Remaining: Phase 6.**

*Playing* works end to end: register, create a character, walk a seeded zone, talk. Inventory,
equipment, and items work; mob AI emotes and wanders; combat kills, loots, and levels, with XP
penalties and respawn. Each Path learns eight abilities to level 20. Quests run the full
talk → collect → give → reward loop with prerequisite chains and zone-scaled rewards. Shops buy
and sell. Builder access is granted from inside the game.

*Authoring* now covers the whole content model in the browser with no SQL: geography, worlds,
zones, difficulty multipliers with a live preview, mob and item templates, mob behavior
(disposition, emotes, shopkeeper and stock), mob and item spawners across multiple rooms, and
quests — the last with reachability warnings and the prerequisite chain shown in the editor.

*Playing together* works: parties form by invitation, share a kill, and are the one thing §4.11
will not let you aim at. `tell`, `reply`, a world channel, and a party channel carry a
conversation between rooms; `recall` returns you to where you bound.

Next: **Phase 6 (ops)** — admin commands, rate limiting, telemetry, backups, and the recovery
half of the runbook.

A note on reading the checkboxes below: several were checked off for work that was designed but
not built, or built but dead on arrival. Where an audit has since disproved one it has been
un-checked and annotated rather than deleted, because the pattern matters — **a box here means
"someone believed this was done", and only a test or a read of the code means it is.**

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
| Room flags | Open registry over `flags jsonb` | New flags cost a registry entry, not a migration (§4.10) |
| Granting roles | In-game `promote`, over an admin API | Reaching the builder must not require SQL (§7.7) |
| PvP | PvE by default, opt-in per room via the `pvp` flag | Geography decides, not a global switch (§4.11) |
| Death | XP loss, no corpse retrieval, respawn at your bind point | Costly enough to matter, never a naked corpse run (§4.12) |
| First milestone | Vertical slice | Thin but real end-to-end before breadth |
| Client | React + Vite + TypeScript | Game panels plus the builder, one app, shared auth |

### Open questions (decide before the phase that needs them)

*None. **Q1** (PvP) → §4.11. **Q2** (death penalty) → §4.12. **Q3** (Path respec) → §4.5:
**Paths are fixed at creation.***

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
| Combat readiness check | 1 pulse | 250 ms |
| Mob AI / wander | 16 pulses | 4 s |
| Spawn sweep | 60 pulses | 15 s |
| Regen + affect expiry | 240 pulses | 60 s |
| Autosave (staggered) | 1200 pulses | 5 min |

Combat has no shared round. Every combatant carries its own attack clocks — a player's from the
weapons in each hand, a mob's one per entry in its template's attack list — so the system is
evaluated every pulse and each attack fires when its own delay has elapsed. Delays are authored
in whole pulses with a floor of 4 (1 s); silence means 8 pulses, which is the single shared round
this replaced.

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
| `GET`  | `/api/game/sessions` | Which of this account's characters are in the world |
| `POST` | `/api/game/{characterId}/enter` | Put that character in the world |
| `GET`  | `/api/game/{characterId}/stream` | **SSE**, long-lived |
| `POST` | `/api/game/{characterId}/command` | `{ "input": "north" }` → `202 Accepted`, empty body |
| `POST` | `/api/game/{characterId}/leave` | Remove it now rather than waiting out link-dead |
| — | `/api/builder/**` | Builder API, see §7.3 |
| — | `/api/admin/**` | Role administration, see §7.7 |
| `GET`  | `/health` | Liveness/readiness |

**Auth must be cookie-based.** The browser's native `EventSource` cannot set request headers,
so a bearer token in an `Authorization` header is impossible on the stream. Use an HttpOnly,
`Secure`, `SameSite=Lax` session cookie. Do not put tokens in the query string — they land in
access logs.

**Game routes are scoped by character, not by account**, so one login can drive several
characters simultaneously — each with its own stream, scrollback, and link-dead window. The
cookie still does all the authorising; the id in the path only selects *which* of the caller's
own characters is meant, and ownership is re-checked on every request. That is materially
different from a token in a URL: the id is not a secret (it is already returned by
`/api/characters`) and possessing it grants nothing.

Two consequences worth stating:

- **Sessions are keyed by character.** Keying by account made entering a second character
  silently evict the first. The Engine already keyed players by character id, so this was
  purely a Server-side limitation.
- **A per-account cap applies** (`Sessions:MaxConcurrentCharactersPerAccount`, default 3).
  Each character in the world holds an open SSE connection, a session, and a 250-event ring
  buffer, so an uncapped account could exhaust server resources by looping over its character
  list. This is a resource bound, not a game rule about multi-boxing — raise it freely.
  Re-entering a character already in the world is a reconnect and does not consume a slot.

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
**Shade** (stealth/burst), **Hallow** (support/control). A Path grants an ability list and
shapes stat growth; it does not hard-gate equipment.

*The fourth Path was called **Channeler** through Phase 5. "Hallow" says what it does — a name
that reads as healing at a glance, while still covering Wither, Sap, and Enfeeble, which "Mender"
would have misdescribed. Renamed while it was cheap: `characters.path` stores the enum's name, so
this cost one data migration, and the ability keys cost nothing because `character_abilities` (§6)
does not exist yet and the seeder reconciles `hallow.*` against the catalogue on every boot.*

**A Path is fixed at creation and cannot be changed** (resolves **Q3**). Rerolling is the respec:
characters are cheap to make, levelling is the game, and a second character on the same account
costs nothing. What a respec would buy is the ability to skip that, and what it would cost is
everything downstream of "your abilities follow from your Path" — spent ability points to refund
or strand, equipment chosen for a Path you no longer are, and a progression table that has to
mean something on a character who arrived at level 20 by a different road. None of that is worth
building to save a player from a decision made on the character-creation screen.

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

**Whether a fight is allowed at all is decided before this math runs**, by room flags (§4.10):
`peaceful` forbids combat entirely, and player-versus-player requires the `pvp` flag (§4.11).
Target validation is a separate gate on purpose — the damage formula never learns who is a
player and who is a mob, so it stays the same pure function either way.

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
  **A mob spawner counts what it made, wherever that has got to** — not what is standing in its
  rooms. Counting by room meant a mob that wandered one step out stopped counting and was
  replaced, and the replacement wandered off as well.
  **An item spawner counts by room, deliberately.** It is a resupply point rather than a
  population cap: take the herb and another grows, which only works if what is in your pack has
  stopped counting. The same field answers two different questions.
- **A wandering mob stays in the zone it spawned in**, unless its template sets `roams`. A zone is
  the unit difficulty is authored in (§4.4), so a mob that crosses a border carries numbers
  resolved from somewhere else's multipliers. Fencing by geography meant flagging every border
  room `noMob` and remembering to do it again whenever a builder dug a new exit; fencing by origin
  is a property of the mob, so it cannot be forgotten. This does not change `noMob`, which says
  *not into this room* where the home zone says *not out of that zone* — both must hold.
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

### 4.10 Room flags — an open registry, not an enum

Rooms carry a `flags jsonb` map of flag key → value. It is deliberately **not** a bit field or a
C# `[Flags]` enum, because both make every new flag a schema change: a migration, a backfill, and
a coordinated deploy. Here a new flag costs a registry entry and the code that reads it. Nothing
else.

The registry is a static table in Domain and is the single source of truth for what a flag means:

```csharp
public static class RoomFlags
{
    public static readonly RoomFlag Pvp = Register("pvp", RoomFlagKind.Boolean, @default: false,
        summary: "Players may attack one another here.");
    …
}
```

Adding a flag is three steps, none of which touch the database:

1. One `Register(...)` line.
2. The code that reads it, via the typed accessor — `room.Flags.IsSet(RoomFlags.Pvp)`, never a
   string literal at the call site, so a typo is a compile error rather than a flag that is
   silently always off.
3. Nothing in the builder. The room editor renders its checkboxes **from the registry**, so a new
   flag appears in the UI with its summary as the label the moment it is registered.

Four rules make this safe to extend:

- **Absence is the safe value, always.** This is why the flag is `pvp` rather than `safe`. Every
  room authored before the flag existed, every room whose key was mistyped, and every room whose
  jsonb failed to parse resolves to *not* PvP. A `safe` flag would have inverted that and made
  forgetting a flag dangerous. Any future flag must be phrased so the default is the harmless one.
- **Unknown keys are preserved, never stripped.** A save round-trips flags the running binary does
  not recognise, so rolling a server back does not quietly erase flags a newer one wrote.
  `/validate` reports them as advisory warnings (§7.4).
- **Flags are read, never written, by game logic.** They are content, edited through the builder
  like a room's description — no spell, trap, or quest reward toggles one. The builder's `rflag`
  command (§7.6) looks like an in-game action but is not: it is a `WorldMutation` that writes a
  `content_audit` row like every other edit. If a *temporary* room state is ever wanted — a
  ritual that pacifies a chamber for a minute — it needs its own expiring-effect mechanism, not
  a write into content.
- **Inheritance resolves room → zone → world → registry default.** The same three-level hierarchy
  the multipliers use (§4.4), but *overriding* rather than composing: the nearest level that
  declares the flag wins. An arena world or a duelling zone sets `pvp` once instead of on forty
  rooms. The room editor shows inherited values greyed out with their source, so a room that is
  PvP because of its zone never looks like a plain unflagged room.

The starting registry:

| Flag | Default | Read by | Meaning | Enforced in |
|---|---|---|---|---|
| `pvp` | false | combat targeting | Players may attack one another here (§4.11) | Phase 4 |
| `peaceful` | false | combat targeting | No combat at all, mobs included | Phase 4 |
| `respawn` | false | death, `bind` | A valid bind point (§4.12) | Phase 4 |
| `noMob` | false | mob AI | Wandering mobs will not path in | Phase 3 |
| `noRecall` | false | `Travel.Refuse` | `recall` and any future teleport out are refused | Phase 5.3 |
| `dark` | false | room rendering | Description withheld without a light source | Phase 5 |
| `indoors` | false | presentation | Shelters from weather when weather exists | later |
| `unfinished` | false | builder | The build to-do list (§7.6) | Phase 2 |

**`peaceful` beats `pvp`.** A room carrying both is peaceful. Conflicts must resolve toward the
safe value for the same reason absence does.

### 4.11 Player-versus-player

**PvE by default. A player may attack another player only in a room that resolves `pvp` true.**
PvP is a property of geography, not a global server switch and not a per-character opt-in flag —
so an arena, a contested dungeon floor, or a duelling ground is authored in the builder like any
other content, and the rest of the world stays safe without anyone configuring anything.

- **Checked every round, not only at initiation.** Combat is room-local (§4.2), so both
  combatants are in the same room by definition and there is one check. If either party leaves
  for a non-PvP room the fight ends immediately. That makes a safe room a genuine escape, which
  is the point of putting the rule in the geography: you can always see where you stand.
- **Mobs are unaffected.** PvE combat works everywhere except `peaceful` rooms. The `pvp` flag
  only ever widens what a *player* may target.
- **A refused attack is narrated, never silent.** *"You cannot attack Kael here."*
- **Pets and charmed mobs inherit their owner's permissions.** A pet is an extension of the player
  who commands it; routing an attack through one must not launder it past the check. *There is no
  pet system and none is planned — see §13, which records this as the constraint that blocks one
  rather than as a feature waiting for it.* `HostileActionGate` is where it would have to land,
  and it must land **before** anything can be commanded, not alongside it.
- **Area effects filter per target, not per room.** An AoE cast in a mixed room hits the mobs and
  skips the players. Running the check once for the room would make a single flag the difference
  between a spell and a massacre.
- **Party members are never valid targets**, `pvp` room or not — this exists so an AoE cannot wipe
  your own group by accident. Checked *before* the `pvp` flag, because being grouped is the
  stronger statement: an arena is exactly where a group stands next to people it is fighting.
  Only hostile actions ask. **Heals, buffs, and helpful area effects target the group freely** —
  that is what a group is for, and the gate is never consulted for them.
- **PvP kills go to the moderation log**, not the application log, so griefing patterns are
  visible without reading `command_log` by hand.

The economics are deliberately flat: a PvP kill yields no XP, no loot, and costs the loser no XP
(§4.12). There is nothing to farm, so PvP is for people who want the fight — the flag decides
*where* that is possible and nothing else is stacked on top of it.

### 4.12 Death — XP loss, no corpse run

Death costs experience and time. It never costs items and it never costs the character.

| | |
|---|---|
| **Items** | Kept. Everything: inventory, equipment, coin. |
| **Player corpse** | None is created. There is nothing to run back to and nothing to lose to a looter. |
| **Mob corpse** | Unchanged — mobs still leave lootable corpses. That is how looting works (Phase 4). |
| **XP** | A bounded loss, below. |
| **Level** | Never lost. |
| **Location** | Respawn at your bind point. |

**The XP penalty is a fraction of the current level band, floored at the level threshold:**

```
band(level) = xpForLevel(level + 1) − xpForLevel(level)
loss        = round(band(level) × Death:XpLossPercent)     -- default 0.10
xp          = max(xpForLevel(level), xp − loss)
```

Expressing the loss against the *band* rather than against total XP keeps the penalty
proportionate as the curve steepens — 10% of a level's worth of progress means the same thing at
level 6 and at level 46, where 10% of lifetime XP would be catastrophic late and trivial early.

Two consequences, stated plainly rather than discovered in play:

- **Dying just after levelling is free; dying just before levelling costs the full 10%.** That is
  the price of clamping at the threshold, and it errs in the forgiving direction — a fresh level
  is a safe moment to push into something dangerous. The alternative, letting XP go negative into
  a debt that must be repaid before progress resumes, punishes exactly the players already
  struggling. Rejected.
- **De-levelling is impossible by construction**, not by a check somewhere. A character cannot
  drop below a level and lose the abilities and spent points that came with it, so no code
  downstream needs to handle a character who has un-learned something.

**No XP loss** below `Death:XpLossMinLevel` (default 5 — the early levels are the tutorial and
there is nothing there worth taking), and **none on a PvP death** (§4.11).

**Respawn location resolves in order, falling through if a room no longer exists** — live editing
means a bind point can be deleted out from under a player (§7.4):

```
1. characters.respawn_room_key    -- set by `bind` in a room that resolves `respawn` true
2. the entrance room of the character's home zone
3. EngineOptions.StartingRoom     -- the world's origin, guaranteed to exist
```

A character who has never used `bind` therefore respawns where they first entered the world, which
is the simple behaviour; `bind` and the `respawn` flag exist so builders can place waypoints as the
world grows outward, without that being a second system.

**On respawn** the character is at 25% Health, 0 Focus, 0 Stamina, and out of combat. The real
cost of dying is the walk back and the rest afterwards; reviving at full vitals would make death
a teleport, and reviving at 1 HP would stack a second punishment on top of the XP already lost.

Death while link-dead follows exactly the same path — the character is still in the world for the
grace window and can still be killed (§3.6), so the respawn has to work with nobody watching.

Every knob here is configuration, not a constant:

| Setting | Default |
|---|---|
| `Death:XpLossPercent` | `0.10` |
| `Death:XpLossMinLevel` | `5` |
| `Death:RespawnHealthPercent` | `0.25` |
| `Death:PvpCostsXp` | `false` |

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
                    last_played_at, playtime_seconds, deleted_at null,
                    respawn_room_key null    -- `bind` point (§4.12); not a FK, the room
                                             -- can be deleted under a bound character

worlds              key pk, name, description, multipliers jsonb, flags jsonb, sort_order
zones               key pk, world_key → worlds, name, description,
                    level_range int4range, multipliers jsonb, flags jsonb
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

admin_audit         id, actor_account_id, target_account_id, action,
                    before, after, reason, at
                    -- role changes (§7.7), and later mute/kick/ban. Deliberately NOT
                    -- content_audit: an account is not content, and merging the two
                    -- makes "who edited this room" and "who promoted this person"
                    -- both harder to answer

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
- `flags jsonb` on all three of `worlds`, `zones`, and `rooms`, for the same reason and resolved
  nearest-level-wins (§4.10). A flag the running binary does not recognise is round-tripped, not
  dropped. **No flag ever becomes a column** — the moment one does, adding the next one is a
  migration again and the whole point is lost. Query flags with the jsonb operators
  (`flags @> '{"pvp": true}'`), and add a GIN index on `rooms(flags)` only if a flag search ever
  shows up in a plan; at world sizes measured in thousands of rooms it will not.
- `characters.respawn_room_key` is nullable and deliberately **not** a foreign key, matching
  `room_exits.to_room_key`: a builder can delete a room that characters are bound to, and death
  falls through to the next candidate (§4.12) rather than the save failing.
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
- Migrations via EF Core, checked in, applied at startup — never `EnsureCreated` (§6.1).
- Identifiers are snake_case throughout, enforced by convention rather than by review (§6.1).

### 6.1 Deployment and migrations

**Migrations run at startup, in every environment.** `Program.cs` calls `MigrateAsync` before the
game loop starts, and the deploy has no separate migration step.

This is a deliberate reversal of the usual advice, and it rests on a property of this design
rather than on convenience. The game loop is **single-writer with no backplane** (§2.1): world
state lives in one process's memory, and nothing shares it. The app therefore cannot be scaled
horizontally at all, so the "multiple instances race to migrate" hazard has no way to occur —
there is never a second instance. Belt and braces: EF takes an exclusive advisory lock for the
duration of `MigrateAsync`, so an accidental second instance waits rather than collides.

If a backplane is ever added and the loop is sharded across processes (§10, last row), this
decision has to be revisited **at the same time** — it is the same change, not a follow-up.

**Startup tolerates a database that is not up yet.** `StartupMigrator` retries transient failures
with exponential backoff (1 s doubling to 10 s) for a budget of 60 s, configurable via
`Database:MigrationRetryBudgetSeconds`; zero disables the wait. Failing fast is right, but failing
on the *first* connection attempt is not — a Postgres accepting TCP while still finishing recovery
would otherwise cost a container restart, and Kubernetes escalates CrashLoopBackOff to five-minute
delays, so a ten-second hiccup becomes five minutes of downtime. Transience is Npgsql's own
classification, so a wrong password still fails in about a second rather than after a minute.

**What this trades away.** A bad migration now fails the deploy at container start rather than at
a gate before it. Rollback is *deploy the previous image*, not *stop the migration job*. Accepted
because the failure mode is loud: the container exits, the orchestrator does not route traffic,
and `/health/ready` (§3.2) fails on the database check regardless.

The rejected alternative was to start anyway and report not-ready until the database appears. That
is right for a stateless service and wrong here: `GameLoop` loads the entire world from Postgres as
its first act, so there is no degraded mode to serve from — "up but not ready" would just be "down,
with a process running".

**Migrations are still explicit and checked in.** Never `EnsureCreated`. The schema is described by
the migration history, not inferred from the model at runtime.

**Naming is a convention, not a habit.** `SnakeCaseNaming` rewrites every table, column, key,
index, and constraint EF generates to snake_case, filling in only what a configuration did not
name explicitly. Postgres folds unquoted identifiers to lower case, so a PascalCase table is one
that must be quoted forever in every hand-written query — and a mixed schema is worse than either
convention. Making it a convention rather than ~100 `HasColumnName` calls means a new entity
cannot drift back.

**Seeding stays development-only.** The starter world is a fixture, not schema.

**What if migration fails?**

- The app does not start. Fix the migration and redeploy; do not deploy around it.
- Postgres runs each migration in a transaction, so a failed one leaves the database unchanged.
- To step back deliberately: `dotnet ef database update --to-migration <previous>`.

---

## 7. The world builder

A second surface in the same React app at `/builder`, behind the `Builder` role. Shares auth,
API client, and types with the game.

### 7.1 What it edits

| Screen | Edits |
|---|---|
| **World tree** | Worlds → zones → rooms, searchable. Create, rename, delete, move. |
| **World** | Name, description, world-level multipliers, world-level flag defaults. |
| **Zone** | Name, description, level range, **multipliers with live preview** (§7.5), zone flag defaults. |
| **Room** | Title, description, **flags** (rendered from the registry, §4.10), ASCII grid painter, legend, exits. |
| **Zone canvas** | Rooms as boxes, exits as lines. Drag to arrange, drag between boxes to link. |
| **Item templates** | Name, description, icon, slot, weight, base value, base stats. |
| **Mob templates** | Name, description, icon, level, base stats, base xp/gold, behavior, loot. |
| **Spawners** | Template, target rooms, count, respawn seconds. |
| **Quests** | Giver mob, turn-in mob, required item and count, rewards, prerequisites, and the four dialogue strings (§4.9). Reachability warnings shown inline. |
| **Storyline graph** | The chain as an indented list — depth is what a builder needs to see, and indentation *is* "how far in is this". Flags cycles, unreachable quests, and prerequisites naming a quest that does not exist. |

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

GET  /api/builder/room-flags                 -- the flag registry: key, default, summary (§4.10)
                                             -- the room editor renders its checkboxes from this,
                                             -- so a newly registered flag needs no client change

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
| Flag key is not in the registry | Preserved on save, ignored by the engine, reported by `/validate`. Covers both a typo and a flag written by a newer binary (§4.10). |
| Flag value is the wrong type (`"pvp": "yes"`) | Treated as absent, so it resolves to the registry default — which is always the safe value. Advisory warning. |
| `pvp` cleared on a room mid-fight | The fight ends on the next round, exactly as if the combatants had walked out (§4.11). A live edit can stop a duel; it can never start one retroactively. |
| A character's bind room is deleted | Death falls through to the next respawn candidate (§4.12). The stale `respawn_room_key` is cleared the first time it fails to resolve. |
| Room has no description | Renders a placeholder; the room still works. |
| Grid rows ragged or legend incomplete | Map falls back to a plain floor rectangle of the same size. |
| Zone deleted with players inside | Blocked outright — the one destructive edit that requires the zone be empty. |

Validation is **advisory**: `/validate` reports dangling exits, orphan rooms, empty spawners,
unreachable areas, unrecognised flag keys, **rooms that are PvP only by inheritance** (§4.10), and
**quest items that nothing drops or spawns** as warnings in the builder UI. It never rejects a save.

The inherited-`pvp` warning is there because it is the one flag whose blast radius is larger than
the thing you edited: setting `pvp` on a zone makes every room in it lethal, including the town
square someone else authored last week. A warning listing exactly which rooms just became PvP
turns an invisible edit into a visible one.

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
rflag <flag> [on|off]       set or clear a flag here; bare `rflag` lists them with their source
goto <room-key>             jump anywhere, no exits required
```

`rflag` completes against the registry (§4.10), so it lists the flags that actually exist rather
than accepting any string and silently doing nothing.

**Safety.** Builder role only — players never auto-create anything, and a player hitting a
dangling exit still just gets *"The way is blocked."* `dig` is rate-limited to guard against a
key-repeat sending a walk command forty times, every dig writes a `content_audit` row, and
deleting a room dug by mistake is an ordinary delete gated on the room being empty.

### 7.7 Granting the builder role

Everything in §7 is gated on a role that, as shipped, can only be granted with
`UPDATE accounts SET role = 'Builder'`. That is a hole: the whole point of the builder is that
authoring a world needs no SQL, and requiring SQL to *reach* the builder concedes the argument on
the first step. The first account on an empty database becoming Admin is a bootstrap, not an
answer — it does nothing for the second person to join.

**An admin promotes from inside the game:**

```
promote <name> <role>       role is builder, moderator, admin, or player
demote <name>               shorthand for: promote <name> player
whois <name>                account, role, and whether they are online
```

Admin only, hidden from everyone else's `help` exactly as the builder verbs are (§7.6). The
target does **not** need to be online — these name an account, not a character in the room, which
is what makes them usable for the ordinary case of someone asking to help build.

Backed by `PATCH /api/admin/accounts/{username}/role` and `GET /api/admin/accounts?q=`, both
behind the Admin policy. The API is the real interface; the verbs are a convenience over it.

Three things make this harder than it looks, and each has to be handled explicitly.

**The loop cannot read the account store.** §2.1 forbids a database call on the loop thread, and
the Engine has no account repository at all — a character's role is carried in on `EnterWorld`
precisely because the loop cannot look it up. So `promote` is not an ordinary command handler
that does the work. It validates its arguments, enqueues an `AccountRoleChange` on a queue the
Server drains (the same shape as the world write queue, §7.6), and the worker performs the
change. This is the same fire-and-forget split, for the same reason.

**The result therefore has to come back asynchronously.** A role change can fail — no such
account, refusing to demote yourself — and the admin who typed the command deserves to be told.
That needs an inbound message the Server can send *to* the loop, addressed at a session:
`Notify(sessionId, message, kind)`, emitted as a `sys` event. This is the first outbound path
that does not originate from a player's own command, and it is generally useful: Phase 6's
`kick` and `ban` need exactly the same thing.

**The role lives in the auth cookie, so changing the row changes nothing.** The claim is written
at sign-in (§3.2), which means a freshly promoted builder keeps a cookie that still says Player
until they log out and back in, and — worse in the other direction — a *demoted* builder keeps
their access for up to fourteen days. The same flaw already applies to bans: `is_banned` is
checked at login and never again, so banning someone who is currently connected does nothing at
all until they choose to reconnect.

The fix is one mechanism for both: **revalidate the principal against the database** in
`CookieAuthenticationEvents.OnValidatePrincipal`, on an interval rather than every request. If
the stored role differs from the claim, refresh the principal; if the account is banned, reject
the cookie outright. A short interval (~60 s) is the right trade — role changes are rare, and
nobody is harmed by a promotion taking a minute to reach an open tab, while a ban that takes at
most a minute is a fundamentally different thing from one that takes two weeks.

A character already in the world also carries `PlayerActor.Role`, which is a copy made at
`EnterWorld` and would go stale the same way. The role-change worker therefore sends a
`SetActorRole` message to the loop, so the builder verbs light up (or stop working) without the
player relogging — matching how a live content edit reaches the room without a relog (§3.5).

**Every role change is audited.** Not to `content_audit` — an account is not content, and mixing
them makes both harder to read. A separate `admin_audit` table (§6) records actor, target,
action, before, and after, and Phase 6's `mute` / `kick` / `ban` write to the same place.

**Guard rails.** An admin cannot demote themselves — that is how an installation ends up with no
admins at all, and there is no recovery path from that state except the SQL this section exists
to remove. Promotion to Admin is permitted but confirmed, since Admin is the only role that can
grant Admin.

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

### Phase 2 — World builder: geography ✅ **complete**
*Done when: a new zone can be built end to end in the browser, with no SQL and no seeder edits.*

Rooms, exits, flags, and the canvas were done first and held up. The gap was a level up — the
**containers** those rooms live in — and `WorldPanel`, `ZonePanel`, and the per-flag primitives
for both scopes closed it.

- [x] Roles (player / builder / moderator / admin) + authorization policies **(moved up from Ops)**
- [x] `WorldMutation` path: enqueue → loop applies → persist → notify occupants
- [x] Request/response mutation pattern with loop ack (§7.3)
- [x] World / zone / room / exit CRUD API + `content_audit` on every write
- [x] **Room flag registry (§4.10):** typed accessors, room → zone → world resolution,
      unknown keys preserved and wrong-typed values treated as absent
- [x] `GET /api/builder/room-flags`; the room editor renders flags from it, inherited values
      shown greyed with their source
- [x] Graceful degradation for every broken state in §7.4 — with tests for each row
- [x] Advisory `/validate`: dangling exits, orphan rooms, unreachable areas, unknown flag keys,
      rooms that are PvP only by inheritance
- [x] Builder UI: world tree, room editor (full), zone panel (flags only)
- [x] **World editor** (`WorldPanel.tsx`) — name, description, sort order, flags, difficulty, and
      delete. There was no world editor of any kind before.
- [x] **World and zone delete are reachable.** Both routes and both `builderApi` functions
      existed with no caller anywhere, so a mistyped world was permanent from the browser.
- [x] **The zone editor renders the flag registry**, the way the room editor does, so a newly
      registered flag appears with no client change — and carries name, description, and level
      range. It was two literal buttons (`pvp`, `peaceful`) and nothing else.
- [x] **Per-flag world and zone primitives** (`SetWorldFlag`, `SetZoneFlag`, both `PUT
      …/flags/{flag}`). All three scopes now edit one key at a time. They were whole-map writes,
      so two builders editing one zone's flags at the same moment overwrote each other — silently,
      since the losing request carried a complete, valid, merely stale map.
- [x] ASCII grid painter with legend management
- [x] Zone canvas: auto-layout from the exit graph, drag to arrange, drag to link
- [x] Live push of edits to players standing in an edited room
- [x] **Walk-and-build (§7.6):** `dig` endpoint covering both materialize and dig cases
- [x] Reciprocal exits, provisional key generation, automatic `editor_x/y` placement
- [x] `flags.unfinished` on new rooms, `/unfinished` to-do list, hatched on the canvas
- [x] In-game builder commands: `dig`, `link`, `unlink`, `rtitle`, `rflag`, `goto`
- [x] Follow mode: builder panel re-targets the room the character walks into
- [x] Room rename rewrites inbound exit references in the same mutation
- [x] Dig rate limiting; builder-only gating with player fallback to "The way is blocked."
- [x] 232 .NET tests + 18 client tests

Notes from the build:

- **The loop normalises requests into primitives, and persistence replays that exact list.**
  A builder asks for `dig north`; the loop answers with `[UpsertRoom, SetExit, SetExit]` and
  hands the same list to the writer. Letting both sides interpret the request independently
  would work right up until they disagreed — and `dig` is where they would, because the loop
  picks the new room's key and nothing else can know it.
- **Reads come from Postgres, writes go through the loop.** Enumerating `WorldState` from a
  request thread is a real race, not a theoretical one: it is mutated with no locks, and the
  moment it would bite is a builder editing a busy world. Since every mutation persists before
  its HTTP call returns, the database is never more than one in-flight edit behind.
- **A failed persist reloads the world rather than leaving memory ahead of it.** The alternative
  was a 200 for an edit that evaporates at the next restart. The reload is blunt — every room,
  not just the failed one — but a failure here means Postgres is unreachable, not that the edit
  was bad, so bluntness costs nothing and correctness is restored.
- **In-game builder commands persist fire-and-forget, and that asymmetry is forced.** A command
  handler runs *on* the loop; enqueueing a mutation and awaiting it would wait for a pulse that
  cannot start until the current one ends. So `dig` typed at the command line queues its writes
  like a character save, and a failure is logged rather than reported. The builder panel, where
  anyone authoring seriously works, does not have that gap.
- **`peaceful` beating `pvp`, and absence beating both, is the whole safety argument.** The
  test that matters is `A_room_with_no_flags_at_all_is_not_pvp` — every other claim in §4.11
  rests on it.
- **EF scaffolded the `flags` migration with a default of `""`.** Postgres rejects an empty
  string as jsonb, so the generated migration would have failed on any database that already
  had a world in it. Hand-corrected to `{}`, with a comment saying why.
- **Test hosts no longer use the Windows Event Log provider.** Each test builds its own host in
  one process, and `EventLogLoggerProvider` wraps a handle the first host to be disposed closes
  for all of them; a later host logging a warning then threw `ObjectDisposedException` from
  inside `ILogger.Log`. It surfaced as a failure in the readiness test — the one most reliably
  logging a warning — with a stack trace pointing at logging rather than anything that test did.
- **Both background workers now swallow `OperationCanceledException`.** An exception escaping a
  `BackgroundService` is treated as a fault: the host logs Critical and stops. During a shutdown
  already under way, that Critical reaches logging providers midway through disposal.

### Phase 2a — Role administration ✅ **complete**
*Done when: an admin can make somebody a builder from inside the game, and a ban takes effect on
someone who is already connected.*

Small, and it came before Phase 3 because Phase 2 shipped a builder that needed SQL to reach
(§7.7).

- [x] `admin_audit` table + migration
- [x] `PATCH /api/admin/accounts/{username}/role`, `GET /api/admin/accounts?q=`, Admin policy
- [x] `AccountAdminQueue` + worker, mirroring the world write queue
- [x] In-game `promote`, `demote`, `whois` — admin-only, hidden from `help`, target may be offline
- [x] `Notify(sessionId, …)` inbound message so an async result reaches the admin as a `sys` event
- [x] `SetActorRole` so a promoted character's verbs work without relogging
- [x] `OnValidatePrincipal` revalidation on a ~60 s interval: refresh a changed role, reject a
      banned account — **this is what makes banning a connected player work at all**
- [x] Self-demotion refused; an installation must never be able to lose its last admin
- [x] README: replace the `UPDATE accounts` instruction with the command
- [x] 278 .NET tests

Notes from the build:

- **`AccountRole` is not a ladder, and pretending it is would be a privilege bug.** The numbers
  ascend, so `actor >= required` reads as obviously correct — and quietly makes every Moderator a
  builder. `AccountRoleExtensions.Satisfies` is the single definition; the HTTP policies and the
  in-game command table both derive from it, and a test asserts the two agree for all sixteen
  pairs. Two copies of an access rule is how they end up disagreeing.
- **The revalidation interval had to become configuration to be testable.** At a fixed 60 s, a
  test that promoted somebody and immediately checked their access would assert the cache rather
  than the behaviour — and would keep passing if revalidation were deleted. `Auth:Revalidation`
  `IntervalSeconds` is 0 in tests, and 0 is a legitimate production setting for a small server
  that would rather pay a read per request.
- **Roles are parsed in full, never by prefix.** `promote kael a` silently meaning Admin is the
  one place where the abbreviation convenience the rest of the parser is built on becomes a
  hazard. Typing the whole word *is* the confirmation step.
- **`Notify` is addressed by session, not by character.** An account may have several tabs open
  and only one of them typed the command; the reply belongs to the connection that asked.
- **A demoted builder is told, not silently stripped.** Verbs that stop working with no
  explanation read as the game breaking rather than as a decision somebody made.

### Phase 3 — Objects, inhabitants, and multipliers ✅ **complete**
*Done when: a zone's difficulty is a slider, and the same kobold is trivial in one zone and lethal in another.*

The engine half was correct from the start; the **slider** was what was missing, which is the
half the phase goal is written about. `MultiplierEditor` and its live preview sit on both the
world and the zone panel now, and mob stats and loot are authorable alongside them.

One deferred nice-to-have remains below, and it is not what the phase goal asks for.

- [x] Item templates + instances (weight and capacity limits deferred)
- [x] `get`, `drop`, `give`, `inventory`, `examine`, `destroy`; `put` for containers deferred
- [x] Equipment slots: `wear`, `wield`, `remove` (containers with depth limit deferred)
- [x] Mob templates, spawners, population maintenance
- [x] **Population is counted per spawner across the world, and mobs stay in their home zone.**
      Both found in playtesting rather than by a test, and they were the same bug wearing two
      faces: the sweep counted mobs standing in the spawner's own rooms, so anything that wandered
      out was replaced — and nothing stopped it wandering out of the zone either. A spawner set to
      three could fill a zone, and a crypt rat could stroll into the starting meadow carrying
      crypt numbers. Now `WorldState` indexes mobs by the spawner that made them, and a wandering
      mob turns back at the border unless its template sets `roams`.
- [x] **Multiplier resolution at spawn time** (§4.4), `spawn_multipliers` recorded per instance
- [x] World + zone multiplier storage and `world × zone` composition **in the engine and schema**
- [x] Builder: mob template, item template, and spawner editors (CRUD endpoints)
- [x] Builder: mob **behavior** editor — disposition, idle emotes, shopkeeper and stock
- [x] Mob AI v1: idle emotes, room-to-room wandering, `sentinel` flag, fire-and-forget system tick
- [x] Ground items and mobs appear on the map with their icons
- [x] `emote` command for expressive actions
- [x] **Multipliers are authorable end to end.** The full slice landed: contract → primitive →
      applier → writer → `builderApi` → a Difficulty tab on both the world and zone editors.
      They were seed-and-SQL only, which made §7.5's "the reason the whole feature exists"
      the one dial nobody could turn.
- [x] **The multiplier preview panel exists.** *(Was checked off once while unbuilt.)* The
      endpoint had been correct since Phase 3 with no caller anywhere in `client/src`.
- [x] **Item spawners and multi-room spawners are authorable.** `SpawnerDialog` takes a kind
      toggle and both template lists, and a room multi-select; update sends `roomKeys`.
- [x] **Item template stats survive an edit.** `baseStats` is kept verbatim and only the five
      multiplier keys are written in place. Stats the form has no field for are listed read-only
      beneath it, so a builder can at least see the `"1d6"` that is there.
- [x] **Spawner removal is honest about scope.** The room list shows every spawner *containing*
      this room, and "Remove" deleted the whole thing while the confirmation said it would stop
      filling "this room" — tidying one room could empty twenty. It now removes this room from
      the set, and only deletes outright when this was the last room.
- [x] **The multiplier preview reported 40 health for everything.** `ResolveMobStats` read
      `BaseStats["health"]` as `value is int`, false for every jsonb-loaded template, so it fell
      through to its 40 fallback. A builder tuning the Strength dial was watching a number that
      was not their mob's. Now read through `JsonBag.Int32`.
- [x] **Loot table and `baseStats` editors for mob templates** (`MobStatsEditor`, `LootEditor`,
      both wired into `MobTemplateEditor`). Only `health` had a field, out of the ten keys the
      engine reads, so nine of a mob's stats were unauthorable from the browser. A test
      transcribes the engine's key list, so an editor that offers fewer fails.
- [x] **`Mob.State` reads through `JsonBag`.** It pattern-matched `is long` / `is true`, which is
      correct only while nothing reads or writes the `mobs` table. The schema and the
      `DbSet<Mob>` both exist, so the day mob persistence lands the sentinel flag and the emote
      timers would have gone quiet with no error — the same trap that had already cost three
      features. Disarmed before it fires rather than after.
- [ ] Builder: *Respawn zone* button to apply live multiplier edits to existing mobs (deferred nice-to-have)

### Phase 4 — Combat and progression ✅ **complete**
*Done when: you can kill something, loot its corpse, and level up — and the multipliers visibly matter.*

- [x] Combat state machine, since rebuilt onto per-combatant attack clocks (§2.3)
- [x] `kill`, `flee`, `consider`, auto-attack continuation
- [x] Damage model per §4.6, injectable RNG, full unit coverage of the formula
- [x] **Target validation gate (§4.11), separate from the damage formula:** `peaceful` forbids all
      combat, player-vs-player requires `pvp`, party members are never targets — the last of the
      three only stopped being a comment in 5.3, when there were parties to check against
- [x] PvP re-checked every round, so leaving a `pvp` room ends the fight; refusals are narrated
- [x] PvP kills recorded to the moderation log
- [x] **Death (§4.12):** no player corpse, no item loss, mob corpses unchanged
- [x] XP penalty as a fraction of the level band, floored at the threshold — never de-level;
      exempt below `Death:XpLossMinLevel` and on PvP deaths
- [x] `bind` in a `respawn`-flagged room; three-step respawn fall-through, stale bind cleared
- [x] Respawn at 25% Health / 0 Focus / 0 Stamina, out of combat; same path when link-dead
- [x] XP awards with zone `xp` multiplier, leveling, point spend
- [x] Regen tied to Vitality and rest state (`sleep`, `rest`, `stand`)
- [x] Aggressive mobs, assist behavior, target selection
- [x] `EquipmentResolver` wired into `CombatSystem` and the `stats` screen, so equipment
      multipliers reach the damage roll instead of decorating the character sheet
- [x] Mob damage: template stats win where declared, level-derived fallback where not
- [x] **Per-combatant attack timing.** Weapons carry an authored delay and verb; the off hand
      needs the `dual-wield` passive (Shade 3, Warden 5) to strike at all and `ambidextrous`
      (Shade 10, Warden 15) to strike on its own beat; mob templates carry an array of attacks,
      each on its own clock. A dead combatant no longer swings, and a cast in progress silences
      the swings that would otherwise interrupt it
- [x] **Builder-authored armour works.** `armorMultiplier` scales only the flat armour a piece
      already declares, and the item editor offered no way to declare any — so every authored
      piece resolved `0 × multiplier = 0`. The editor now writes `armorFlat`, `armorPercent`, and
      `defense` alongside the weapon stats, which is what the multiplier needed to act on.
      No armour *baseline* was added, and none should be: unarmed damage could take one (1–2)
      because the floor is a scratch, but a flat armour baseline of 10 would reduce nearly every
      hit in the game to 1. Unarmoured resolving to zero is the correct answer.

### Phase 5 — Depth

#### 5.1e — Ability progression ✅ **complete**
- [x] **8 abilities per Path unlocking to level 20**, never more than four levels apart. It used
      to stop at level 6 for every Path, so levelling past it granted nothing.
- [x] **One `AbilityCatalogue`** feeds both the seeder and `AbilityProgression`. They were two
      hand-written lists and had drifted apart in both directions: four abilities were unlocked
      at level 6 with **no ability row behind them**, so the level-up granted something
      uncastable; and `warden.battle-fury`, `adept.weaken`, and `shade.fortify` were seeded but
      in no progression, so all of 5.2a's buffs and debuffs were **unlearnable**.
- [x] Ability rows **reconcile on every startup**, not once at seed time, so an existing database
      receives abilities added later. Seeding bailed whenever a world already existed.
- [x] **Parry is a passive**, not a castable self-heal (Warden 4, Shade 8). It rolls after the
      attack roll and before narration, so it only spends itself on a blow that would have landed.
      Mobs never parry — the chance comes from Path and level, which a mob has neither of.
- [x] **Debuffs were inverted.** `debuff.weaken` sets `IncomingDamageMultiplier`, which scales
      damage the target *takes*; every weaken was authored below 1.0 and so made its target
      25–45% **harder** to kill. `DebuffEffect` now also reads `outgoingMultiplier` — hardcoded
      to 1.0 before, so the effect could not express "deals less damage" at all.
- [x] **Ability rows reconcile in all three directions** — added, updated, and *purged*. The
      catalogue is authoritative, which is safe because abilities are the one content type with
      no builder UI and no foreign keys pointing at them. Without the purge, a renamed ability
      (`warden.slash` → `warden.kick`) left the old row in the table forever.
- [x] **`damage.overtime`** — the fifth executor, and the first that differs in *kind*: damage on
      a clock of its own rather than a number scaled at the moment of a swing. Shade's Ambush is a
      stacking bleed, Adept's Scorch a heavier burn, Hallow's Wither the long slow one. The
      tick lives in `CombatSystem.Tick` where the death, XP, and loot paths already are, so a
      bleed can land the killing blow — the consequence being that wounds only work during a
      fight, and fleeing stops the bleeding.
- [x] **`control.stun`** — the sixth, and the first that takes a *turn* away rather than changing
      a number. `PreventsActing` is checked in three places, because each gate is independent: the
      combat loop that swings, the `cast` command that starts spells, and the mob AI that emotes,
      wanders, and aggresses. The interrupt is driven by `ShouldInterrupt` reading the *state*, so
      any stun breaks a cast however it arrived. Duration is clamped in the effect (24 pulses) so
      an authored typo cannot remove someone from the game, and a catalogue test fails the build
      rather than letting the clamp apply silently. New `warden.shield-bash` at 9 — `warden.kick`
      stays instant damage.
- [x] **`control.root`** — the seventh, and the counterpart to the stun: it leaves the turn and
      closes the exit. What it denies is `flee`; ordinary movement is already refused mid-fight,
      so a root that only blocked walking would do nothing in the one situation it is cast in.
      Clamped to 40 pulses. `shade.shadowstep` became `shade.hamstring`, which decides whether a
      fight ends rather than how fast — something no damage number expressed.

#### 5.1a–5.1d — Ability System ✅ **complete**
- [x] Ability system: cost, cooldown, cast time, targeting rules
- [x] `/cast <ability> [target]` command with validation
- [x] Per-Path ability trees with level-based unlocks
- [x] Extensible `IAbilityEffect` interface for effect resolution
- [x] `DamageEffect` and `HealEffect` with scaling and variance
- [x] Pulse-based cast resolution (250ms intervals)
- [x] Cast queue with interruption detection (movement, combat entry)
- [x] Cooldown tracking per (CharacterId, AbilityKey)
- [x] In-memory caching at game loop startup

#### 5.2a — Buffs and debuffs ✅ **complete**
- [x] `ActiveEffect` with duration, `Refresh`/`Stack`/`Ignore` stacking rules, and max stacks
- [x] `BuffEffect` / `DebuffEffect` behind `IBuffEffect`, resolved through `EffectRegistry`
- [x] Outgoing and incoming damage multipliers read by `CombatSystem` each round
- [x] `EffectExpirySystem` sweeps expired effects off the pulse; effects are in-memory only
      and reset on restart, matching cooldowns and combat state
- [x] `buffs` command lists what is active and when it ends

#### 5.2b — Quest engine ✅ **complete**
- [x] `quests` + `character_quests` tables, Active/Completed only, composite key
- [x] String keys for mobs and items rather than FKs, so a quest can be authored before its
      content exists (§7.4)
- [x] `talk <npc>`: offers when prerequisites are met, state-dependent dialogue for
      offer / in-progress / complete / turn-in
- [x] `give` hook: strict match on item and count, otherwise the NPC refuses and keeps nothing
- [x] Prerequisite chains, enforced on offer — the mechanism storylines are built from
- [x] Repeatable quests with a `TimesCompleted` counter
- [x] `quests` journal and `quest <name>` detail
- [x] `CharacterQuestSaveQueue` so progress survives restart
- [x] Builder API: quest CRUD, reachability (`GET /quests/{key}/reachability`), and the
      storyline graph with cycle detection (`GET /zones/{zoneKey}/storyline`)
- [x] **Builder UI: a Quests tab** (`QuestsTab`, `QuestEditor`, `QuestCreateDialog`,
      `StorylinePanel`, `ReachabilityPanel`). This line was previously checked off naming a
      `QuestEditor.tsx` that was never written, while `builderApi`'s five quest functions had zero
      callers — the §12 lesson repeating, and the worst version of it, because the plan claimed a
      whole authoring surface that did not exist. Three columns rather than the templates' two:
      a quest is not isolated the way a template is, and a chain is invisible from inside any
      quest in it.
      Every mob and item reference is picked from a real template, because a typo produces a
      *dormant* quest — one that reads perfectly in the journal, is offered by nobody, and reports
      no error anywhere. Prerequisites stay free text, since a chain is routinely authored
      backwards (§7.4) and the storyline panel is what reports a key that never turns up.
      Reachability warnings render in the editor, where §10 says they have to be: an unobtainable
      required item fails silently in play, so the quest reads correctly and the player just
      wanders.
      Two things the API shape forced, both worth knowing: **create needs a zone, giver, and
      turn-in**, so quests get their own create dialog rather than the shared key-and-name one;
      and **a quest key is one segment**, not the dotted composite a room uses.

#### 5.2c — Shops and currency ✅ **complete**
- [x] `buy` / `sell` / `list` against a mob flagged `shopkeeper`
- [x] Priced through `itemValue`; sellback at a configurable percentage
- [x] Gold on the character, persisted through `CharacterSaveQueue`
- [x] Shopkeepers and their stock are authorable in the builder (`MobBehaviorEditor.tsx`), with
      the stock picked from real item templates rather than typed as keys
- [x] Shop coverage: `ShopCommandTests` (engine) exercises list / buy / sell against behavior in
      the shape storage returns it

#### 5.2d — Gaps in the above, found by audit ✅ **complete**
These were the parts of 5.2 that had been checked off in spirit but were not in the code. Every
one failed quietly, which is why they needed listing rather than assuming — and is the reason
§12's rule now reads "before checking a box, name the test or the call site."

- [x] **The behavior bag was unreadable.** `Behavior` is `Dictionary<string, object>` stored as
      jsonb, so every value arrives as a `JsonElement`. `ShopCommands` tested `is bool` and
      `is List<object>`, and `MobAiSystem` tested `is List<object>` — all false for any template
      that had round-tripped, which is every template outside a unit test. **Shopkeeper
      detection, shop stock, and idle emotes were dead in the running game**; only aggression
      survived, by luck. Now read through `JsonBag` / `MobBehavior`, the counterpart to
      `StatReader` for the non-numeric bags.
- [x] **`buy` could never complete.** It looked its zone up with `RoomKey.Zone` (the bare
      segment) where `FindZone` wants the qualified `world.zone`, so every purchase fell through
      to "the shop is temporarily unavailable".
- [x] **Quest rewards ignore zone multipliers.** Now resolved through `Multipliers.Resolve` the
      way combat resolves `mob.ResolvedXp`. Two adjacent bugs fixed with it: the reward-item
      `Reply` sat inside the spawn loop (a count of 3 announced itself three times), and a
      missing template or zone dropped the reward in silence.
- [x] **`/reachability` cannot fail.** Rewritten to walk loot tables, item spawners, and other
      quests' rewards, and to check giver/turn-in mobs exist and are spawned.
- [x] **Non-combatant mobs.** `type: "npc"` mobs can neither attack nor be attacked, so a quest
      giver or shopkeeper cannot be killed into a soft-lock (§7.4). Authorable in the builder.
- [x] **`questItem` is written as well as read.** A toggle on the item template editor sets
      `IsQuestItem`, and `ItemSpawner` stamps the flag onto every instance — the one path shop
      stock, quest rewards, the spawner sweep, and combat loot all pass through. Both readers had
      been correct and both dead, so neither rule had ever fired in play. Two call sites that
      hand-built an `ItemInstance` from a spawned one now use the spawned instance directly;
      they would otherwise have dropped the stamp.
- [x] **Dormant quests.** A quest whose giver, turn-in, or required item no longer exists stops
      being offered, and an in-progress copy stays in the journal marked *unavailable*. Dormancy
      is **derived, not stored** — no `QuestStatus.Unavailable`, no migration, and restoring the
      content revives the quest with no repair pass. No player row is ever written.
- [x] **`cast` could not target a mob.** Target resolution searched players only, so every
      offensive ability resolved to no target against the things you actually fight — and because
      cost and cooldown are spent *before* the target is resolved, it charged in full, started the
      cooldown, and narrated "takes effect!". Now matches mobs through `NameMatch`, falls back to
      the current combat target for harmful abilities and to self for helpful ones, and refuses
      before charging when nothing matches.
- [x] **Every ability was on cooldown at boot.** `GetAbilityCooldown` returned `0` for "never
      cast", which is a real pulse — so for the first `CooldownPulses` of server uptime the whole
      spellbook was refused. Returns `long?` now.
- [x] **The Hallow could not heal anyone.** Every supportive ability on the support Path was
      `TargetingType.Self`. They are `SingleTarget` now, and a helpful ability cast with no target
      named still lands on the caster.
- [x] **`TargetingType.Aoe` resolves.** `AbilitySystem` gathers a target *list*, filtered per
      target as §4.11 requires: a harmful cast takes every mob that may be fought — skipping
      non-combatants — plus other players only where the room resolves `pvp`, and nothing at all
      in a `peaceful` room; a helpful one takes the caster and the people standing with them and
      leaves the mobs alone. One cost and one cooldown however many it lands on, and a cast that
      gathers nobody is refused before either is spent. Two abilities declare it: `adept.firestorm`
      (18) and `hallow.benediction` (18).
      **Party membership now overrides both flags** (5.3): a harmful area effect skips the
      caster's group even where `pvp` is set — the one room it mattered in — and a helpful one
      prefers the group over the room, falling back to the room when the caster is ungrouped.
- [x] **Which way an ability points is declared by its executor**, `IAbilityEffect.IsHarmful`,
      rather than by a hardcoded list of two effect keys in the command layer. That list predated
      five of the seven executors and classified all of them as helpful, so `cast scorch` with no
      target named set the caster on fire. It also decides which set an area effect gathers.

- [x] **The tick systems no longer query the database once per entity per tick.** `MobAiSystem`
      called `GetByKeyAsync` per mob every 16 pulses (4 s) and `SpawnerSystem` did the same per
      spawner per kind every 60 (15 s), each opening a fresh `DbContext` for a single-row
      `AsNoTracking` read that nothing memoized — a world with twenty mobs made twenty round-trips
      every four seconds with nobody logged in. Both now read `MobTemplateCache` /
      `ItemTemplateCache`, which already held every template, load at boot, and are kept live by
      the applier; only the command paths and the Phase 4 systems read them, because these two
      systems predate them.
      **The fallback is gated on `IsLoaded`, not on a miss** — a deliberate departure from the
      note this item started as. A per-miss fallback looks safer and behaves worse: a mob or
      spawner whose template a builder deleted misses forever, so it would reissue the doomed
      query on every sweep for the life of the process, which is the pathology being removed in
      the one case where it can never pay off. An unloaded cache is a different failure, and
      reading through to the repository for it is what stops a host that never called `LoadAsync`
      from silently having no mob AI at all.
      Behaviour is identical either way, which is why nothing surfaced this until the SQL log was
      read — so the tests count repository calls rather than assert on outcomes.

- [x] **The spawner rules themselves are cached too.** The item above moved the *templates* out of
      the database and left the sweep still opening a `DbContext` for
      `SELECT … FROM spawners` every 15 seconds — the last per-tick query in the engine, and the
      one still visible in the SQL log afterwards. `SpawnerCache` is the same shape as the template
      caches, keyed by `Guid` rather than by key, loaded at boot and kept live by the applier:
      `ApplyUpsertSpawner` / `ApplyDeleteSpawner` had been no-ops that forwarded the change to
      persistence and nothing else, so this is also what makes a spawner saved in the builder take
      effect on the next sweep instead of at the next restart.
      **Gated on `IsLoaded` for the same reason, but the miss question does not arise** — the sweep
      wants the whole set, so there is no key to miss on and the only read-through case is a host
      that never loaded the cache.
      The sweep **copies** the values rather than enumerating them live. It is fire-and-forget, so
      a template read that *does* fall through to the database parks the rest of the loop on the
      thread pool, and a builder saving a spawner on the loop thread meanwhile would invalidate the
      enumerator underneath it. One list of a few dozen references every 15 seconds is not the cost
      being avoided here.
      Unchanged: deleting a spawner stops it being enforced but does not despawn what it already
      placed.

#### 5.2f — Threat, and the gate every hostile action goes through

- [x] **Every kind of damage counts on the hate list.** `GetTopHater` reads cumulative damage, so
      the list was always a damage meter that reorders itself — but only landed melee swings fed
      it. Ability damage went from the executor straight into `Vitals.Health`, and damage-over-time
      ticks straight in from the combat loop. That inverted the design: **the Adept, the Path built
      to deal the most damage in the game, was the only one that could never pull a mob off
      anybody**, and the Shade's Ambush was worth no threat at all. Damage is measured as the
      target's health before and after, since the executors return `void`; a tick credits
      `ActiveEffect.SourceEntityId`, because the caster may have left and the bleed keeps working.
- [x] **Hurting a mob with an ability starts the fight**, the way swinging at it does. An opening
      Bolt used to be free — the mob took the damage and stood there, nothing having put it in
      combat. Engages without retargeting, so an area effect cannot swing the caster's own weapon
      round to the last thing the flames touched.
- [x] **`taunt` (Warden 8) and `provoke` (Shade 12)** — the first thing in the game that *writes*
      threat instead of earning it. Sets the caster above the current top by a fraction of the
      target's max health (0.30 / 0.18), which is a **lead, not a lock**: the list is still a
      damage meter afterwards, so whoever was displaced climbs back by out-damaging the taunter.
      Expressed as a fraction because threat grows without bound over a fight — a flat lead would
      be decisive in the first ten seconds and beneath notice five minutes in, and would mean
      nothing consistent between a rat and a dragon.
- [x] **One §4.11 gate for every hostile action** (`HostileActionGate`). `kill` refused a
      `peaceful` room, a non-combatant, and an unsanctioned duel; **`cast` checked none of the
      three**, so a Bolt worked in a safe room and an Adept could kill the shopkeeper handing out
      the zone's quests. Area effects had grown a third copy of the same rules. All three answer
      through one place now — which is what makes taunt safe, since it is a way to start a fight
      with something you never attacked.
- [x] **A malformed entity ID can no longer kill the game loop.** One in a hate list became the
      top hater and threw on the next tick when nothing could resolve its room. Effect sources
      survive a jsonb round trip and outlive the cast that set them, so `EntityId.IsWellFormed`
      guards the door. Found by a test, not in production.
- [x] **You can name yourself as a target.** The player search is `OthersIn`, which excludes the
      caster, so a Hallow typing their own name at their own heal — the obvious thing to type —
      got "You don't see 'Bram' here." while standing there.

#### 5.2e — Deferred from the original 5.2 list
- [→] `noRecall` refuses teleports out — **moved to 5.3**, where the `recall` command it is
      waiting for now sits. The flag is registered with no reader, which is not a bug on its own:
      §4.10 says a flag with no reader is dead weight, and this one is dead weight *until the
      verb exists*. Listing it apart from that verb was what made it look like an oversight.
- [x] Path respec — **decided against** (Q3, §4.5). Paths are fixed at creation; rerolling is the
      respec. Nothing to build, which is the point: this line closes rather than defers.
- [→] Pets and charmed mobs inheriting their owner's §4.11 permissions — **moved to §13**. There
      is no pet system and none is planned for a numbered phase, so this had been sitting in a
      phase checklist as work that could never start.

#### 5.3 — Communication and travel
- [x] **Parties, session-scoped.** A `PartyRegistry` in `WorldState`, beside combat and active
      effects, with no table behind it: a party describes who is in the world right now, and
      persisting one would raise a question — what a party whose members are all offline means —
      that nothing in §4.11 or the split needs answered. `WorldState.Remove` is the one door out
      of the world, so party cleanup lives there rather than at the four call sites that reach it.
      Going link-dead deliberately does *not* clean up: §3.6 leaves that character standing in the
      room, and a group that dissolved over ten seconds of bad wifi would be worse than one that
      waited.
      `group` with subcommands (invite, accept, decline, leave, kick, disband), six members,
      invitations that expire after a minute, and leadership that passes when the leader walks
      out. A party of one dissolves itself — otherwise it is a state you cannot tell from being
      grouped, and it silently changes how a helpful area effect gathers.
- [x] **The three approximations waiting on parties are gone**, each of which carried a comment
      saying so. `AreaTargets` filtered by the `pvp` flag as a stand-in for membership;
      `TargetValidator`'s summary claimed *"party members are never valid targets"* with nothing
      enforcing it; and the XP split had nothing to split between.
- [x] **Party XP and gold split**, evenly, remainder to whoever landed the blow, among members
      **standing in the room where it died**. A member two zones away shares nothing — a group
      that could farm by scattering would make the split an exploit rather than a convenience.
      **No group bonus**: four people killing one mob earn exactly what one earns, so grouping is
      a social choice rather than an efficient one. That may be the wrong call, but it is one
      number in `RewardShare` and inventing a multiplier before anyone has played in a group would
      be balancing against a guess.
- [x] **`tell`, `reply`, a world channel, and a party channel.** `tell` and `gtell` cross rooms,
      which is what makes them different from `say`. `chat off` silences the world channel in
      *both* directions — a channel you can shout into while ignoring the replies is not one
      anybody else wants to share. The short forms were chosen so no older verb lost its
      abbreviation: `t` is still talk, `r` still rest, `c` still consider, `g` still get.
- [x] **`AbilityValidator` deleted.** A third copy of the §4.11 rules with no production callers,
      which applied the hostile check to *every* single-target ability — so anyone who wired it up
      would have found that healing another player was refused everywhere except a `pvp` room. Its
      cost check, targeting check, and self-target rule are all enforced on the live path already.
- [x] **A second world needs no portal concept — an ordinary exit already crosses worlds.**
      This line used to read *"a second world reachable by portal"*, which invented a mechanism
      for something the exit system does already. `RoomExit.ToRoomKey` is a fully-qualified
      `world.zone.room`; `ApplyLink` compares nothing but whether the source room exists;
      movement resolves the key and moves; `/validate` looks its targets up globally, so a
      cross-world exit is not reported as dangling; the zone canvas skips edges to rooms it does
      not hold, so the layout degrades rather than breaking; and Add Exit already takes a free
      `world.zone.room`.
      So this is **content, not code** — build the world, link a room to it. Whether the door is
      described as a portal, a ship, or a staircase is prose.
- [x] **`recall`, and the `noRecall` flag finally has one reader.** `Travel.Refuse` is that
      reader, and it is the seam any future teleport goes through — a flag becomes a lie when a
      second travel verb lands later and forgets to ask.
      Recall returns you to the bind point `bind` already sets and death already uses, rather than
      a second configured room that could drift from it; which also means `bind` now matters while
      you are alive. It is free and uncooled, which sounds generous until you notice what it is:
      dying on purpose without the XP loss. What stops it being an escape hatch is that it refuses
      mid-fight, so the fight you would want out of is the one it will not take you out of.
      Deliberately not used by the builder's `goto`, which is documented as ignoring exits — a
      builder held in place by the content they are editing would have to walk out to fix it.
- [→] Teleport *as a spell* — not built, and not a gap. A destination is a `world.zone.room`
      like any other, so a teleport effect is a parameter, not a new kind of link: nothing about
      travelling needs to know whether the target is in this world. When one is wanted it is an
      executor reading a room key and calling `Travel`, which is why that seam exists now rather
      than being invented alongside the spell.

### Phase 6 — Operations

Partly done ahead of schedule — the deployment pipeline landed alongside Phase 5.

- [ ] Admin commands: `teleport`, `stat`, `set`, `mute`, `kick`, `ban`
      (`goto` shipped with Phase 2; `promote` / `demote` / `whois` with Phase 2a)
- [ ] Rate limiting per session; command flood protection; builder API throttling
      (only `DigThrottle` exists, covering the `dig` endpoint alone)
- [ ] OpenTelemetry: pulse duration p50/p99, sessions, commands/s, queue depths
      — nothing is instrumented; the slow-pulse watchdog logs but does not measure
- [ ] Scheduled `pg_dump` backups + a rehearsed restore drill
- [ ] World export/import (JSON) for moving content between environments
- [ ] Deployment pipeline:
      - [x] Dockerfile (multi-stage: publish layer, runtime layer, `dumb-init` entrypoint)
      - [x] Migration strategy settled: applied at startup rather than by an init container,
            because a single-writer loop with no backplane cannot run two instances (§6.1)
      - [x] Health checks gate readiness; `/health/ready` includes database check
      - [x] Reverse proxy with SSE buffering off — `proxy_buffering off` in `client/nginx.conf`,
            `X-Accel-Buffering: no` set on the stream response
      - [x] Deployment automation: `docker-compose.prod.yml`, per-environment deploy configs,
            and a GitHub Actions image build/push to ghcr.io
      - [ ] Runbook: rollback procedure if migration fails, monitoring dashboard, incident response
            — `DOCKER.md` and `DEPLOY_NO_ENV.md` cover setup, not recovery

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
| Room flags | Resolution is room → zone → world → default; an unknown key survives a save/load round-trip; a wrong-typed value resolves to the default; `peaceful` beats `pvp`. The load-bearing test is that **a room with no flags at all is not PvP** — that is the property every other safety claim rests on. |
| PvP | Refused in an unflagged room, allowed in a flagged one, ends the round after either combatant leaves, and never targets a party member — including a duel already under way, which ends the round the two players group up. AoE in a mixed room hits mobs and skips players. Every hostile action — swing, single-target cast, area effect, taunt — answers through the one gate, so the coverage is that they agree rather than that each was remembered. *Pet laundering is not tested because there are no pets; it is §13's blocking constraint, not a gap here.* |
| Death | XP loss floors at the level threshold and never de-levels; no loss below the min level or on a PvP death; dying at the exact threshold costs nothing. Respawn falls through all three candidates, including when the bind room was deleted mid-session. Nothing leaves the inventory. |
| Quests | The full loop: talk → drop → give → rewards. Plus the refusals — wrong NPC, no active quest, wrong item, insufficient count — each leaves the item in the player's inventory. Chains unlock in order and cannot be short-circuited by pre-holding the item. Deleting a referenced mob leaves an Active quest in the journal rather than wiping it. |
| Spawners | A mob that wandered out still counts; ten sweeps that scatter what they made never exceed the target; a kill is replaced; two spawners of the same template do not count each other's work; a mob a builder placed by hand satisfies nobody's target. |
| Wandering | Turns back at a zone border, crosses it with `roams`, still moves freely inside its own zone, and a mob with no recorded home zone is confined rather than freed — absence resolves to the restrictive value, as it does for room flags. |
| Parties | Forming, expiry, leadership passing, and the dissolve at one member. Leaving the world drops you from the group — asserted through `WorldState.Remove`, which is the one door out. The split pays only members standing where the mob died, and an odd remainder goes to whoever landed the blow rather than evaporating. |
| Travel | `recall` reaches the bind point and falls through to the starting room when unbound or when a builder deleted it. Every refusal has a test, because the value of `noRecall` having exactly one reader is entirely in that reader being consulted. |
| Builder | Mutation → loop → persist → occupants notified, end to end. Audit row written on every write. |
| Roles | Promotion reaches an open session without a relog, and demotion revokes builder access within the revalidation interval rather than at cookie expiry. A banned account is rejected while still connected. Self-demotion refused. An offline target can be promoted. Every change writes an `admin_audit` row. |
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
| **A stray `pvp` flag turns a town into a killing field, live, with no rollback** | The flag defaults to off and absence is always safe (§4.10); zone- and world-level sets are warned about by `/validate` with the affected rooms listed; `peaceful` overrides; every flag change writes a `content_audit` row, so *"who made the square PvP"* is one query |
| **Flags accrete until nobody knows what a room does** | The registry is the single source of truth and carries a summary per flag; the builder renders from it, so UI and behavior cannot drift. A flag with no reader is dead weight and should be deleted — the registry makes that auditable |
| **The XP penalty feels punishing enough to drive players off** | It is bounded by construction — one fraction of a single level band, never a de-level, nothing below level 5, nothing on PvP — and all four knobs are configuration, so it can be tuned without a deploy |
| **A role or ban change does nothing until the cookie expires** | The claim is written at sign-in, so a demoted builder keeps access for up to 14 days and a ban on a connected player does nothing at all. `OnValidatePrincipal` revalidation on a ~60 s interval (§7.7). This is the security-relevant half of Phase 2a and the reason it is not deferred to Phase 6 |
| **An installation loses its last admin and cannot get it back** | Self-demotion is refused outright. There is no recovery from zero admins except the SQL that §7.7 exists to eliminate |
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

**Phase 5 is closed, 5.3 included.** Multipliers, world and zone editors, spawners, `questItem`,
`baseStats`, ability progression, dormant quests, the cast targeting bugs, seven effect executors,
all three targeting modes, threat accounting, the quest builder, mob attack effects, parties, the
XP split, `tell`/`reply`/channels, and `recall` have landed. **Every content type §4 describes can
now be authored in the browser with no SQL**, the effect vocabulary runs in both directions, and
nothing in §4.11 is approximated any more.

**Next: Phase 6 (ops).** The deployment pipeline landed early; what is left is the part that
matters when something goes wrong.

1. **Admin commands** — `teleport`, `stat`, `set`, `mute`, `kick`, `ban`. `Travel` is already the
   seam an admin `teleport` moves through, and `goto` deliberately bypasses it.
2. **Rate limiting and flood protection.** Only `DigThrottle` exists, covering one endpoint.
3. **Telemetry.** Nothing is instrumented; the slow-pulse watchdog logs but does not measure, so
   the §11 targets cannot currently be checked against reality.
4. **Backups and the recovery runbook** — `DOCKER.md` and `DEPLOY_NO_ENV.md` cover setup, not
   rollback or incident response.

Also logged from playtesting and not yet in a phase: an admin-triggered shutdown with a warning
and a delay, delay 0 meaning immediate.

The items this section carried last, kept as the record of what they were and why the shape
changed:

1. **Parties.** Three approximations were waiting on them, each carrying a comment saying so:
   `AreaTargets` leaned on the `pvp` flag as a stand-in for membership, `TargetValidator`'s summary
   listed *"party members are never valid targets"* as a rule nothing enforced, and the XP split
   had nothing to split between. All three are now real. Session-scoped was the call — no schema,
   no save queue, and no question about what a party of offline characters means.
   The one thing the build surfaced that the plan had not: **`AbilityValidator` was a fourth copy
   of §4.11 with no callers**, and it applied the hostile check to helpful abilities, so wiring it
   up would have made healing another player impossible outside a `pvp` room. Deleted rather than
   corrected — the live path already enforces every rule it held.

2. **Mob attacks carry effects.** ~~Every one of the seven executors is a player-only tool~~ — the
   asymmetry was live rather than theoretical: a Warden's Shield Bash (9) takes a boss off its
   feet for three seconds and the boss had no answer of any kind.
   This used to read *"mobs cannot cast"*, which overstates it and invites a build big enough to
   defer forever. **The receiving side is already finished**: `ActiveEffect`, `world.ApplyEffect`,
   `IsStunned` gating both the cast command and the combat loop, `PreventsEscape` gating `flee`.
   A player can already receive all seven. What is missing is a way for a mob to *emit* one.
   So the shape is an optional `effectKey` and params on `MobAttack`, applied where its damage
   already lands — no `CastJob`, no cast bar, no mob focus pool, no target-selection changes, and
   no migration, since `attacks` is already `jsonb`. It reuses the effect registry and the
   per-attack timers that already let a wolf bite every second and rake every three.
   *A departure from the traditional mob spellcaster, deliberately: a mob's abilities are things
   it does with its attacks rather than a spellbook it works through.*
   Shipped as `MobAttack.EffectKey` + `EffectParams`, applied in `CombatSystem.ApplyRider` where
   the swing's damage already lands — so the effect inherits the miss chance, the parry, and the
   death check for free, and a stun you dodged does not stun you. It lands only on a survivor.
   An unknown key is ignored at runtime (absence is the safe value, as with flags) and **refused**
   at the builder API, where a typo is still cheap to fix. The editor offers only harmful effects:
   a rider hits whoever the attack hit, so a helpful one would mean a mob mending the player it
   just struck.

**Not yet in any phase, from playtesting:**

- An admin-triggered shutdown with a warning and a delay, a delay of zero meaning now. Probably
  belongs beside the other admin commands in Phase 6, with the confirmation in the builder.
- `examine` and `stats` are builder-aware; the other inspection commands (`look`, `inventory`,
  `consider`) are not.
- Autocomplete and alias lists moved to §13 — both want a protocol or schema addition rather than
  a command tweak, so neither is the small fix its one-line description suggests.

**Two lessons from the audits, worth carrying forward:**

- **A free-form jsonb bag must be read through a reader that accepts the shape storage returns.**
  `Behavior` and item `State` come back as `JsonElement`; code that pattern-matched the C# shape
  was false for every value that had round-tripped, which silently killed three features at once.
  `JsonBag`, `ItemState`, and `StatReader` exist for this. A test that hand-builds the bag in C#
  proves nothing about the running game — author it through `WorldHarness.AsPersisted`.
- **An endpoint with no caller is not a feature, and a checkbox is not evidence.** Reachability,
  the multiplier preview, world delete, and `/respawn` were all written, checked off, and never
  wired to anything. Before checking a box, name the test or the call site. The quest editor was
  this same lesson caught a second time, and the worst version of it: 5.2b checked off a
  `QuestEditor.tsx` that was never written, so the plan claimed a whole authoring surface that
  did not exist for as long as nobody looked. **Naming a file is not evidence either — open it.**
  It is built now, and its smoke test asserts the API is *called*, because that is the property
  that was missing rather than the code.

**No open questions remain.** Q3 (Path respec) is decided: Paths are fixed at creation (§4.5).

---

## 13. Future enhancements

Not in any phase, and deliberately so. Each of these is a real idea with no committed slot —
listed here rather than in a phase checklist, because an unstartable item inside a phase reads as
work that is merely late.

### Pets and charmed mobs

**Status: not planned. Nothing in the codebase references a pet, and nothing should until this
section becomes a phase.**

§4.11 already states the rule a pet system would have to honour: *a pet is an extension of the
player who commands it, and routing an attack through one must not launder it past the check.*
That sentence has been in the document since PvP was designed, and it is the reason this is
written down at all rather than dropped.

**The blocking constraint, for whoever builds it.** Every hostile action now goes through one
gate, `HostileActionGate`, which asks *"may this player do this to that target"* and answers with
a refusal or null. A pet attacking on its owner's behalf is a **mob** acting, so it does not pass
through that gate at all — `RefuseMob` and `RefusePlayer` both take `CombatantType.Player` as the
attacker, because today every hostile action has a player behind it.

That is the laundering hole in exactly the form §4.11 warns about: order a pet to kill someone in
a non-PvP room and no check has an opinion. So the day pets exist, the gate has to learn a third
question — *who is this mob acting for* — and every call site has to route through it. Doing that
first, before any pet can be commanded, is the difference between the rule holding and the rule
being a paragraph.

Until then this is blocked, not deferred: **no partial pet support should land**, because a pet
that can attack before the gate understands ownership is a PvP bypass shipped by accident.

### Command-line autocomplete

Item and mob names on the command line, and `spawn` completing against real keys. Wants a
protocol addition — the client cannot complete what it has never been sent.

### Per-template alias lists

Matching is derived from the name, which covers the common case. Nothing reaches a named wolf
called "Fang" whose key is `grey-wolf`. An explicit alias list on the template is the fix; the
matcher (`NameMatch`) already ranks candidates, so it is a new source of candidates rather than a
new algorithm.
