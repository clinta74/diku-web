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

## What a run leaves behind

**It never stops the server.** In `--server` mode the apparatus is a guest: it connects to
something you are running, and shutting down a shared dev or staging box because a test run
finished would be far worse than any bug it could find. Hosted mode owns a server's lifetime and
tears it down; that is the only mode that should.

*(If a server does outlive the shell that started it, that is `dotnet run` — it spawns the app as a
child and killing the wrapper does not take the child with it. That orphan is what locks build
outputs into MSB3021. `Get-Process DikuWeb.Server | Stop-Process -Force`.)*

**It cleans up its own characters.** A janitor runs after every run — one Admin actor that deletes
every character the run created, by the name the world actually gave it. On wherever an admin
credential makes it possible; `--no-cleanup` opts out. It cannot delete the character it is playing,
so each run leaves exactly one janitor behind rather than one character per actor, and the summary
names it rather than hiding it.

**It still leaves accounts.** Every actor registers a real account, and there is no delete-account
endpoint — deliberately. They all carry the `playtest_` prefix precisely so they can be told apart
from a person's at a glance:

```sql
-- What the apparatus has left behind
select count(*) from accounts where username like 'playtest\_%';

-- Purge it. Characters are soft-deleted by the schema; the accounts go with them.
delete from accounts where username like 'playtest\_%';
```

Hosted mode makes the question moot, because the database is thrown away with the run. Against a
long-lived server, purge occasionally — and never point the apparatus at production, which the
account-per-actor design makes obvious enough to state once.

## A plan brings its own world

The starter seeder lays down rooms and nothing else, so every plan that meets a mob, buys
something, or picks something up needs content that does not exist in a fresh database. Plans
declare that content in a `world:` block, and the apparatus **checks it before anyone plays and
builds what is missing**:

```yaml
world:
  zones:
    - key: aldenmoor.sunken-crypt
      name: The Sunken Crypt

  rooms:
    - key: aldenmoor.millbrook.smithy            # verified: it must be there
    - key: aldenmoor.sunken-crypt.crypt-hall     # dug, because it says where from
      from: aldenmoor.millbrook.chapel-nave
      direction: down
      zone: aldenmoor.sunken-crypt
      title: Crypt Hall

  items:
    - key: horseshoe
      name: a horseshoe
      value: 8
    - key: river-stone
      name: a river stone
      room: aldenmoor.millbrook.chapel-nave      # a spawner lays them here
      count: 3

  mobs:
    - key: village-smith
      name: the village smith
      room: aldenmoor.millbrook.smithy
      disposition: npc
      shopkeeper: true
      markup: 0.25
      sells: [horseshoe, iron-nail]
```

Every plan in the library now runs against an empty database. That was worth more than it sounds:
the alternative was a footnote in this file that nobody reads until a plan fails, and the failure
it produced was the confusing kind — a transcript of a player standing in an empty room typing at
nobody, which reads exactly like a broken game.

### The five rules

- **Through the builder API, never SQL.** A running server plays an in-memory world owned by one
  loop thread and loaded at boot (PLAN.md §2.1). A builder edit is a mutation queued into that
  loop, applied, and written through to Postgres, so it reaches the live world at once. An
  `INSERT` reaches storage and nothing else: the row would be invisible until a restart, and the
  plan would then run against a server that really does not have the mob. Same door as the
  builder, same guarantees, and the apparatus stays a client.
- **Idempotent by key, and it never reconciles.** Anything already there is left exactly as it is,
  *including where its numbers differ from the plan's*. A run that quietly re-pointed somebody's
  hand-built shopkeeper at a plan's markup would be editing a world it was invited to observe. The
  report says `found` or `made`, and a plan reading oddly against pre-existing content is a
  question for a person — which is what the whole apparatus is for.
- **A room is dug, or it is only checked.** A room created from nothing has no exit into it, so it
  is a room no player can reach and no plan can walk into: a fixture that reports success and
  leaves the plan exactly as broken. Give it `from` and `direction` and the passage and the room
  arrive together, which is how the builder does it (§7.6). Without them, a missing room is
  reported and the plan plays without it.
- **Mobs are sentinels.** A fixture that wanders off between provisioning and the plan reaching it
  fails for reasons about mob AI. Anything a plan stands next to and talks to should also be `npc`
  — a killable quest giver or shopkeeper is a soft-lock (§7.4) — and anything it fights must not
  be, which is why `test-dummy` is `passive`.
