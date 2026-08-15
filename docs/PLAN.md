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

Status: **Phases 0–6 complete. Remaining: Phase 7, the mobile client.**

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

*Operating* it is covered: admin and moderation commands, three rate-limit policies, world
export/import, a Prometheus dashboard against the §11 targets, nightly backups that are only kept
once they have been restored, and a recovery runbook whose every procedure has been run
([RUNBOOK.md](RUNBOOK.md)).

Next: **Phase 7, the mobile client** ([MOBILE.md](MOBILE.md)).

**This document is the design and the open work. [HISTORY.md](HISTORY.md) is the record of what is
finished** — the phase checklists through 5, the notes from each build, and the postmortems. They
were moved out on 2026-08-13, when §8 alone had grown longer than the design it was meant to
deliver. Section numbers were left exactly as they were, because source comments cite them by
number in some two hundred files.

One thing carried over from those checkboxes, because the pattern outlives them: several were
ticked for work that was designed but not built, or built but dead on arrival. **A box means
"someone believed this was done", and only a test or a read of the code means it is** (§12).

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
├─ README.md                        # the only document in the root
├─ LICENSE
├─ docs/                            # this file, and everything else written down
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
| `abilities` | the character's whole ability list, each with cost, the verb to type, and what is left of its cooldown | Sent on entry and on level-up. Carrying the remainder is what resynchronises a reconnect, which has missed every `cooldown` event it was away for |
| `cooldown` | one ability's key and its cooldown in pulses | Sent once when a cast lands, not per pulse. The client counts down from it — a frame per pulse per cooling ability is four a second per player, which is the traffic §11 is careful about. The client draws only what is cooling, using the roster's `cooldownPulses` as the denominator for the fill |
| `sys` | connection notices, link-dead warnings, forced logout | |

A builder edit to an occupied room pushes fresh `room` / `map` events to everyone standing in
it, so live edits are visible without relogging.

### 3.6 Disconnect handling

If the SSE stream drops, the character does **not** vanish. It is marked link-dead and stays in
the world for 90 s (and can still be attacked — classic MUD risk). A reconnect inside that
window rebinds the session and replays the ring buffer. After 90 s the character is saved and
removed. A `sys` event warns the player as the window runs out.

**One character, one live stream — the newest one.** A session's event channel is `SingleReader`,
so two SSE responses draining it do not each get a copy of every event; they get roughly half
each. That is not a race to be lost occasionally, it is what a channel read twice does, and it is
what a player signing in on a second device reported: a fight legible on neither screen.

- **The newest connection wins**, rather than the second being refused. Picking up a phone while
  the desktop is still logged in is a thing people do on purpose, and a browser told *no* just
  retries.
- **Ownership is per character, not per session**, and this is the correction that made the rest
  of it work. Entering builds a *new* `GameSession`, so ownership recorded on a session was thrown
  away at exactly the moment a second device arrived: the older device's retry met a session that
  had never heard of it, was served, and took the character back. `StreamOwnership` lives in the
  registry, keyed by character, and outlives every session that character has.
- **The hand-over happens at `enter`, not when the stream notices.** Entering is the act that says
  which device is playing this character; the stream only carries the decision out. Left to the
  stream, the older device saw its channel go quiet, could not tell that from a dropped network,
  and reconnected — which is the tug-of-war, and it is what the first attempt at this shipped.
- **The displaced screen is told, in words**, with a `sys` event of kind `displaced`, and the
  client closes the stream on it rather than reconnecting. Retrying is the one reaction that must
  not happen: two devices that both keep reconnecting take the stream in turns for ever.
- **The older screen goes back to character select**, and is not offered the character back. Two
  screens that can each reclaim it is the same tug-of-war in a politer costume — the loser
  reconnects, wins, and the other becomes the loser. One of them has to be finished, and it is the
  older one, because the player is at the device they just picked up. It does **not** call
  `leave`: the character is in the world being played, and leaving would pull it out from under
  the device that now has it.
- **Being displaced is not going link-dead.** No `LeaveWorld` is submitted, because the character
  is standing there being played on another screen — and narrating *"goes still, eyes unfocused"*
  to the room about somebody who is very much present is exactly the confusion this started as.
- **The client names its connection** with an id minted per stream, sent as `?connection=`. Two
  devices playing one character send byte-identical requests, so nothing else tells the device
  that was replaced from the device that replaced it. It works because `EventSource` retries the
  *same URL*: a dropped connection returns under the id it already had and is recognised as a
  screen resuming, while a takeover is a new `EventSource` with a new id. It is not a credential —
  the cookie still authorises and ownership is rechecked per request (§3.2). A client that sends
  none is served; it loses only the ability to be told apart from its successor.
Getting the character back is choosing it again from the character list, which is one click and is
also the honest description of what it does.

**A stream must not be reopened by a re-render**, which is a client rule but belongs here because
of what it costs on the server: tearing a stream down reports a dropped connection, and that marks
the character link-dead and completes its event channel — so the stream that replaced it reads a
closed one and the screen is stuck reconnecting for good. This cost a player the character they had
just taken over. `GameScreen` holds its callbacks in refs rather than depending on their identity;
a ref holds for every caller, where a `useCallback` at the call site holds only for callers who
remember.

*A separate thing this surfaced, unchanged and worth knowing: going link-dead completes the
session's channel, so `EventSource`'s own retry reconnects to a closed one and stays silent. Only
entering again re-establishes output. The automatic retry therefore recovers a stream but not a
link-dead character.*

**Getting back in is the character list, and only the character list.** There was a *Rejoin the
world* button on the disconnected bar as a second route to the same `enter` call. It is gone.
Choosing the character again does exactly what it did, in one click, and it is already the way back
from being displaced by another device — so the button was a second implementation of a path that
existed anyway. It is also the one that had quietly broken: it called a state setter that a
refactor had moved to `App`, so pressing it threw rather than rejoining, and nothing noticed because
the client typecheck was being run in a mode that checks nothing. The bar remains, and now only says
what is happening.

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
| `strength` | Monster health **and** damage together | The master difficulty dial. Also moves the mob's *level* — see below |
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

Worked example — one **level 8** `kobold-sentry` template with base 40 hp, 4–7 damage, 120 xp,
25 gold:

| Zone | `strength` | `gold` | `xp` | Result | Fights at |
|---|---|---|---|---|---|
| `millbrook` | 1.0 | 1.0 | 1.0 | 40 hp, 4–7 dmg, 120 xp, 25 gold | level 8 |
| `sunken-crypt` | 2.5 | 3.0 | 1.0 | 100 hp, 10–18 dmg, 120 xp, 75 gold | level 20 |
| `the-deep` | 6.0 | 8.0 | 2.0 | 240 hp, 24–42 dmg, 240 xp, 200 gold | level 48 |

**The last column is a resolved value like the others** (`MobLevel.Effective`, §4.7). The same
template is a level 8 nuisance and a level 48 problem depending on where it stands, and nothing
about the authored row changed — which is the point of templates, and the reason a mob's *authored*
level cannot be what decides whether killing it taught you anything.

**Multipliers bake in at spawn time**, not read time. When a spawner creates a mob it resolves
the arithmetic once and stores concrete values on the instance — health, damage, xp, gold and the
level it fights at — along with a `spawn_multipliers jsonb` snapshot recording what was applied,
so "why does this kobold have 137 hp?" is answerable from the row.

`MobScaling` is the one place that knows how a placement is scaled: a factor on health, a factor on
damage, and the level those add up to. The invariant it holds is that **a mob at effective level N
has the stats of a mob authored at level N** — without it, a template that declares its own combat
stats scales while one that stays silent falls back to level-derived defaults, and two mobs in one
zone disagree about what the zone did to them purely by which fields their author filled in. Ratios such as
`damageMultiplier` are never scaled; multiplying a factor by a factor applies the zone twice. `armor`
*is* scaled, on the health dial, because it is a rating rather than a ratio — the fraction it becomes
belongs to `ArmorCurve` (§4.6) and is never stored.

*The table above was aspirational until 2026-08-14.* `Mob.ResolvedStats` was a verbatim copy of the
template, so `damage`, `health`, `itemPower` and `spawnDensity` reached nothing at all and
`strength` scaled only the health pool — the master difficulty dial made mobs tankier and never
deadlier, and the worked example's damage column was false in every row. Worse, `DamageCalculator`
read `Mob.Level` for the stats a silent template leaves to its level, so the most common kind of mob
fought at its authored level whatever its zone said. The example rows are now test cases.

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

**Abilities are content, held in the `abilities` table.** `AbilityCatalogue` is the set a fresh
database is seeded with — the same standing as the Millbrook rooms — and stops being consulted the
moment a row exists; the startup reconcile plants what is missing and never updates or deletes, so
a retune survives a restart. The table carries `path` and `unlock_level` as well as the mechanics,
because *who learns this and when* is as much a tuning decision as a cooldown. **Passives are the
exception and stay in code**: parry, dual-wield, and ambidextrous have no row, no cost, and nothing
to target, so they are Path-and-level thresholds the combat system reads directly. That is two
sources for "what does this Path get at level N", held apart by a rule rather than by memory — an
ability key must begin with its Path's name, no Path is called `passive`, so the namespaces cannot
collide.

Because a free jsonb parameter bag fails silently — an effect skips a key it does not recognise, so
a plausible misspelling produces an ability that costs its resource and does nothing —
`AbilityValidator` runs on every save and on every boot. It refuses an unknown effect key, a
missing required parameter, a buff below 1.0, a debuff on the wrong multiplier, and a wound that
expires before it ticks; it warns about progression shape. This is the one place in the builder API
that refuses on content grounds rather than following §7.4, and the reason is that a broken exit
announces itself the moment somebody walks into it while a broken ability never does.

