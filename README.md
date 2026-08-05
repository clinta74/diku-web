# diku-web

A browser-played, text-driven multi-user dungeon. C# / .NET 10 server, PostgreSQL 18,
React client. Server→client push over Server-Sent Events; client→server commands over HTTP POST.

See [PLAN.md](PLAN.md) for the full design.

**Current status: Phase 2a complete.** You can register, create a character, walk a 12-room
village, talk to other players in real time, **build new geography from the browser** — no SQL,
no seeder edits, no restart — and hand out builder access from inside the game. There are no
items, mobs, or combat yet; those are Phases 3 and 4.

---

## Prerequisites

| | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 22+ | `node --version` |
| Docker | any recent | Docker Desktop or Rancher Desktop, **must be running** |
| `dotnet-ef` | 10.0.10 | `dotnet tool install --global dotnet-ef` |

## Local setup

```bash
# 1. Database (PostgreSQL 18 + Adminer)
cp .env.example .env          # defaults are fine for local work
docker compose up -d

# 2. Schema
dotnet ef database update --project src/DikuWeb.Persistence --startup-project src/DikuWeb.Persistence

# 3. Server
dotnet run --project src/DikuWeb.Server --urls http://localhost:5180

# 4. Client (separate terminal)
cd client
npm install
npm run dev
```

Then open <http://localhost:5173>, register, and create a character. The server migrates and
seeds the starter world automatically in Development.

### Playing

| Command | |
|---|---|
| `n` `e` `s` `w` `u` `d` | Move between rooms. Full names work too. |
| `look` / `l` | Describe the room in full. Movement shows only the title and exits. |
| `say <message>` | Speak to everyone in the room. |
| `who` | List everyone online. |
| `help` | The command list. |
| `quit` | Save and leave. Requires the whole word — no prefix. |

Most verbs accept any unambiguous prefix, so `l` is `look` and `sa hi` is `say hi`. Up and down
arrows walk the input history. Clicking a name in the **Here** panel types its keyword.