- **Blocked is never fatal.** A world that could not be authored is one whose plans play against
  what is actually there. The console says what was found, made, and refused; `fixtures.log` in the
  run directory has all of it.

It needs an admin credential, because there is no "promote me" endpoint (§7.7) — the same
`--admin-user`/`--admin-password` a plan with an Admin actor already needs. Admin is the one role
that satisfies a Builder requirement, so nothing extra is registered and provisioning leaves no
account behind. Without one, nothing is built and the run says so. `--no-fixtures` skips the whole
step and plays the world exactly as it stands.

### One timing rule worth knowing

**A spawner is a rule, not an instance.** The loop's spawn sweep runs every 60 pulses (15 s) and is
what actually stands a mob in a room, so a run that started immediately after provisioning walked
into an empty one. The first fixtured run showed it exactly: *"You don't see 'rat' here."* and then,
two seconds later, *"A rat appears."* Nothing was wrong with the fixture, the plan, or the game —
the run was simply faster than the world. Creating a spawner now costs one 17-second wait before
the first plan, and only on a run that created one.

### On the numbers a fixture chooses

`shop-markup` prices a horseshoe at 8 and a nail at 1 so its transcript is arithmetic a person can
check: at 1.25× those are 10 and **2**, and the 2 is the round-up-with-a-minimum rule from §4.13 —
the half of that feature a reviewer should see land. Its sale-back figure assumes Millbrook keeps
the default `itemValue` of 1.0, since sellback is half of the *instance's* value rather than half
of what the smith charges. Tuning that zone moves one line of one plan, which is cheap — but it
will look like a shop bug if nobody knows.

## Load mode

The same apparatus, asked a different question. `--sessions` holds a stated number of characters in
the world, plays a plan with all of them at once, and reports what it did to the game loop.

```
docker compose -f docker-compose.load.yml up -d --build web
docker compose -f docker-compose.load.yml run --rm runner     --server http://web:8080 --plans Plans/load-village.yaml     --sessions 200 --ramp 120 --hold 180
```

| Flag | |
|---|---|
| `--sessions <n>` | Concurrent character sessions to hold. Rounded up to a whole number of casts. |
| `--ramp <s>` | Arrivals are spread over this long, then the run waits for the world to actually be full. |
| `--hold <s>` | The measured window, which opens only once it is. |
| `--metrics <url>` | Where `/metrics` is, when it is not on the same address as the game. |

*On Windows, run that second command with `MSYS_NO_PATHCONV=1` in front of it.* Git Bash rewrites
anything that looks like a Unix path before the process sees it, so `--out /runs` arrives as
`C:/...` and the run dies on `Access to the path '/app/C:' is denied` — which reads like a
container permissions problem and is not one.

### The verdict does not come from anything timed here

**A command POST returns `202 Accepted` the moment it is queued.** `GameEndpoints.SubmitCommand`
hands the input to the gateway and returns without waiting for the loop to touch it. So every
latency this apparatus could measure at the socket is the time to enqueue a string — and under a
loop that had fallen four seconds behind, that number would stay beautiful. It is not a useful
signal and it is worse than useless as a headline, because it looks like one.

The signal is **pulse duration**, it lives inside the server, and `/metrics` is how it gets out.
`MetricsProbe` scrapes it at both ends of the hold and `Histogram.Since` subtracts them: every
instrument there is a total since boot, so the difference is exactly the pulses that happened while
the world was full, with the idle minutes before the run excluded rather than averaged in.

Three properties of that report are worth knowing:

- **"Over 25 ms" is a count, not an estimate.** The exporter puts an explicit bucket boundary at
  25 because that is the §11 budget, so the number of pulses above it is read off the histogram
  rather than interpolated across a bucket. `CountAbove` returns null for any bound that is *not* a
  boundary rather than quietly interpolating an answer that would look just as authoritative.
- **Percentiles carry the bucket they were interpolated inside.** `p99 12.4 ms (bucket 10–25)` is
  honest where a bare `12.4` is not: everything inside one bucket is indistinguishable, and the
  decimals are arithmetic rather than measurement.