**Cooldowns are whole numbers of the 2-second combat beat** (§2.3), and their length follows how
much an ability changes the fight — for anything with a duration, *duration ÷ target uptime*, so
nothing with a refresh rule can be held up permanently.

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
attackRating = level/2 + MightMod + weaponBonus       (a mob: level/2 + 6)
defenseVal   = 10 + level/2 + defenseRating           defenseRating = AgilityMod + Σ item defense
needed       = clamp(defenseVal − attackRating, 2, 20)

miss   if natural d20 <  needed
hit    if natural d20 >= needed
crit   if natural 20                                  → damage dice rolled twice, modifier once

damage     = weaponDice + MightMod (+ ability riders)
             a silent mob's dice are (1 + level/2) to (4 + 3·level/2)
mitigation = min(0.75, armor / (armor + 100))         armor = Σ item armor
final      = max(1, damage × (1 − mitigation))
```

No positional terms and no zone terms anywhere in the formula — the whole thing is a pure
function of the two combatants, which is exactly what makes it unit-testable in isolation.

**Both sides carry `level/2`, and that is not decoration.** Attack rating grew at `level/2` while
the number to beat grew at `level/4`, so the gap between them widened past the die's entire range:
by level 15 a player hit on every swing, by level 30 so did every mob, and a d20 had stopped being
consulted anywhere in the game. Matching the rate cancels it at parity, leaving gear, attributes and
the *level difference* to decide — all small enough for twenty faces to express. A higher-level
defender is still harder to hit; an equally-matched one is a coin the die can actually flip.

**The clamp is the guarantee, and the guarantee is the point.** Clamping `needed` to 2–20 means a
natural 1 always misses and a natural 20 always hits, whatever anyone authors or buffs. Nothing can
be equipped into being unhittable and nothing can be stripped into being auto-hit, so neither
property depends on somebody's numbers being sensible. The two ends stay open by construction.

**A critical is a natural 20 and nothing else.** It also used to trigger on beating the defence by
ten or more, which was reasonable while overshoot stayed small and became absurd once it did not:
at level 50 every landed mob blow overshot by ten, so *every* hit was a critical and the dice were
rolled twice permanently. Overshoot is a symptom of the scaling above, so a rule that read it was
measuring the bug.

**Armor absorbs a fraction, never a fixed amount** (`ArmorCurve`). Subtraction has no usable value
at any level: a rating of 10 reduced a level 25 mob's blow to the 1-damage floor and a level 50
mob's by less than half, and there is no number that behaves reasonably across even one band,
because the thing it is subtracted from grows and it does not. `armor / (armor + 100)` is scale-free,
cannot reach 1, and is capped at 0.75 well below that — so a fully equipped character still takes a
quarter of every blow that lands, and a mistyped extra zero changes nothing.

**Two authored numbers, doing two jobs.** `armor` decides what a landed blow costs; `defense`
decides how often one lands. Keeping them apart is what lets a shield be evasive and a breastplate
absorbent — one number could only have made every piece both, in a fixed ratio nobody chose. The
retired vocabulary (`armorFlat`, `armorPercent`, `armorMultiplier`) is gone rather than deprecated;
`armorMultiplier` in particular was accumulated across *every* equipped piece and applied to the
set's total, so six pieces at 1.2 multiplied to 2.99 and a piece carrying only a multiplier granted
nothing at all.

**Guard effects carry percentage points, not ratings.** `buff.defense` and `debuff.expose` author
`mitigation` in whole points, summed into the gear's fraction and clamped once with it. A rating
would have been worth twenty points to an unarmoured Adept and two to a geared Warden, since the
curve's returns diminish — backwards, given whose abilities these mostly are.

`Trinket` counts as an armour slot. It was absent from the sweep and is not one of the two hands a
damage multiplier is read from, so the eighth slot equipped and did nothing whatsoever.

**A silent mob's dice scale, rather than a fixed 1–4 with a `level/3` adder beside them.** The old
shape failed twice over. Its spread collapsed — a level 50 mob dealt 17–20, an eight percent band,
so no exchange in the late game was luckier than any other and the dice had quietly stopped being
rolled. And its total fell behind: player health grows by 5 a level and mitigation rises with the
tiers, so fights got *longer* as the game went on, from roughly thirty landed blows to kill a player
at level 1 to fifty-three at level 50. Scaling every face keeps it a d4 in shape — the spread stays
around three to one at every level — while the average tracks what a character of that level can
absorb. `NdN` dice would give a tighter bell, but the roll is a single uniform draw, so `(level)d4`
authored as a range would be `level` to `4·level`: both about two and a half times too large and far
*swingier* than the tight distribution real dice of that name produce.

The flat fallback is now **zero** rather than `level/3`, so all the level scaling is in one place and
an authored `damage` means what it says — its scaling comes from the zone dials (§4.4) like
everything else about a mob.

**Whether a fight is allowed at all is decided before this math runs**, by room flags (§4.10):
`peaceful` forbids combat entirely, and player-versus-player requires the `pvp` flag (§4.11).
Target validation is a separate gate on purpose — the damage formula never learns who is a
player and who is a mob, so it stays the same pure function either way.

**Leaving a fight is two questions, not one.** "The attacker left the room" and "the target left"
used to be one condition with one consequence — the *attacker* was removed. When the target was the
one who left, that took the wrong party out, and took them out without releasing them: they kept
`CombatState.Fighting` and their target while no longer being in `Combatants`, where the
end-of-fight sweep would have found them. Stuck for the rest of the session, refused every later
`kill` and every direction. The target's departure now removes the target, clears it from everyone
aiming at it — **including `Character.CurrentTarget`, which is the copy `kill` reads** — and is
narrated, because every other way out of a fight has words on it and this silence was
indistinguishable from the bug.

**Nothing wanders out of a fight.** `ShouldWander` gates on being in one, beside the sentinel and
`noMob` gates it already had. It is the argument the stun guard makes for itself: gating only a
mob's swings "would have it strolling out of the room mid-stun, which reads as the stun having done
nothing", and a fight is the same claim on its attention. Fleeing is still a decision a mob can
make; this is only about a wander timer coming due mid-swing.

**A fight is over when nobody in it still has somebody to hit — sides, not heads.** The rule was
once "two or more combatants", which is correct for exactly one shape of fight: one player against
one mob, where the mob dying leaves one combatant. **In a group it never ended.** Two players on
one mob is three combatants; the mob dies, two remain, the count is still two, and both players
were left permanently `Fighting` — refused every later `kill` and unable to walk out of the room.
It scaled with the party, and it was invisible solo, which is how it survived. Asking whether
anyone still has a target that is also in the fight is the same question the loop asks before
swinging, and it reads correctly for every shape without enumerating them: a duel stays live
because each duellist targets the other, a taunted player stays in because the mob's hate list
still names them, and a party standing over a corpse falls out because removing the corpse already
cleared every target that pointed at it.

### 4.7 Progression

Levels 1–50. XP from kills, quest completion, and first-time room discovery — rewarding
exploration, which is what a MUD is for. Each level grants attribute and ability points spent
deliberately: point-buy rather than use-based improvement, because use-based systems are
notoriously hard to balance and invite grinding.

**A kill is worth what it cost you.** Two numbers decide that, and nothing else does: your level,
and the level the mob actually fights at.

**The mob's level** is `MobLevel.Effective`, resolved at spawn beside `ResolvedXp` and snapshotted
there, so retuning a zone changes what spawns next rather than re-levelling what is already
standing in it:

```
effective = max(level × strength × √(health × damage), zone.MinLevel)
```

Combat power is roughly "how long it survives" × "how hard it hits", so a zone scaling both by `s`
makes a mob `s²` the problem — and the level has to move by the square root, or it stops meaning
what every other level means. The exponent is not chosen freely: `XpForLevel` is `1000·L·(L−1)/2`,
so **power ∝ L² is already what progression assumes**, and reusing it keeps one idea of what a
level is worth. `Strength` appears un-rooted because it scales health *and* damage together and is
therefore already the `s`; `Health` and `Damage` are one-sided and take the root. Four times the
power is twice the level, at every level.

The `MinLevel` floor sits underneath because the two say different things and both are the
author's: **the multipliers are how hard they made it, the band is who they made it for.** Not
clamped at `MaxLevel` — a boss above its band is deliberate. This is the first thing that reads
`Zone.MinLevel`, authored and stored since Phase 3 and consulted by nothing until now.

**A spawner may pin the level instead** — `fights_at_level`, null to let the zone decide. A zone
dial is zone-wide, and a zone is not uniform: scaling a 25–30 zone by two gives the level-20 content
you wanted from a level-10 template *and* turns a level-25 template already written for that zone
into a level-50 monster. Without a per-placement say the only ways out are authoring every template
at its final level — losing the reuse §4.4 exists for — or keeping the dials at 1.0 and writing one
template per tier.

It **states an outcome**: "fights at 27" is what a builder means, and the factor `N / templateLevel`
falls out (`MobScaling.FromTarget`). It **replaces** the zone's combat dials, world dials included —
composing would make 27 into 54 in a doubled zone and the number typed would be a lie. It is **not**
floored at `MinLevel`: the band catches mobs nobody said anything about, and the most explicit
statement in the system must not be overruled by the least. `xp` and `gold` are untouched, because
`strength` does not scale them either and a zone that is hard but stingy is a deliberate shape — the
cost is that a lifted rat pays a rat's experience, which the preview shows side by side.

On the wire it is a **word** — `"zone"` or the number as text — for the reason `wander` is one: on a
PATCH null already spells *leave this alone*, so a nullable number could not also spell *clear it*.
An item spawner is refused one; an item has no level, and a stored value would go live the day
somebody flips the kind to Mob.

**Your side** is `XpRelevance`, one window on `Floor(level) = min(level / 2, 30)`. At or above your
level pays in full; below `Floor` pays nothing; between the two it tapers on a straight line, so no
single level of mob is ever worth a jump. A cliff teaches players to count levels instead of
picking fights.

- **Who you stand next to is not an input.** An earlier version floored the whole party on the
  highest level present, and a level 9 beside a level 20 then earned nothing from a level 19 mob
  they would have been paid in full for killing alone. *Help with a fight you could have taken
  cannot be worth less than taking it.* Everyone present splits the pot; each share is then scaled
  by **that person's own** distance from the mob.
- **Gold is not level-scaled at all.** Experience is credit for the fight; gold is payment for
  being there. An even split, whatever the levels.
- **Zero says why**, because a silent zero reads as a broken reward, and a player who believes the
  reward is broken reports it rather than hunting something else.

One ordering carries real weight: **the window is applied after the experience multiplier** (§4.4),
so a generous zone scales a reward and can never resurrect a worthless one. Reverse it and an
8×-experience starter zone is the best farm in the game for a level 50.

**The window, as a table.** Percentage of the mob's resolved experience, by the level it fights at:

| Player | m1 | m2 | m5 | m8 | m10 | m15 | m20 | m30 | m40 | m50 |
|---|---|---|---|---|---|---|---|---|---|---|
| **1** *(floor 0)* | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% |
| **5** *(floor 2)* | — | 25% | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 100% |
| **10** *(floor 5)* | — | — | 17% | 67% | 100% | 100% | 100% | 100% | 100% | 100% |
| **20** *(floor 10)* | — | — | — | — | 9% | 55% | 100% | 100% | 100% | 100% |
| **30** *(floor 15)* | — | — | — | — | — | 6% | 38% | 100% | 100% | 100% |
| **40** *(floor 20)* | — | — | — | — | — | — | 5% | 52% | 100% | 100% |
| **50** *(floor 25)* | — | — | — | — | — | — | — | 23% | 62% | 100% |

Two things authors should read off it. **A level 1 has no floor**, so the first level of the game
never punishes anyone for what they can find. And the window is *wide* — a level 50 still earns
from a level 25 — so the rule retires content slowly rather than in steps.

And the same kobold from §4.4, at 120 base experience, killed by four different players:

| Zone | `strength` | `xp` | Resolved | Fights at | p10 | p20 | p30 | p48 |
|---|---|---|---|---|---|---|---|---|
| `millbrook` | 1.0 | 1.0 | 120 | 8 | **80** | — | — | — |
| `sunken-crypt` | 2.5 | 1.0 | 120 | 20 | **120** | **120** | **45** | — |
| `the-deep` | 6.0 | 2.0 | 240 | 48 | **240** | **240** | **240** | **240** |
| a greedy starter zone | 1.0 | 8.0 | 960 | 8 | **640** | — | — | — |

The last row is the ordering doing its job: eight times the experience is eight times the reward
for someone the fight is still meant for, and **nothing at all** for a level 20 — the multiplier
scales what the kill is worth, it does not decide whether it was worth anything.

The first two rows are the reason effective level exists. Same authored row, same 120 experience,
and the crypt version pays a level 20 in full while the Millbrook one pays them nothing — because
the crypt version is a genuinely harder fight and the template could not say so.

**`consider` reads the same two numbers** — the warning and the reward are one judgement shown
twice, and both go through `WorldState.EffectiveLevelOf`. Above your level it still reports on
absolute level differences, because danger does not scale the way relevance does: five levels up is
a hard fight at 10 and at 50 alike. Below your level it reports the window, and says outright when
there is nothing left to learn. They previously disagreed — a fixed ±5 band against half your level
agrees around level 10 and does not at 50, where a level 44 mob read *"you are much stronger"* and
then paid 77%.

**The verb is `attack`, and `kill` still works.** The rename is about the word the game reaches
for first, and there was never a mechanical reason for that word to be the harsher one — the old
help line already read *"kill &lt;target&gt; (k) - attack target"*. `kill` stays registered and
hidden from `help`, because unregistering it would have taken `k` with it, and thirty years of
muscle memory is not a tone decision. `attack` asks for three characters: `a` and `ab` already
belong to `abilities`. Older sections below say `kill` because they are describing bugs that
happened when that was its name.

**Spells are cast; skills are done.** `cast bolt rat` and `kick rat` — the verb matches what the
character is doing, because *"cast kick"* describes a boot to the knee as though it were
sorcery. The split is **derived, not authored**: the two caster Paths pay Focus for all eighteen
of their abilities and the two martial Paths pay Stamina, so the cost type already draws the line
and a second field for it would be a second source of truth to disagree with.

- **Every ability is usable as a verb of its own**, resolved *after* the command table misses — so
  an existing command can never be taken out from under someone, and abilities added later need
  no registration. Done as a fallback rather than as thirty-seven registered verbs because the
  table is global while abilities are per-Path: registering them would put an Adept's Amplify in
  front of a Shade's Ambush for `am`, and the Shade would be told they do not know an ability
  they have.
- **`cast` refuses a skill**, naming the verb form instead. The refusal is the teaching.
- The cost is that an ability sharing a name with a command is unreachable as a verb, which is why
  the moderation verbs are `kickplayer`, `banplayer`, `muteplayer` — a command called `kick` would
  have taken the Warden opener away from every Warden in the game.
- **Abilities resolve by display name, not by key.** `shield bash`, `shield-bash`, and
  `warden.shield-bash` all arrive at the same place, matched longest-first so a two-word ability
  is not read as a one-word ability plus a target.
- **An ability says what it did, per target, with the number.** *"Your Kick hits a rat for 7."*
  This was one line before the loop — *"Your Kick takes effect!"* — naming no target, no amount,
  and no outcome, so a player could not tell a hit from a miss or an area effect that caught four
  things from one that caught one. Read from the **health delta** rather than from the executor,
  which is the same reasoning threat credit already uses: the executors return void and each
  computes its own numbers, so the wound is the one measure that cannot drift from what landed.
  Three outcomes, because there are three kinds of ability — something lost health, something
  gained it, or neither. The third is not a failure: a stun, a root, a taunt, and a buff all land
  without moving a health bar, and reporting them as nothing would make half the catalogue look
  broken. Those use the effect's own `name` — *"leaves a rat reeling"*.
- **A hostile ability opens a fight, and closes one.** Both ends were missing, because abilities
  resolve in their own system earlier in the pulse and used to write to `Vitals.Health` and stop:
  - *Opening.* Engagement is keyed on the effect's `IsHarmful`, not on damage dealt — the
    abilities that move no health are exactly the ones that most need the fight to exist. Wounds
    only tick inside a combat, so an opening Ambush applied a bleed that then never ticked, and a
    stun landed on something that carried on standing there. The caster's own weapon follows the
    ability **only when they were not already fighting something**: `kick rat` is a way to start a
    fight, and one that leaves the player standing there afterwards is not one — but throwing a
    debuff at the second mob in the room is not a request to turn your back on the first, and an
    area effect must never pick one victim out of the room to swing at.
  - *Closing.* Deaths from outside the combat loop are resolved at the top of the next combat
    tick, through the same `HandleDeath` a swing's kill goes through, so the experience, the loot,
    and the corpse are identical however the killing damage arrived. Without it a mob killed by a
    kick sat in the fight at zero health for ever: it could not swing, so the fight never dropped
    below two combatants and never ended, and the player stayed `Fighting` — unable even to `kill`
    again. Sweeping *first* is what preserves "the blow that kills ends the exchange here and
    now": whatever killed a combatant, it is out before anyone takes a turn.

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
- **Idle emotes carry their own cadence.** Each line in a template's `emotes` list is either a
  bare string, taking a default of every 20–60 seconds, or a row with `text`, `minSeconds`, and
  `maxSeconds`. Both shapes stay valid and may be mixed, because the bag is schemaless and every
  emote authored before timing existed is a bare string.
  A cadence *per line* rather than per mob, because the two things a mob says are rarely worth
  hearing equally often — a shopkeeper calling the catch of the day every few minutes is
  atmosphere, and the same line every four seconds is a reason to leave the room. Ranges rather
  than fixed intervals, so three rats spawned in one sweep do not fall into step and read as
  clockwork. At most one line per mob per tick, and a freshly spawned mob is *scheduled* rather
  than fired, so nothing greets the room the instant it appears.
- **A mob stands still unless something says otherwise.** `wanders` on the template is the
  default, and a spawner's three-valued `wanders` overrides it per placement — *follow the
  template*, *always*, or *never* — with *follow the template* being what a fresh spawner has.
  The old arrangement had no template field at all: wandering was what every mob did unless its
  spawner ticked `sentinel`, which put the decision on the placement rather than on the thing
  being placed, so one shopkeeper spawned by two spawners could wander from one and not the other.
  It also defaulted the wrong way. Most authored mobs are shopkeepers, quest givers, and guards
  that belong somewhere specific; the ones that should roam are the minority worth naming, and
  absence should resolve to the harmless answer the way room flags do (§4.10). A rat that fails to
  wander is a dull room; a quest giver that wanders off is a chain nobody can finish.
  **Both ends are spelled `wanders`, in one direction.** The spawner's field used to be `sentinel`
  — the same fact with the opposite sign — and a pair of flags meaning one thing in two directions
  is the bug this codebase has already shipped once, when every `weaken` in the game made its
  target harder to kill. The resolution is therefore a coalesce, `spawner.Wanders ?? template`,
  with nothing to invert.
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
2. EngineOptions.StartingRoom     -- the world's origin, guaranteed to exist
```

