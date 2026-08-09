# The playtesting apparatus

A standalone application that logs characters into a running world, drives them through scripted
plans, and records everything every session saw — for a person to read.

```
dotnet run --project tools/DikuWeb.Playtest -- --server http://localhost:5050
dotnet run --project tools/DikuWeb.Playtest -- --server http://localhost:5050 --plans tools/DikuWeb.Playtest/plans
```

It writes a run directory and prints a `file://` link to `index.html`.

## Why this exists, given 1,200 tests

The tests assert **properties**, one at a time, against a harness. A playtest is different in kind:
a **sequence a player would perform**, whose product is a **transcript somebody reads**.

The distinction is not academic. *"Your Kick takes effect!"* — no target, no amount, no outcome —
passed every assertion in the suite, because every assertion about it was true. It was obviously
wrong the moment a person read it in context. That is the class of bug this catches, and no
assertion was ever going to.

The second thing it does is make multi-player scenarios practical at all. Anything needing two or
three people at once was, in practice, not being tested by hand.

**It is a recording, not a gate.** It always exits zero. A missed expectation is a question for a
person, not a failure — and a runner that exited non-zero on a surprise would become a second test
suite that CI learns to ignore.

## Why it was built rather than installed

- **MUD bot frameworks** (TinTin++, Mudlet) are telnet. This game is HTTP POST up, SSE down.
  Bridging that costs more than the runner does.
- **Load tools** (k6, Artillery, Gatling) emit aggregate metrics. A per-session readable transcript
  is not a shape they produce.
- **Playwright** could drive the real React client headlessly, and one day might — it would cover
  the scrollback rendering, the map and the vitals panel, which this cannot. But its artifacts are
  video and traces rather than transcripts, it needs browser downloads, and it is roughly an order
  of magnitude slower and flakier than speaking the protocol.

Meanwhile the protocol plumbing already existed here and was already correct: the SSE frame parser
is lifted from `tests/DikuWeb.Server.Tests/Infrastructure/SseReader.cs`, which had already solved
the two failures that make hand-rolled SSE readers flaky.

## How a plan is written

```yaml
name: Two players
about: |
  Prose for the reviewer: what this is for, and what to look at.

cast:
  - name: Alice
    path: Adept
  - name: Bob
    path: Warden
    role: Admin          # optional; needs --admin-user/--admin-password, or --hosted

steps:
  - actor: Alice
    note: A marker in the transcript saying what the next stretch should show.
    do: say well met

  - actor: Bob
    wait: { text: "well met" }     # or { seconds: 3 }, or { text: ..., timeout: 20 }
    expect: "well met"             # a string, or a list of them
    expect-not: "You say"          # second-person output must not leak across characters

  - note: Steps that must happen at the same time, for anything that is a race.
    together:
      - { actor: Alice, do: get coin }
      - { actor: Bob, do: get coin }
```

### The rules worth knowing

- **A step's window is everything since that actor's previous step.** Not "since this command",
  which cannot see the output that arrives on entering the world; not "since the plan began",
  which would let sequential combat steps all pass on the first exchange. Between the two is the
  only reading that matches what a plan means by "then".
- **Cast names are rewritten** in commands, waits and expectations. Character names are globally
  unique, so on the second run against a server `Alice` is really `Alicexqbfm`; the plan keeps
  saying `Alice` and the transcript is honest about the real name.
- **A wait that times out is flagged**, not merely noted. It means the plan and the game disagree
  about what happens, which is the subject — whoever turns out to be wrong.
- **There is no `level:` or `start:`.** A plan that wants a level-12 Warden puts an Admin in its
  own cast and types `set Theron level 12`; a plan that wants somebody in the tavern walks them
  there. Both then appear in the transcript as things that happened. Setup performed invisibly is
  setup nobody can check, and it would have needed engine features that do not exist and should
  not be added for a test tool.

## What a run produces

```
runs/2026-08-09T18-38-41Z/
  index.html            actors side by side on one clock, unmet observations gathered at the top
  run.json              every entry, with the raw SSE payload of every frame
  <plan>/interleaved.log
  <plan>/<actor>.log    exactly what that player would have seen, and nothing else
```

`index.html` is the point for anything with two actors: a party fight is only legible when what
Bram saw sits beside what Kael saw at the same moment. The single-actor plans read just as well as
text.

## Design

Five pieces, in `tools/DikuWeb.Playtest/`:

| Piece | What it is |
|---|---|
| `IGameTarget` | Where the game is. `RemoteTarget` connects by URL; a hosted target booting the server in-process is the next piece of work. Nothing above this interface knows which. |
| `Actor` | One driven character: its own account, cookie jar, SSE stream and scrollback. An account each rather than one account driving three, because sharing would share the mute state, the role, and the per-account cap. |
| `PlanRunner` | Performs the steps. **Nothing here throws on a disappointing result** — the steps after a missed line are usually the ones that explain it. |
| `Transcript` | One shared, timestamped record; the per-actor views are projections of it, which is what guarantees they agree about ordering. |
| `HtmlReporter` / `JsonReporter` | The run directory. Self-contained: a run gets copied around and opened from `file://`. |

### Two things the first runs taught

**The room arrives twice, and that is correct.** `SendRoom` sends the title, description and exits
as a structured frame *and* as text spans, because §5 makes the scrollback authoritative so a
player who ignores the map misses nothing. The first transcript rendered both and every room
double-printed, which reads exactly like an engine bug. The `room` frame is a panel; the prose is
the scrollback.

**A timed-out wait had to be flagged.** The first two-actor plan waited for words the game never
says — `invite` for a line reading *"asks you to join"* — and the run reported "nothing flagged"
while silently spending twenty seconds. Waiting for something that never comes is a finding.

## The apparatus is a client

It references `DikuWeb.Domain` and nothing else. If it ever needs `Engine` or `Server` to work,
that is a finding about the protocol rather than a reason to add a reference — and no production
code should ever change to accommodate it.