Builders and admins have more verbs — see [Building the world](#building-the-world). They are
hidden from `help` and answer *"not something you can do"* for everyone else, so a player never
learns they exist.

Open a second browser profile (or a private window) and log in as someone else to see two
characters in the same room, on each other's maps.

### Playing several characters at once

One login can drive multiple characters simultaneously — open each in its own browser tab.
Game routes are scoped by character (`/api/game/{characterId}/stream`), so each has its own
stream, scrollback, and link-dead window. The character select screen marks the ones already
in world.

The default cap is **3 characters per account**, set by
`Sessions:MaxConcurrentCharactersPerAccount` in `appsettings.json`. It exists because each
character in the world holds an open SSE connection and a ring buffer, not as a game rule
about multi-boxing — raise it if you want. Reconnecting a character that is already in world
does not consume a slot.

## Building the world

**The first account registered on an empty database becomes an Admin.** Everyone after that is a
Player, and an admin promotes them from inside the game:

| Command | |
|---|---|
| `promote <name> <role>` | `player`, `builder`, `moderator`, or `admin` — spelled out in full |
| `demote <name>` | Back to player. |
| `whois <name>` | Account, role, last login, and their characters. |

These name an **account**, not somebody standing in the room, so the target does not have to be
online — which is the ordinary case of someone asking to help build. Admin only: a builder cannot
hand out access to the builder. There is also `PATCH /api/admin/accounts/{username}/role` if you
would rather script it.

A promotion takes effect **without the promoted player logging out**. Their in-game verbs change
immediately, and their browser session picks up the new role within a minute — see
`Auth:RevalidationIntervalSeconds` in `appsettings.json`. The same mechanism is what makes a ban
land on someone who is already connected, so that interval is the one to shorten if you ever need
to remove somebody in a hurry.

An admin cannot demote themselves; there is no way back from an installation with no admins.
Every role change is recorded in `admin_audit` with who did it, and what it was before.

Builders get a **builder** button on the vitals bar. The panel has three columns: the world tree
on the left, the zone canvas and room editor in the middle, warnings on the right.

The fastest way to lay out geography is to **walk it**. Leave *Follow my character* ticked, move
around with `n`/`e`/`s`/`w`, and the editor re-targets whatever room you are standing in. When
you want a room that does not exist yet:

| Command | |
|---|---|
| `dig <dir>` | Create and link a room that way. If an exit already points at a room that was never built, this fills in *that* room, using the key the exit already names. |
| `dig <dir> into <zone>` | Same, but place the new room in another zone. Digging never crosses a zone boundary on its own. |
| `link <dir> <room-key>` | Point an exit at an existing room. |
| `unlink <dir>` | Remove an exit, and its reciprocal if there is one. |
| `rtitle <text>` | Retitle the room you are in. |
| `rflag [<flag> [on\|off\|clear]]` | List room flags with where each value came from, or set one. |
| `goto <room-key>` | Jump anywhere, exits not required. |

Descriptions belong in the panel, not the command line. New rooms are born titled *An Unfinished
Room* and flagged `unfinished`, which is the zone's build to-do list — they show hatched on the
canvas and clear themselves once you give them a real title and description.

### Things that are allowed to be broken

There is no draft/publish step: a save is live the moment it lands, including for anyone standing
in the room. The cost is that the world must tolerate being half-built, so it does:

- An exit can point at a room that does not exist. Players get *"The way is blocked."*; you get
  an offer to `dig` it.
- Deleting a room leaves inbound exits dangling on purpose, and moves anyone inside to a sibling
  room rather than orphaning them.
- Deleting a zone with players in it is refused. It is the one destructive edit gated on being
  empty, because the zone entrance they would be moved to is what you are deleting.
- Ragged grid art and missing legend entries fall back to a plain rectangle.

The **Warnings** panel reports all of that plus orphan rooms and unrecognised flag keys. It is
advisory and has never blocked a save. The one worth reading is `inherited-pvp`: setting `pvp` on
a zone makes every room in it lethal, so the warning names them.

Every write leaves a `content_audit` row with before/after and who did it —
`GET /api/builder/audit?kind=room&key=<room-key>`, or query the table directly. That is what
replaced git history when world content moved into Postgres, and it is honestly weaker; see
PLAN.md §10.

### Room flags

Flags live in a registry in `DikuWeb.Domain/Worlds/RoomFlags.cs` and resolve **room → zone →
world → default**, nearest level wins. Adding one is a `Register(...)` line plus the code that
reads it — no migration, and no client change either, since the editor renders its checkboxes
from `GET /api/builder/room-flags`.

Two rules if you add one. **Absence must be the safe value** — that is why the flag is `pvp` and
not `safe`, so a room nobody flagged, a mistyped key, and unparseable jsonb all come out
non-lethal. And **prefer clearing to setting false**: clearing removes the key so the zone
decides, while `false` is a decision about that room specifically.

## Services

| Service | URL |
|---|---|
| Client (Vite) | <http://localhost:5173> |
| Server (Kestrel) | <http://localhost:5180> |
| Liveness | <http://localhost:5180/health> |
| Readiness | <http://localhost:5180/health/ready> |
| Adminer | <http://localhost:8080> |

### Why the client proxies instead of calling Kestrel directly

Auth will be an HttpOnly, `SameSite=Lax` cookie, because the browser's native `EventSource`
cannot set an `Authorization` header (PLAN.md §3.2). Proxying `/api` and `/health` through the
Vite dev server keeps the browser on one origin, so the cookie behaves in development exactly
as it will in production. Calling Kestrel cross-origin would force `SameSite=None` in dev only,
which is how cookie bugs reach production.

## Tests

```bash
dotnet test              # 278 tests: domain, engine, architecture, server integration
cd client && npm test    # 18 tests: state reducer and zone-canvas layout
```

Server tests start a **real PostgreSQL 18 container** via Testcontainers, run migrations, and
seed the starter world, so `citext`, `jsonb`, and `uuidv7()` are genuinely exercised — as is
the live SSE stream, read incrementally rather than buffered. Docker must be running.

Two test groups are worth knowing about:

- **Architecture tests** (`DikuWeb.Engine.Tests/Architecture`) assert that Domain declares no
  coordinates and that command handlers cannot reach `RoomLayoutService`. They exist so the
  room map stays cosmetic as the codebase grows — see PLAN.md §4.2. If one fails, the fix is
  almost never to loosen the test.
- **Engine tests** run against a manual clock and a seeded RNG, so a "ten minutes of combat"
  test finishes in milliseconds and fails identically every time.
- **Graceful-degradation tests** (`DikuWeb.Engine.Tests/Mutations`) cover every row of PLAN.md
  §7.4 one by one. Live editing means the world is *allowed* to be invalid, so the contract is
  that a mutation refuses rather than throws — a mutation that took down the loop would take the
  world down for every connected player.

## Layout

```
src/
  DikuWeb.Domain/        entities, rules, RoomKey, RoomFlags registry. ZERO deps, no coordinates.
  DikuWeb.Engine/        game loop, commands, world state, IGameClock, IRandomSource
    Mutations/           WorldChange + the applier that runs builder edits on the loop
    Presentation/        RoomLayoutService - the ONLY place x,y exists
  DikuWeb.Persistence/   EF Core 10 + Npgsql, migrations, starter-world seeder
  DikuWeb.Server/        ASP.NET Core: auth, characters, SSE, command endpoint
    Admin/               role administration, admin_audit
    Building/            builder API, queries, world writer, content_audit
client/                  React 19 + Vite + TypeScript
  src/builder/           world tree, room editor, grid painter, zone canvas
tests/                   xUnit; server tests use Testcontainers
```

Dependencies flow one way: `Server → Engine → Domain` and `Server → Persistence → Domain`.
Domain references nothing.

**The load-bearing rule:** a single game-loop thread owns all mutable world state. HTTP
handlers never touch it — they put messages on a channel and the loop drains them. That is why
there is not a single lock in the game logic, and why ticks are replayable. If you find
yourself wanting to mutate the world from a request handler, don't; send a message.

**Builder edits obey it too**, which is why they take the shape they do. A save is a
`WorldMutation` on the same channel as player commands; the loop validates it against live world
state, applies it, and returns the ordered list of primitives it performed, which the writer then
replays into Postgres. Builder *reads* deliberately go to Postgres instead of `WorldState` — the
loop mutates that with no locks, so enumerating it from a request thread is a real race.

Dependencies flow one way: `Server → Engine → Domain` and `Server → Persistence → Domain`.
Domain references nothing — that isolation is what keeps the rules unit-testable.

## Conventions worth knowing before you commit

- **Warnings are errors.** Set in `Directory.Build.props`, deliberately.
- **Package versions live in `Directory.Packages.props`**, not in individual csproj files.
- **Logging uses `[LoggerMessage]` source generators, never inline templates.** This is a
  performance requirement on the game loop, not a style preference — see PLAN.md §2.4.
- **No `Random.Shared`, no `DateTimeOffset.UtcNow` in Domain or Engine.** Use `IRandomSource`
  and `IGameClock`, or the game loop stops being replayable (PLAN.md §9).
- **Migrations are explicit.** Never `EnsureCreated`.

## Troubleshooting

**Postgres container is unhealthy.** PostgreSQL 18 changed where Docker images store data: the
volume must mount at `/var/lib/postgresql`, not `/var/lib/postgresql/data` as it was through
PG 17. If you have an old volume, `docker compose down -v` and recreate.

**`dotnet build` fails with a file lock on `DikuWeb.Server.exe`.** A server instance is still
running. Stop it before rebuilding.

**Server tests fail to start a container.** Docker is not running, or the CLI cannot reach the
daemon. Check `docker ps`.