A character who has never used `bind` therefore respawns where they first entered the world, which
is the simple behaviour; `bind` and the `respawn` flag exist so builders can place waypoints as the
world grows outward, without that being a second system.

**Two steps, not three.** An earlier version of this list had *the entrance room of the character's
home zone* in the middle, which was never built — there is no entrance field on a zone to build it
from. It stays out deliberately rather than as an omission: a long run back is an acceptable price
for never having bound, and it is the price that makes `bind` worth typing. Placing `respawn` well
is the lever here, and it is an authoring decision rather than an engine one.

**Where `bind` may be used is entirely the `respawn` flag**, and because flags resolve room → zone →
world (§4.10), setting it once on a zone makes every room in it bindable. That is the intended
granularity: bind points are a handful of deliberate places, not a property of individual rooms.
**No second flag.** *"You may bind here"* and *"you may respawn here"* are one fact, and a pair of
flags meaning one thing is the bug this codebase has already shipped once (§4.8, `sentinel`).

`bind` is free, uncapped, and instant — it is a waypoint, not a resource. Two guards, both small:
it is refused mid-fight, for the reason travel is, and it **names the point it replaced**, because
the verb is one keystroke and the cost of a stray press is otherwise invisible until you next die.

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

### 4.13 Shop pricing — one markup dial per shopkeeper

