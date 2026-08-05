# diku-web

A browser-played, text-driven multi-user dungeon. C# / .NET 10 server, PostgreSQL 18,
React client. Server→client push over Server-Sent Events; client→server commands over HTTP POST.

See [PLAN.md](PLAN.md) for the full design.

**Current status: Phase 1 complete.** You can register, create a character, walk a 12-room
village, and talk to other players in real time. There are no items, mobs, or combat yet —
those are Phases 3 and 4.

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

Open a second browser profile (or a private window) and log in as someone else to see two
characters in the same room, on each other's maps.

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
dotnet test              # 139 tests: domain, engine, architecture, server integration
cd client && npm test    # 8 tests: client state reducer
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

## Layout

```
src/
  DikuWeb.Domain/        entities, rules, RoomKey. ZERO dependencies, no coordinates.
  DikuWeb.Engine/        game loop, commands, world state, IGameClock, IRandomSource
    Presentation/        RoomLayoutService - the ONLY place x,y exists
  DikuWeb.Persistence/   EF Core 10 + Npgsql, migrations, starter-world seeder
  DikuWeb.Server/        ASP.NET Core: auth, characters, SSE, command endpoint
client/                  React 19 + Vite + TypeScript
tests/                   xUnit; server tests use Testcontainers
```

Dependencies flow one way: `Server → Engine → Domain` and `Server → Persistence → Domain`.
Domain references nothing.

**The load-bearing rule:** a single game-loop thread owns all mutable world state. HTTP
handlers never touch it — they put messages on a channel and the loop drains them. That is why
there is not a single lock in the game logic, and why ticks are replayable. If you find
yourself wanting to mutate the world from a request handler, don't; send a message.

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