- **Pulses owed is the sharpest line in the report.** A 250 ms loop owes four pulses a second; if
  it delivered three it is behind, and no percentile can show that, because the pulses it missed
  were never recorded at all. A loop running half as often reports the same healthy p99 as one
  keeping up.

The verdict refuses to answer when the world did not hold the sessions asked for. A run that only
got 180 in has measured a smaller world, and "p99 under budget" about that world would be true and
useless.

### Why not k6

"Why it was built rather than installed" above rejects load tools because they emit aggregates
rather than transcripts. Under `--sessions` this tool emits aggregates too, so that reason no
longer applies and a better one is owed. It is the load *shape*: request rate is not what stresses a single-threaded loop. Two hundred characters standing
in separate rooms typing `look` is nearly free. The expensive work is fan-out, combat, threat and
regen — most of it landing on pulses where no request arrived at all. k6 generates request volume
and has no way to drive the world into an expensive state; a plan does, because a plan is a
description of what players *do*.

The other half is that k6 would have to reimplement register → create → enter → stream → command,
which `Actor` already does, and its SSE support is a newer non-core module. Where request rate
genuinely *is* the metric — the auth and builder APIs — k6 remains the right tool.

### The plan matters more than the number

`Plans/load-village.yaml` walks Gatetown's three-room spine, which concentrates two hundred
characters where fan-out costs the most, and runs `who`, whose cost *is* the population.
`Plans/load-idle.yaml` is its control: the same sessions at the same cadence, but every command
local to the caller, so nothing is broadcast.

**Run both, or the number means nothing.** On its own the circuit cannot separate *"the server
cannot hold 200 sessions"* from *"the server cannot hold 200 sessions in three rooms"* — different
findings with different fixes. Together they answered it in one line: the control handled more
commands on a twenty-fifth of the CPU. See PLAN.md §11 for the numbers.

Two details in the circuit were learned rather than designed:

**The circuit has to close.** `--sessions` cycles a plan's steps for the whole hold, so a plan
ending somewhere other than where it started walks its cast into a wall on the second lap and
spends the measured window recording two hundred people failing to move.

**Think time is load-bearing.** Without it a session runs its lap as fast as the server answers,
which measured four commands a second — faster than a person types and close enough to the
five-a-second limiter that the run measures a client being throttled. Two seconds a step puts each
session near half a command a second. The report prints the rate it actually saw, so this can be
checked rather than trusted.

### Two things the first load runs taught

**The generator will die before the server does, if you let it.** Two hundred sessions each keeping
a full transcript is the largest thing in the process, and a run that holds every line ends up
measuring its own garbage collector. Capping at two thousand *entries* was not a bound at all — one
`who` reply naming two hundred people is kilobytes on its own — and the apparatus died of an
OutOfMemoryException at seventy sessions with the measurement half-taken. `Transcript` now takes a
**byte** budget, which is self-tuning: a session drowning in fan-out keeps fewer lines. It also
drops the raw SSE payload, which nothing reads on a load session.

**The ramp ending is not everybody having arrived.** The last session's stagger lands on the ramp
deadline and arriving takes four round trips after it, so a window opened on the deadline itself
measures a world still filling — and the verdict correctly refused to answer. The runner now waits
for the server's own `dikuweb_sessions_active` gauge to reach the target, and starts the hold clock
from there.

### What it leaves behind

The same accounts an ordinary run leaves, two hundred at a time — see the purge above. The janitor
needs an admin credential to sweep the characters; without one, pass `--no-cleanup` and expect to
purge by hand.

## What is declared and not built

**`--hosted`** — booting a server in-process against a throwaway database. It says so and exits 2,
which is the right behaviour for an unbuilt flag. Fixtures took most of what it was for: a plan can
now build its own content against any server, so the remaining case is wanting a database nobody
else is using. Two things it would still buy: a run that leaves nothing behind at all (the
accounts, below), and content authored *fresh* every time rather than found from a previous run.

**Quests** are the one content type a fixture cannot author. Nothing in the library needs one yet —
`the-pack` reads the quest-item mark off an item flag rather than off a quest, which is the flag's
actual meaning — but a plan about the talk → fetch → deliver loop would need `quests:` here.

## The apparatus is a client

It references `DikuWeb.Domain` and nothing else. If it ever needs `Engine` or `Server` to work,
that is a finding about the protocol rather than a reason to add a reference — and no production
code should ever change to accommodate it.