A shop sells at the item's base value, optionally raised by a **markup** the builder sets on the
shopkeeper. `0.1` means 1.1× and `0.25` means 1.25×, so the number reads as *"how much over the
odds this trader is"* rather than as a price factor to be mentally decremented.

```
markup <= 0  →  price = base
markup >  0  →  price = max(ceil(base × (1 + markup)), base + 1)
```

Four decisions in that, each with a reason:

- **It rounds up, not to nearest.** A shop rounds in its own favour, and a markup that vanishes on
  cheap goods is a dial that does nothing where most of the stock is. At 1.1× a 1-gold loaf costs
  2, which is the whole point of setting the dial on a village baker.
- **A markup always costs at least a gold**, which is what the `base + 1` floor says. Ceiling
  already guarantees it for any base of 1 or more, so the floor only does work on a base value of
  zero — where it is still the right answer, because a trader charging nothing is not a trader.
- **Absence is neutral, and negatives are read as absent.** No key means base price, as a fresh
  shopkeeper should. A discount shop is a coherent idea and this is deliberately not it: the
  minimum-increase rule contradicts a discount, so a shop that pays out on a purchase would need
  its own rule rather than a sign flip on this one.
- **Sellback is untouched.** What a shop *pays* stays `ShopSellbackPercent × item value`. Markup
  is a buy-side dial, so a greedy trader is expensive to buy from without also being generous to
  sell to — one dial for both would make "expensive shop" and "rich shop" the same word.

Stored as `markup` in the mob behavior bag (§4.8), read through `JsonBag` like every other key in
it, and edited beside the stock list in the mob editor. Not a column: it is one number a builder
tunes on a mob, which is exactly what the bag is for.

### 4.14 Reading your pack

The inventory listing is prose a player scans, not a data structure, so it collapses:

| Carried | Shown |
|---|---|
| one stone | `stone` |
| three stones | `stone (x3)` |
| one quest item | `a symbol of support (q)` |
| two quest items | `a symbol of support (x2 q)` |

- **The quest tag is `(q)`, not `(quest)`.** It sits at the end of every line it applies to and
  competes with the item's own name for the eye; a single letter reads as a margin note, which is
  what it is. `examine` still says the whole sentence — *"it is bound to a quest"* — because that
  is where a player goes when the mark raises a question.
- **A count and a tag share one parenthesis**, `(x2 q)`. Two brackets in a row on the same line
  would read as two separate facts about two separate things.
- **Grouping is by displayed name and by quest-ness, and it is display only.** Nothing about
  the instances merges: `get`, `drop`, `sell`, and `examine` still act on one item and still match
  the same way. Quest-ness splits a group because the tag is a statement about what you can *do*
  with the thing — one sellable stone and one bound stone are not interchangeable, and a line
  claiming otherwise would mislead about exactly the rule the tag exists to advertise.
- **Equipped items never stack.** They are listed under their slots, and the slot is the
  information — two identical rings worn in two places is two facts, not one fact twice.

### 4.15 Conditional exits — a locked door and a portal are one mechanism

An exit may require a **character flag**, an **item in the character's inventory**, or both. That is
the whole feature, and it covers the cellar door and the world-to-world portal with one rule,
because they are one rule: *this way is open to some characters and not to others.*

| Requirement | Field | What it is for |
|---|---|---|
| Character flag | `RoomExit.RequiredFlagKey` | A capability that cannot be lost — attunement to a realm, a rank, a pardon |
| Inventory item | `RoomExit.RequiredItemKey` | A capability that can be — a key, a writ, a severed hand |

**Two kinds because they fail differently, and that difference is the design.** A flag survives
being robbed, killed, and reincarnated; an item does not. Attunement to a Reach must be permanent
or the endgame is one pickpocket away from being unreachable. A vault key must be losable or it is
not a key, it is a flag with a picture on it.

- **Flags live on the Character, never the Account.** An account-level flag would let a fresh alt
  inherit attunement to the last realm at level 1, which is the entire gate defeated by the
  character-select screen. Per-character is the only scope that means anything here.
- **Character flags are content, so there is no registry** — and this is where they part company
  with `RoomFlags` (§4.10). That registry is closed because every flag in it has engine behaviour
  attached: `pvp` reaches combat, `dark` reaches the description, so registering a flag and writing
  its reader are one act. A character flag has *no* engine behaviour at all. Its only meaning is
  that some exit asks for it, which makes the real set a property of the authored world rather than
  of the binary — and a `Register` call per realm would mean shipping a build to open a new Reach.
  Keys are validated for shape only (`CharacterFlags.IsValidKey`).
- **Reachability replaces the registry.** `/validate` reports an exit asking for a flag that no
  quest grants, which is the same check and the same class of bug as a quest item nothing drops
  (§7.4). A typo is caught by nothing being able to grant it. **Absence is still the safe value:**
  a character who does not hold the flag does not pass.
- **`Character.Flags` is a `text[]` of held keys, not a `FlagSet`**, and that is not an
  inconsistency. A room flag needs three states because *absent* must fall through to the zone
  while an explicit *false* must override it. Nothing inherits from a character, so a flag is held
  or it is not, and a map of key to `true` would be a second way of writing one fact — the shape
  that let `sentinel` and `wanders` disagree (§4.8). It follows the `text[]` mapping already used
  for prerequisite quest keys and spawner room keys.
- **A quest grants a flag as a reward** — `Quest.RewardFlagKey`, beside XP, gold, and item. This is
  what makes the capability earnable through content rather than hardcoded, and it is why the exit
  names a *flag* rather than a quest key: re-author the chain, split it, add a second route, and
  the gate still works, because the gate never knew which quest it was waiting on.
- **Requirements are ANDed.** There is no motivating example for OR, and adding it later is
  additive.
- **The item is held, not consumed.** A key is reusable. A consuming toll gate is a later field on
  the same row, not a different feature.
- **Held means `OwnerCharacterId`** — carried or equipped, via the existing
  `WorldState.InventoryOf`. It is deliberately **not** recursive into containers, because an
  `ItemInstance` carries exactly one of owner / container / room, so a key in a backpack has no
  owner id. *This is a known limit rather than a decision:* no container content exists today, and
  the day it does, a key that stops working when it is put away is a bug report. Revisit then.
- **The refusal line is authored on the exit** — `RoomExit.RefusalMessage`, defaulting to a generic
  line. *"The gate does not know you."* and *"It is locked."* are different sentences, and deriving
  which to say from which requirement failed gets clumsy the moment an exit has both. The author
  knows what the door is; let them say so.
- **A requirement does not mirror to the reciprocal exit.** `dig` and `link` create both directions
  (§7.6), but a lock is directional — you can always leave a vault. The builder offers mirroring as
  a checkbox rather than doing it silently.
- **A wandering mob never traverses a conditional exit.** Mobs have no flags and no inventory worth
  interrogating, and the alternative is flagging `noMob` on every room behind every door and
  remembering to do it again whenever a builder digs one — the exact failure mode §4.8 rejects when
  it fences mobs by origin rather than by geography.

**Enforced in `Move`, and only there.** `flee` ends combat in place and never relocates, so there is
no escape path to tunnel through a lock. `recall`, death respawn, and builder `goto` teleport rather
than walk, and bypassing by nature is correct — they do not use exits.

That leaves one hole, and it is closed by authoring rather than by code: **never set `respawn` on a
room behind a conditional exit.** Otherwise a player binds inside the vault, walks out, and recalls
back in forever without the key. Binding already requires a `respawn`-flagged room (§4.12), so the
rule has somewhere to live; `/validate` should warn when the two are combined.

In practice the rule is nearly self-enforcing, because bind points are meant to be a handful of
deliberate places — hub towns — and a hub is never behind a lock. The `/validate` warning is a
backstop for the day someone flags a zone rather than a room, not the primary defence.

**Note what is *not* a hole.** `noRecall` refuses `recall` (`Travel.Refuse`), but death respawn does
not route through that check — it moves the character directly, so **dying always works, even where
recall does not.** That is correct and load-bearing: a player who dies must go somewhere. It means a
`noRecall` region is a restriction on convenient travel rather than a trap, and that dying is the
guaranteed way out of one, paid for in experience.

**Fails closed, and `/validate` is how a builder finds out** (§7.4). An exit naming a flag that is
not in the registry refuses. The asymmetry justifies itself: a wall players cannot pass is reported
within the hour, and a silently-open gate to the endgame is reported never.

**This takes `WorldBundle.FormatVersion` to 6.** A v5 bundle read as v6 would deserialise every
missing requirement to null and quietly open every gate in it — the silent partial apply the version
number exists to refuse. Both fields land in one bump rather than two. (5 was already spent on the
spawner level pin, which landed first.)

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

**Half of that shipped.** The stacking exists; the toggle does not, so on a phone the map still
holds 12 rem of a screen the transcript needs. That gap and the rest of the mobile story — the
keyboard covering the input bar, movement with no direction control, the builder's modifier-key
canvas — are worked through in [MOBILE.md](MOBILE.md), which is a proposal rather than a record.

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

- **There is no `mobs` table, deliberately.** A mob is a *population*, not a record: a spawner
  says "maintain N of template X across these rooms" (§4.8), so the sweep rebuilds the world's
  inhabitants at every restart from rules that are already stored. Persisting mobs would be a
  second, staler answer to a question the spawner already answers — and a worse one, since it
  would restore a rat with three health left standing wherever it had wandered to, which the
  spawner would then have to reconcile against its target.
  The asymmetry with `item_instances` is the point rather than an inconsistency: a sword dropped
  on the floor is persisted because **nothing would recreate it**; a rat is not, because
  something does.
  One did exist, from the initial migration until 2026-08-10, with a full EF configuration and
  never a single reader or writer — the only `DbSet` in the model with no call sites anywhere.
  It was empty in every environment it ever ran in. Dropping it cost one thing worth naming: the
  owned `vitals_*` columns were the only place in the model where an explicit `HasColumnName`
  differed from what `SnakeCaseNaming` would have produced, so the test proving the convention
  does not override an explicit choice lost its only subject. It is now asserted against a model
  built for the purpose, which is the better test anyway — a rule about every entity should not
  have its proof tied to one of them.
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

**The migration history is squashed while nothing has shipped.** Pre-release, a migration chain is
an accounting of how the schema was arrived at rather than a contract with anybody's data — no
deployed database exists that has to be walked forward through it. Six migrations became one on
2026-08-10, the second such squash; the rule for when it stops is **the first real deployment**,
after which a migration is a promise to a database somebody else's data lives in and squashing one
breaks it.

**Squashing means every dev database is rebuilt, which means content has to survive it.** §6 makes
Postgres the only source of truth for the world, so a database that is dropped takes the world with
it — and the seeder rebuilds only twelve rooms of Millbrook, not the Sunken Crypt somebody dug, the
mob templates they authored, or the spawners that place them. `tools/export-content.sql` writes
those eight tables out as upserts:

```
tools/export-content.ps1                      # writes backups/content-<date>.sql
docker exec dikuweb-postgres psql -U dikuweb -d dikuweb -f /tmp/content.sql
```

Upserts rather than inserts, so the file does not care whether the seeder has already run — applied
to a seeded database the twelve Millbrook rooms are updated in place with whatever they had actually
become, and to a bare one they are inserted. It exports **content only**: accounts, characters,
items, quest progress, and the audit tables are player data and history, and a content restore that
resurrected deleted characters would be a bug. **Abilities are exported**, and used not to be: the
reconcile rebuilt them from the catalogue on every startup, so a restored row was overwritten on the
next boot. That reconcile now only plants what is missing, so the table is authoritative and an
ability is content like everything else.

The rehearsal is the part worth keeping: build a scratch database, migrate it, seed it, apply the
export, then **regenerate the export from the rebuilt database and diff it against the original**.
Identical output is the only check that proves the round trip is lossless rather than merely
plausible, and it is cheap enough to run every time. **Run for real on 2026-08-13** and it earned
its keep twice: it proved the round trip lossless with abilities in it, and it caught the header's
own documented invocation being wrong — `psql` echoes `Output format is unaligned.` into the file
without `-q`, and that line is not SQL, so a restore taken the documented way died on its first
line before a single row landed.

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
| **Abilities** | Path, unlock level, cost, cooldown, cast time, targeting, and the effect with its own typed parameters. Grouped by Path in unlock order, since the question is nearly always the shape of a progression. Validator problems shown per ability; an error refuses the save (§4.5). |
| **Mob templates** | Name, description, icon, level, base stats, base xp/gold, behavior, loot, **attacks and the effect each one carries**. |
| **Spawners** | Template, target rooms, count, respawn seconds. |
| **Quests** | Giver mob, turn-in mob, required item and count, rewards, prerequisites, and the four dialogue strings (§4.9). Reachability warnings shown inline. |
| **Storyline graph** | The chain as an indented list — depth is what a builder needs to see, and indentation *is* "how far in is this". Flags cycles, unreachable quests, and prerequisites naming a quest that does not exist. |

Placing a one-off object in a room is just a spawner with `target_count: 1` — the builder offers
it as *Place item here* rather than making you think about spawners.

**An attack is edited as one thing, because it is one thing.** The attack editor was two lists
over the same array — every attack's timing in the first, every attack's effect in the second —
so adding a third attack put its effect three blocks below its own timing, and the only thing
tying the two halves together was a heading quoting the verb. That heading is ambiguous exactly
when it matters: a new attack defaults to `hit`, so clicking *Add attack* twice produced two
effect blocks both labelled *"hit" also…*. Now one card per attack holds what it says, how often,
how hard, and what it carries, with the effect's parameters indented under the effect that owns
them — one of the four reads five parameters, which is enough to read as a section of its own if
nothing says otherwise. The select is labelled *On a hit, also…* rather than *also*, because
**when** a rider applies is the part that is not obvious: it rides the swing's damage, so it
inherits the miss chance and the parry and lands only on someone the blow left standing (§12).

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
                                          -- NOT MAPPED. Designed, never built (§8)
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
| Exit requires a flag or item key that does not exist | Movement is refused with the exit's own message, and `/validate` reports it (§4.15). The one place an unknown key fails *closed* rather than being ignored: a wall is noticed within the hour, an accidentally-open gate to the endgame is not. |
| A character holds a flag no longer in the registry | Preserved on the character, ignored by the engine. Same rule as room flags, for the same reason — a downgrade must not silently strip what a player earned. |
| `respawn` set on a room behind a conditional exit | Allowed, and reported by `/validate`: it lets a character bind past the lock and recall in without the key ever after (§4.15). |
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

### 7.8 Passwords, and the Accounts tab

Passwords are stored as ASP.NET Core `PasswordHasher<Account>` V3 hashes — PBKDF2-HMAC-SHA512,
100 000 iterations, with a 128-bit salt generated per password and carried inside the encoded
string. There is no site-wide pepper, which is worth stating plainly because it has a
consequence: an `accounts` row is portable. Copy it to another installation and the password
still verifies, because nothing outside the row takes part in the check.

**Two ways to set one, because there is no third.** This deployment sends no email, so there is
no self-service recovery for somebody who has forgotten theirs:

- `POST /api/auth/password` — the account changes its own, proving it knows the current one. The
  cookie is not proof enough on its own: it is `SameSite=Lax` and lasts a fortnight, so without
  the check a borrowed laptop converts into permanent ownership in one request.
- `POST /api/admin/accounts/{username}/password` — an admin sets someone else's outright and
  tells them out of band. Admin only, audited as `PasswordReset`, and refused when an admin aims
  it at themselves: that path exists on the account screen, where the current password is asked
  for. The audit row records the act and the hour, never the password.

**A password change invalidates the other sessions.** The case it exists for is that somebody
else knows the old password, and a change that left their fortnight-long cookie working would not
address it. Each ticket carries a stamp of the password it was issued against
(`PasswordStamp`), compared during the same `OnValidatePrincipal` revalidation the role and ban
checks use; a mismatch rejects the cookie. The ticket's own `IssuedUtc` looks like it would serve
and does not — sliding expiry re-issues a cookie roughly weekly, so an old session would quietly
acquire a timestamp newer than the change and survive it. The account is evicted from the world
at the same moment, because an SSE stream was authorised when it opened and does not re-check.

**The Accounts tab** is the admin-only fifth tab in the builder. It searches accounts and, for
one of them, changes role, bans and unbans, mutes for a duration and lifts it, retires a
character, and sets a password. It drives the same `AccountAdminService` the in-game verbs do, so
the two cannot disagree — and the live effects each action pushes into the loop are shared
(`AdminLiveEffects`) for the same reason. Its tab is hidden from non-admins, its route redirects
them, and the API refuses them: the first two are presentation, and only the third is the
boundary.

---

## 8. Phases

The builder lands early — with no content files, it is the only way to author a world, so it
gates all content work after Phase 1.

### Phases 0 through 5 ✅ **complete**

Summarised here; the full checklists and the notes from each build are in
[HISTORY.md](HISTORY.md), which is where the postmortems went when this section was condensed.
Roughly half of those entries are accounts of something written, checked off, and wired to
nothing — the pattern §12's two rules exist for.

**A citation of a phase subsection resolves there, not here.** Source comments name
`PLAN.md §5.3`, `§5.2b`, `§5.2c` and the like; those headings kept their numbers and moved to
`HISTORY.md` intact, so the number is still the address — the file it lives in changed.

| Phase | Done when | Shipped |
|---|---|---|
| **0 — Foundation** | `docker compose up`, `dotnet run`, `npm run dev`, `/health` 200 | Solution and projects, central package management, postgres:18 compose, EF + Npgsql, `IGameClock` / `IRandomSource` from the first commit, React scaffold, Testcontainers |
| **1 — Vertical slice** | Two browsers log in, walk a seeded zone, see each other, talk | Auth and characters, session registry, 250 ms loop, SSE with ring buffer and replay, the command parser, World→Zone→Room, `RoomLayoutService`, the five client panels, link-dead grace |
| **2 — Builder: geography** | A zone is built end to end in the browser, no SQL | Roles and policies, the `WorldMutation` path with loop ack (§7.3), world/zone/room/exit CRUD with `content_audit`, the flag registry (§4.10) rendered into all three editors, §7.4 degradation with a test per row, advisory `/validate`, grid painter, zone canvas, walk-and-build (§7.6) |
| **2a — Role administration** | An admin makes somebody a builder from inside the game, and a ban reaches someone already connected | `admin_audit`, the admin API and Admin policy, `AccountAdminQueue`, `promote` / `demote` / `whois`, `Notify` and `SetActorRole`, and `OnValidatePrincipal` revalidation — which is what makes banning a connected player work at all (§7.7) |
| **3 — Objects and inhabitants** | A zone's difficulty is a slider, and the same kobold is trivial in one zone and lethal in another | Item and mob templates and instances, the item verbs, equipment slots, spawners and population maintenance, multiplier resolution at spawn time with `spawn_multipliers` recorded (§4.4), the multiplier editor and preview on both world and zone, mob behavior authoring, mob AI v1 with per-line emote cadence |
| **4 — Combat and progression** | You can kill something, loot it, and level up — and the multipliers visibly matter | The combat state machine on per-combatant attack clocks (§2.3), `kill` / `flee` / `consider`, the §4.6 damage model, the §4.11 target gate, death and the XP penalty (§4.12), `bind` and respawn fall-through, XP and levelling, regen and rest, aggression and assist, `EquipmentResolver` in the damage roll |
| **5 — Depth** | Abilities, quests, shops, parties, travel | Eight abilities per Path to level 20 from one `AbilityCatalogue`, seven effect executors and all three targeting modes, threat accounting behind one `HostileActionGate`, the quest engine with chains, repeats, `abandon` and dormancy, the Quests tab and storyline graph, shops and currency with a per-shopkeeper markup (§4.13), session-scoped parties with an XP split, `tell` / `reply` / channels, `recall` and the `noRecall` reader, and the pack listing (§4.14) |

Three lines from those phases are open rather than closed, and stay here rather than in the
history:

- [ ] **Builder: *Respawn zone*** to apply live multiplier edits to mobs already standing. Neither
      half is built: `POST /api/builder/zones/{key}/respawn` is listed in §7.3 and is not mapped in
      `BuilderEndpoints`, and no client calls it. Deferred nice-to-have from Phase 3, and the last
      thing §4.4's *"editing a multiplier affects future spawns only"* leaves a builder unable to
      see. §12 has listed `/respawn` among the endpoints written and never wired since Phase 3 —
      it was never written.
- [→] **Teleport as a spell** — not built, and not a gap. A destination is a `world.zone.room` like
      any other, so a teleport effect is a parameter rather than a new kind of link: an executor
      reading a room key and calling `Travel`. That seam exists now (§5.3, `recall`) rather than
      being invented alongside the spell.
- [→] **Pets and charmed mobs** — moved to §13, which records the constraint that blocks one
      rather than treating it as work that is merely late.

### Phase 6 — Operations

Partly done ahead of schedule — the deployment pipeline landed alongside Phase 5.

- [x] **Admin commands the loop can answer itself: `teleport`, `stat`, `kick`, `shutdown`.** In
      `AdminWorldCommands`, deliberately apart from `AdminCommands` — those touch the account
      store, which §2.1 forbids the loop, so they hand off to a queue and are answered later.
      `shutdown <minutes|now|cancel>` warns at nine milestones; it came from playtesting, because
      progress is already safe (§11) and what a warning protects is the half hour someone spent
      walking to a boss.
- [x] **`ban` / `unban`, `mute` / `unmute`, and `set`.** The first four outlive the session and go
      through `IAccountAdminQueue`; `set` is world-side and answers immediately. A ban **evicts**,
      since an SSE stream was authorised before the ban existed. A mute is a time
      (`accounts.muted_until`), not a flag, and covers every one of the six verbs that carry words
      to another player. `set` takes a closed field list rather than reflecting over `Character`.
- [x] **Rate limiting per caller.** Three policies: commands partitioned by character, builder by
      account and looser, auth by remote address and tight. 429s carry `Retry-After`; the SSE
      stream is deliberately unlimited. **Known weakness, named rather than hidden:** behind nginx
      every caller shares the proxy's address, so the auth limit is a site-wide cap until
      forwarded headers are honoured — which needs a trusted-proxy list this repo does not have.
- [x] **Instrumented, with no exporter.** `EngineMetrics`, six instruments on a meter named
      `DikuWeb.Engine`, all from `System.Diagnostics.Metrics` so nothing joined the dependency
      graph. Every pulse is recorded rather than only the slow ones — a log has no distribution,
      so one bad pulse and a p99 creeping up for a week look identical. Where it is sent is a
      deployment decision, made below.
- [x] **World export/import (JSON)** — `GET /api/builder/export`, `POST /api/builder/import`,
      carrying the same eight content tables `tools/export-content.sql` covers (§6.1). A scoped
      export is **closed over its references, not merely filtered**: `?zone=` also carries the
      templates its spawners place, the mobs and items its quests name, the items those mobs drop,
      and the world above it — a bundle that was only filtered imports cleanly and then spawns
      nothing. Every entity goes through `WorldEditor` one primitive at a time, so the loop stays
      the single writer and each lands a `content_audit` row. An import is a **merge, not a
      mirror**; there is deliberately no replace mode that deletes the difference. **It is not
      atomic** — one mutation per entity is one transaction — so `?dryRun=true` reports collisions
      while changing nothing and a partial import answers **207**.
- [x] **Exporter and dashboard.** A Prometheus scrape endpoint on the server
      (`MetricsExport`), with Prometheus and Grafana in `docker-compose.prod.yml` and the
      dashboard provisioned from `tools/monitoring/` so it lives in git rather than only in
      Grafana's database. Pull rather than push, which buys the `up` series — the one that says
      the process stopped answering at all. `/metrics` is unauthenticated and stays private
      because nginx forwards only `/api/` and `/health`.
      **Both histograms carry explicit buckets**: the OpenTelemetry defaults start 0, 5, 10, 25 ms
      and a healthy pulse here is under a *tenth* of a millisecond, so every observation would land
      in the first bucket and every quantile would be an interpolation inside it — a p99 panel
      drawing a flat line at ~4.95 ms forever, looking like a measurement.
      One trap worth naming: Prometheus 3 negotiates UTF-8 metric names and stores what the
      exporter calls them, so it held `dikuweb.pulse.duration_…` with dots while `curl` of the same
      endpoint showed underscores. Every panel read "No data". `metric_name_escaping_scheme:
      underscores` in `prometheus.yml` is what fixes it, and only running the stack finds it.
- [x] **Scheduled `pg_dump` backups + a rehearsed restore drill.** A `backup` sidecar takes a
      nightly dump and **restores it into a scratch database and compares exact per-table row
      counts before keeping it** — a dump that will not restore is deleted rather than kept,
      because a file that looks like a backup and is not one is worse than no file. It refuses to
      run at all if `/backups` is not on a volume, after dumps were found landing in a container's
      writable layer where they look completely normal and vanish on recreate.
      `tools/restore-drill.ps1` is the rehearsal: it restores a dump, **starts the server against
      it**, and reads the room count out of the loop's own startup line. That is a different
      question from "does it restore", and §6.1's `backups/dikuweb-full-2026-08-10.dump` answers
      the two differently — it restores perfectly and the server will not start against it,
      because it predates the migration squash. Procedure in
      [RUNBOOK.md](RUNBOOK.md) §3b, tested.
- [ ] Deployment pipeline:
      - [x] Dockerfile (multi-stage: publish layer, runtime layer, `dumb-init` entrypoint)
      - [x] Migration strategy settled: applied at startup rather than by an init container,
            because a single-writer loop with no backplane cannot run two instances (§6.1)
      - [x] Health checks gate readiness; `/health/ready` includes database check
      - [x] Reverse proxy with SSE buffering off — `proxy_buffering off` in
            `client/nginx.conf.template`,
            `X-Accel-Buffering: no` set on the stream response
      - [x] Deployment automation: `docker-compose.prod.yml`, per-environment deploy configs,
            and a GitHub Actions image build/push to ghcr.io
      - [x] Runbook: [RUNBOOK.md](RUNBOOK.md) — backups, the drill, restoring for real, rollback
            when a startup migration fails, a triage table, and the known gaps. Every procedure in
            it has been run. The migration-failure section is short for a reason worth stating:
            **no migration here suppresses its transaction**, so each is atomic under Npgsql and a
            failure leaves the schema at the previous migration — making it a redeploy rather than
            a restore.

### Phase 7 — Mobile client

Not started, and deliberately after Operations: a game nobody can deploy is not improved by being
playable on a phone. Planned in full in [MOBILE.md](MOBILE.md) — findings, the layout it argues for,
and the reasoning behind each phase. The checklist there is the authority; this is the pointer.

- [ ] **M0–M3, the game.** Viewport and keyboard handling, a phone layout where the transcript owns
      the screen, touch verbs in place of a keyboard, and surviving a backgrounded tab. Roughly six
      to nine days, and worth shipping in pieces.
- [ ] **M4a, the zone canvas off its modifier keys.** Plain drag-to-pan behind a movement threshold,
      on Pointer Events. A desktop improvement first — touch support falls out of it rather than
      being added to it, which is why it is separated from the rest of the builder work.
- [ ] **M4b, the builder on small screens**, with the canvas summoned rather than resident.
- [ ] **M5, installable.** A manifest, once there is something behind the icon worth keeping.
- [ ] **One server change**, listed here because it is not client work: `Program.cs` never binds the
      `Engine` configuration section, so `Engine__LinkDeadGraceSeconds` and `Engine__StartingRoom` in
      `docker-compose.prod.yml` are no-ops today and the link-dead window is the hardcoded 90
      seconds. Mobile wants that window longer; every deployment wants it bound.

---

## 9. Testing

Each row states the property, not the bug that motivated it — the bugs are in
[HISTORY.md](HISTORY.md), and several of these rows are the only thing standing between the
codebase and their return.

**By layer:**

| Layer | Approach |
|---|---|
| Domain | Pure unit tests. Combat formula, multiplier resolution and rounding, stat math, shop pricing, narration, parser — no mocks, no I/O. |
| Engine | Manual clock + seeded RNG. Step N pulses, assert world state. Fully deterministic. |
| Architecture | Domain declares no coordinates; command handlers never touch `RoomLayoutService`. The guardrail that keeps the map cosmetic (§4.2) as the codebase grows. |
| Robustness | One test per row of §7.4. Delete a room out from under a player, point an exit at nothing, orphan a spawner — the loop survives every one. |
| Server | `WebApplicationFactory` + Testcontainers Postgres, including an SSE test that opens the stream, POSTs a command, and asserts events arrive in order. |
| Client | Vitest for the protocol and state layer; Playwright for login → move → see-map, and build-a-room → walk-into-it. |

**The load-bearing invariants**, each one a property some part of the design rests on:

| Area | What is asserted |
|---|---|
| Room flags | Resolution is room → zone → world → default; an unknown key survives a round trip; a wrong-typed value resolves to the default; `peaceful` beats `pvp`. Everything else rests on **a room with no flags at all is not PvP**. |
| PvP | Refused unflagged, allowed flagged, ends the round either combatant leaves, never targets a party member. Every hostile action — swing, cast, area effect, taunt — answers through the one gate, so the coverage is that they *agree* rather than that each was remembered. |
| Death | XP loss floors at the level threshold and never de-levels; none below the min level or on a PvP death. Respawn falls through all three candidates, including a bind room deleted mid-session. Nothing leaves the inventory. |
| Verbs | **Every registered verb is reachable by something a player can type.** A verb whose name is a strict prefix of an earlier one is shadowed completely and nothing reports it. Asserted over the whole table, plus the shortest allowed abbreviation of each. |
| Abilities | **Every entry in the catalogue** is castable by display name and usable as a verb — a theory over the whole list, since per-ability tests each pick one. A skill is refused by `cast` with the verb form named; an existing command always wins. |
| Abilities in a fight | An ability opens a fight and the player keeps swinging — asserted on the mob's health falling, not on the target field. A kill landed by an ability or a wound ends the fight, pays the same XP and gold, and leaves the player able to start another. |
| Fights ending | Every shape, because the old rule was right for one: groups of two and three both released when the mob dies, and released *completely*; a bystander who never chose a target too. Against that, the shapes that must stay live — a duel, an unhit mob, one of two mobs dying. |
| Quests | The full loop, plus every refusal leaving the item in the player's inventory. Chains unlock in order and cannot be short-circuited by pre-holding the item. Deleting a referenced mob leaves the quest in the journal rather than wiping it. |
| Chains | A three-link chain, so downstream is transitive rather than one hop. A step that starts itself applies every rule `talk` would. A repeatable chain reruns only as a whole; a non-repeatable quest stays finished whatever the chain does. `abandon` returns a quest to never-started in the world *and* in storage. |
| Spawners | A mob that wandered out still counts; scattered sweeps never exceed the target; a kill is replaced; two spawners of one template do not count each other's work; a hand-placed mob satisfies nobody's target. |
| Wandering | Turns back at a zone border, crosses with `roams`, moves freely inside its zone, and a mob with no recorded home zone is confined rather than freed — absence resolves to the restrictive value. |
| Parsing an item | Both halves of `give <item> <recipient>` may be several words, so the split is *found* rather than assumed: every split point is tried and the first where both halves resolve wins. Failure messages quote what the player typed, not the fragment a bad split left. |
| Reading the pack | Duplicates collapse with a count, separately when quest-bound. The load-bearing assertion is that **grouping changed no verb** — `get`, `drop`, and `sell` still take one item out of a stack of three. |
| Shop markup | Rounding unit-tested on `ShopPricing`: up at every fraction, never less than a gold over base, absent or negative reading as none. Through a shop, `list` and `buy` quote the *same* number and sellback is unmoved. Authored through `AsPersisted`. |
| Narration | Every line about a mob goes through `NarrationHelper`, not interpolation — a name authored as "a rat" does not become "an a rat", a name without an article gains one, and a line ending in "!" gains no full stop. Every line of `examine` names its subject rather than pronouning it. |
| Emotes | Both authored shapes read and mix; a row with no text is dropped; an inverted range reads as its lower number. A fresh mob is silent on its first sweep, one line lands per tick, and **the schedule survives the jsonb round trip** — the one bug class this codebase keeps rediscovering. |
| Jsonb bags | Anything read out of `behavior`, `state`, or `attacks` is authored through `WorldHarness.AsPersisted`. A test that hand-builds the bag in C# proves nothing about the running game (§12). |
| Parties | Forming, expiry, leadership passing, and the dissolve at one member. Leaving the world drops you, asserted through `WorldState.Remove` — the one door out. The split pays only members standing where the mob died. |
| Travel | `recall` reaches the bind point and falls through when unbound or deleted. Every refusal has a test, because the value of `noRecall` having exactly one reader is entirely in that reader being consulted. |
| Roles and moderation | Promotion reaches an open session without a relog; demotion revokes within the revalidation interval; a banned account is rejected while connected; self-demotion refused. A mute is refused on all six speech verbs and expires against the clock. Every change writes an `admin_audit` row. |
| Admin | Every verb reads as unknown to a player *and to a builder* — content authority is not moderation authority. Shutdown warns before it acts, reschedules rather than refusing, and only reaches the host when the countdown runs out. |
| Rate limits | A flood is refused once the bucket empties but early commands land; the 429 carries `Retry-After`; **one player's flood does not refuse another player**; the event stream is never limited. Asserted against a host with real numbers, since the shared test host lifts them. |
| Telemetry | Every pulse recorded rather than only slow ones; meter and instrument names pinned, because renaming one breaks every dashboard silently. `/metrics` answers and carries the engine meter, with the sub-millisecond buckets still on it — a view silently reverting to defaults leaves a dashboard that draws a plausible line. *The numbers themselves are not asserted: a p99 under an xUnit host is not the p99 §11 is about. Neither is the name Prometheus stores, which is not the name this endpoint appears to serve — only running the two containers together shows that.* |
| Moving a world | The closure is most of it, because a merely-filtered bundle *looks* complete. Then a round trip against edits made after the export, a second import creating nothing and doubling no spawner, a dry run that leaves the edit in place, an unknown flag surviving, and a foreign format version answering 400. Authorization asserted on these two routes **by name** — a route mapped one line outside `MapGroup` would be an unauthenticated dump of the whole world. |
| Two devices, one character | A second device **entering** turns the first out at once, before it has opened a stream. The turned-out device's retry is refused *and* the new device still has the character. A reconnect under the same id is not a takeover; being displaced does not take the character out of the world. Plus the one that cost the most: **a re-render must not reopen the stream**, counted across three re-renders passing fresh inline callbacks. |
| The prompt | Typing anywhere focuses the input — but not Ctrl+C over selected scrollback, not Tab, not Enter over a focused button, and not while the builder is up. Tab is *reported rather than swallowed* when nothing matches. History survives a reload, stays per-character, and treats its storage as hostile. The scrollback follows the newest line only while already at the bottom. |

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

**First measurements**, once the exporter existed to take them (single player, idle world):

| Target | Measured | |
|---|---|---|
| Pulse p99 < 25 ms | **0.21 ms** | p50 0.05 ms. Two orders of magnitude of headroom |
| Command → first byte p95 < 150 ms | **238 ms in-process alone** | p50 126 ms |

**The command target as written cannot be met, and not for a fixable reason.** A command waits for
the next pulse before the loop touches it, so on a 250 ms pulse the wait is roughly uniform on
[0, 250] ms — p50 near 125 and p95 near 237, which is what was measured, before a single byte
crosses the network. The number is structural, not a symptom.

Three honest options, none of them "optimise": drop the pulse (250 → 100 ms costs 2.5× the loop's
wakeups and buys a p95 of ~95 ms), handle input-only commands off the pulse boundary at the cost of
the single-writer simplicity §2.1 rests on, or **restate the target as what it is measuring**.
Prefer the third for v1: the number that matters to a player is whether a keystroke feels answered,
and a 250 ms tick is the game's heartbeat rather than latency to be shaved. Left as a decision to
make rather than silently adjusted — it has been an unverified target from the start, and the
useful thing about verifying it is finding out it was the wrong one.

---

## 12. Next step

**Everything through Phase 5 is closed**, and Phase 6 is most of the way in: admin commands,
moderation, the three rate-limit policies, `EngineMetrics`, the deployment pipeline, and world
export/import have all landed. Every content type §4 describes can be authored in the browser with
no SQL, and nothing in §4.11 is approximated any more. [HISTORY.md](HISTORY.md) is the account of
how that was arrived at.

**Phase 6 is closed too.** The recovery half — the metrics exporter and dashboard, verified
backups, the rehearsed drill, and [RUNBOOK.md](RUNBOOK.md) — landed last because it only matters
once something has already gone wrong, and it turned out to be the part that found the most:

- The §11 command-latency target **cannot be met by construction** on a 250 ms pulse. Measured, not
  reasoned about; see §11 for the three options.
- A backup in `backups/` **restores cleanly and the server will not start against it**, because it
  predates the migration squash. Repair procedure in RUNBOOK §3b, tested.
- Dumps were landing **inside a container** rather than on a volume, which is invisible from
  inside it.
- Every Grafana panel read **"No data"** against a working exporter, because Prometheus 3 stores
  UTF-8 metric names and `curl` shows you the other ones.

None of the four is something reading the code would have found. All four came from running it.

Then **Phase 7**, the mobile client, planned in full in [MOBILE.md](MOBILE.md).

**Open, from playtesting** ([PlayTestingNotes.md](PlayTestingNotes.md) is the live inbox):

- A changelog, or GitHub releases — nothing records what changed between two builds.
- **Ability cooldowns are retuned; the pending-cooldown display is not built.** The numbers went
  first because the note understated the problem: ten of the eleven timed effects had a duration
  *longer than their own cooldown* and all of them refresh, so buffs and debuffs were permanently
  maintainable and the cooldown was decorative — Weaken sat at 200% uptime. Every cooldown is now a
  whole number of 2-second combat beats, since a swing is 8 pulses and anything else drifts against
  the fight forever; 14 of 37 were fractional. Length follows impact, and for a timed effect that
  means *duration ÷ target uptime*. Ambush moved *down*, because it is authored to stack three
  times and at 28 pulses could never reach two.
  What is left is the UI half, and it is the larger one: the client has never heard of abilities.
  `EventTypes` is `text`/`room`/`map`/`contents`/`vitals`/`sys`, so a fire event would arrive at a
  client with nothing to grey out. It needs an `abilities` roster event carrying remaining cooldown
  per ability — which also resyncs a reconnect for free — and a `cooldown` event on cast, with the
  client counting down locally rather than the server ticking one per pulse.
- The **UX evaluation** is written — [UX.md](UX.md), eight findings. The *Follow my character*
  checkbox is gone, which is the part the note asked for outright. What is left is the fix list at
  the end of that document; the top three are minutes each and the largest, resizable builder
  rails, wants the Pointer Events work Phase 7's M4a is already going to do.
- `examine` and `stats` are builder-aware; `look`, `inventory`, and `consider` are not.
- **Claude in the builder** (§13) — assistance with authored prose, starting with descriptions,
  built so that it proposes and never writes. Would be the first outbound HTTP call in `src/`.
- **Authored lines that mean exactly what they say** (§13). Emotes are formatted as predicates,
  which is right for what they are today and wrong for anything else a builder might write.

**Two lessons from the audits, worth carrying forward:**

- **A free-form jsonb bag must be read through a reader that accepts the shape storage returns.**
  `Behavior` and item `State` come back as `JsonElement`; code that pattern-matched the C# shape
  was false for every value that had round-tripped, which silently killed three features at once.
  `JsonBag`, `ItemState`, and `StatReader` exist for this. A test that hand-builds the bag in C#
  proves nothing about the running game — author it through `WorldHarness.AsPersisted`.
- **An endpoint with no caller is not a feature, and a checkbox is not evidence.** Reachability,
  the multiplier preview, and world delete were all written, checked off, and never wired to
  anything. `/respawn` is the sharper version and was misfiled here for months as one of them:
  it was never written at all, and both this list and §7.3 said otherwise. Before checking a box,
  name the test or the call site — and grep for the route. The quest editor was
  the same lesson caught a second time and the worst version of it: a `QuestEditor.tsx` checked
  off that was never written, so the plan claimed a whole authoring surface that did not exist for
  as long as nobody looked. **Naming a file is not evidence either — open it.**

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

### Command-line autocomplete — done, and deliberately stopping here

**Shipped, and considered finished.** Tab completes names of things *in the room*, from the
contents frame the client is already sent. The fragment is searched for rather than taken as the
last word, so a multi-word name completes from the middle of one. No verb list ships to the client
and none should: the engine already prefix-matches verbs, so a copy in the browser would be a
second list to keep in step, to save keystrokes that are already saved.

**Carried items are out of scope by decision, not by omission.** `drop`, `wear`, and the item half
of `give` all name something in the pack, and inventory is not on the wire at all. Completing them
would need a frame of its own, sent when the pack changes — it must *not* be folded into the
contents frame, which is sent when a *room* changes, so a list that also claimed to describe the
pack would be stale after every `buy`. That is a protocol addition to buy a small convenience, and the convenience is
not wanted enough. Recorded so the next person to notice the gap finds the reasoning rather than
re-deriving it. `spawn` completing against real template keys is the same trade on the builder
side.

One asymmetry, noted rather than scheduled: clicking a room keyword inserts the *template key*
(`bar-maiden`), while Tab inserts the *label* (`a bar maiden`). Both target correctly, because
`NameMatch` accepts either.

### Per-template alias lists

Matching is derived from the name, which covers the common case. Nothing reaches a named wolf
called "Fang" whose key is `grey-wolf`. An explicit alias list on the template is the fix; the
matcher (`NameMatch`) already ranks candidates, so it is a new source of candidates rather than a
new algorithm.

### Authored lines that mean exactly what they say

**The question.** Emotes go through `NarrationHelper.BuildSentence`, which supplies the article,
the capital, and the full stop — so an authored *"laughs at something said three tables away"*
becomes *"An old man laughs at something said three tables away."* That is right for the shape
emotes have today, where the line is a predicate and the subject is always the mob. It is wrong
the moment a builder wants a line the helper cannot express:

- a line that starts with something other than the mob — *"A draught goes through the room as the
  old man shifts in his seat"*;
- a mob referred to twice, or possessively — *"turns his cup"* works only because the pronoun is
  hardcoded into the authored text, and it is wrong for a rat;
- a line addressed to a specific player, which needs a name the template cannot know.

**Two ways out, and they compose rather than compete.**

*Exact mode:* a per-line opt-out, so the authored text is emitted verbatim and the builder owns
the whole sentence. Cheap, and it makes the awkward cases possible immediately. The cost is that
every line opting out also opts out of the article handling that made this consistent in the
first place, so a builder can reintroduce *"old man laughs"* by ticking a box.

*Tokens:* the better long-term answer, and **most of it is already written**.
`NarrationHelper.FormatProse` supports `{entity:key}`, `{player:key}`, `{direction:key}` and bare
`{key}`, articles the entity forms, capitalises the line, and leaves an unknown token untouched
rather than blanking it. It has tests. It has **no production callers at all** — §12's lesson in
its usual form, and the reason to check before building: the work here is likely *wiring and a
token vocabulary*, not a formatter.

What a token vocabulary would have to answer, which is the actual design work:

- `{self}` for the emoting mob, articled — the current behaviour, spelled explicitly.
- Possessives and pronouns, which is where it gets hard. `{their}` needs a mob to declare its
  pronouns, and nothing in a template does. That is a content-model question, not a formatter
  one, and it is the same question `examine` dodged by naming the subject instead of pronouning
  it (§9, *Examining a mob*).
- `{someone}` for a player in the room, which needs a rule for *which* player, and a fallback for
  when the room is empty of them.

**Why it is parked.** Nothing needs it yet: the tavern lines all fit the predicate shape, and the
one that looked like a counter-example — *"turns his cup a quarter-turn"* — only reads oddly if
the same line is given to something that is not a man. Build it when a second mob wants one of
the shapes above, and let that mob decide the token vocabulary rather than guessing it now.

### Claude in the builder

**The idea.** A builder writing a room, a mob, or a quest is doing two jobs at once: deciding what
exists, and writing the prose that makes it exist. The first is the interesting one. An assistant
in the builder UI would take the second when it is wanted, starting with the smallest and safest
case — descriptions.

**The decision that makes it safe: it proposes, it never writes.** The endpoint returns text and
touches nothing. The builder reads it, edits it, and saves through the same `PATCH` every other
edit goes through, so `WorldEditor` stays the only path into the world and `content_audit` still
records a human account as the author of the change. Nothing new can corrupt a zone, because
nothing new can write to one. A bad suggestion is a paragraph in a textarea that gets deleted.

This also means the client work is genuinely small, because the commit point already exists.
`RoomDetailsTab` has a dirty flag, an explicit Save, and `NavGuard` behind it — a *Suggest* button
fills the same buffer a keystroke would, and everything downstream already behaves. The same is
true of the description fields on mob and item templates.

**Why descriptions first, and not because they are easy.** They are the only case whose output
needs no validation beyond "it is prose". A mob or a quest is a *shape*: stats, a behaviour bag, an
attack list, dialogue keyed by state. Generating one means a model emitting JSON that has to
satisfy the same contracts `BuilderContracts` enforces, and an endpoint that refuses anything that
does not — including the quiet failures, like a quest whose required item nothing can obtain
(`reachability` already knows how to ask that). That is a real project. Descriptions are a week and teach us
whether the assistance is wanted at all.

**Where the quality actually comes from.** Not the model — the context. "Write a room description"
is worthless; "write a room description for *the Cellar Stair* in *Millbrook*, exits down and
north, adjacent to the tavern common room, in a zone whose other rooms read like *this*" is the
feature. All of that is already server-side in `BuilderQueries`. So the request should name an
**entity**, not carry a prompt: `{ kind, key, instruction? }`, with the server assembling context.
The client stays thin, and the prompt stays one thing in one place to tune.

The best few-shot examples are the zone's own existing rooms. That needs no schema change, costs
nothing to assemble, and gets better as the zone grows — a zone with fifteen rooms teaches its own
voice. A `tone` field on the world or zone is the obvious alternative and should wait: it is a
migration, and sibling rooms may make it unnecessary.

**What the server has to grow.** Four things, and one of them is a first.

- An `IContentAssistant` with one implementation calling the Messages API, registered through
  `IHttpClientFactory`. **This would be the first outbound HTTP call anywhere in `src/`** — every
  other thing this server talks to is Postgres, in process. That is worth noticing rather than
  discovering: it introduces a dependency that can be slow, down, or rate-limited, in a codebase
  that has never had one.
- Configuration for the key, which must never reach the browser: the browser calls us, we call
  Anthropic. `appsettings.json` is committed and already keeps the connection string empty, so the
  same rule applies — user secrets in development, environment variable in deployment.
- Its own rate-limit policy. `RateLimiting.Builder` is 120 burst at 20/second, deliberately loose
  because a tree view issues a burst of reads. Every one of those reads is free and local; an
  assist call costs money and takes seconds. Same trusted role, completely different budget, so it
  needs a separate policy partitioned by account rather than a share of that one.
- A timeout, a bounded output size, and cancellation tied to the request. Nothing here goes near
  the game loop — it is an ordinary request thread, and §2.1's rule about the loop is untouched —
  but a builder who navigates away should not leave a call running.

**The open question: does the audit record that a model drafted it?** `content_audit` exists to
answer "who changed this, when, and what did it say before", and "was this written by a person" is
the same class of question, asked a year later when nobody remembers. Recording it means the save
contract carries a provenance flag on every entity that can be assisted, which is not free. Not
deciding this before building is fine; not noticing it would not be.

**How it fails, and what that must look like.** Upstream is down, slow, or refusing. The rule is
that the builder degrades to a plain textarea: Save is never gated on the assistant, a failed
suggestion is a message beside the field, and no state is left half-applied. The feature is a
convenience, and the moment it can block authoring it has cost more than it gave.
