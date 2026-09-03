# Finding a quadratic: a worked example

A case study in performance work, written from one real investigation in this codebase. Every
number here was measured; every wrong turn described was actually taken.

The subject is the room-refresh path, but the point is the method. If you only take one thing:
**the bug was not slow code. It was correct code called the wrong number of times**, and no amount
of optimising the code itself would have found it.

---

## 1. The target that could not fail

[PLAN.md](PLAN.md) §11 sets a target: *200 concurrent sessions on one process*, with *pulse
duration p99 under 25 ms*. The first measurement said:

| Target | Measured |
|---|---|
| Pulse p99 < 25 ms | **0.21 ms** — two orders of magnitude of headroom |

That number is worthless, and knowing why is the whole beginning of this story. It was taken with
**one player in an idle world**. For a single-threaded game loop, that measures an empty function.
The loop's cost is dominated by what players do to each other, and there was nobody to do anything
to.

> **Rule 1 — A benchmark that does not reproduce the shape of production measures nothing.**
> "Two orders of magnitude of headroom" was not a safety margin. It was an artefact of the test.

The second thing hidden in that number: it says nothing about *how much machine*. "200 sessions on
one process" is unfalsifiable until you say on how many cores. A result on a 16-core workstation
and a result on a 2-vCPU VPS are both "200 sessions" and neither predicts the other.

> **Rule 2 — A capacity target without a resource budget is not a target.**
> Everything below runs the server pinned at **2 CPUs / 2 GB** in a container, stated up front, so
> two runs a month apart are comparable.

---

## 2. Measuring the right thing

The obvious instrument is the wrong one here. A command POST returns `202 Accepted` the moment it
is queued:

```csharp
var accepted = gateway.TrySubmit(new PlayerCommand { ... });
return accepted ? Results.Accepted() : Results.StatusCode(429);
```

So **client-side latency measures the time to put a string in a channel.** Under a loop that had
fallen four seconds behind, that number would stay beautiful. Any load tool pointed at this
endpoint — k6 included — would report health while the game was unplayable.

The real signal, pulse duration, lives inside the process and comes out through `/metrics`. Two
scrapes, subtracted, give exactly the window you asked about.

> **Rule 3 — Measure at the layer where the work happens, not the layer that is easy to reach.**
> An asynchronous accept turns client-observed latency into a measurement of your own queue.

Three properties of the resulting report were worth building deliberately:

- **"Pulses over 25 ms" is a count, not an estimate.** The exporter puts an explicit histogram
  boundary at 25 because that is the budget, so the number above it is read off rather than
  interpolated. Asking for a bound that is *not* a boundary returns null rather than a
  plausible-looking guess.
- **Percentiles carry the bucket they were interpolated inside.** `p99 12.4 ms (bucket 10–25)` is
  honest; a bare `12.4` implies precision the histogram does not have.
- **Pulses *owed* leads the report.** A 250 ms loop owes four pulses a second. If it delivers
  three, it is behind — and **no percentile can show that, because the missed pulses were never
  recorded.** A loop running half as often reports the same healthy p99 as one keeping up.

> **Rule 4 — Ask what your metric cannot see.**
> Every percentile here is computed over work that *happened*. Work that was skipped is invisible
> to all of them, and skipping was the actual failure.

---

## 3. Separating the variable from the constant

The first real run held 200 sessions walking a three-room circuit. It failed badly. But that
result on its own could not distinguish two very different findings:

- *the server cannot hold 200 sessions*, or
- *the server cannot hold 200 sessions **in three rooms***.

Different causes, different fixes, different urgency. So a **control** was run: the same 200
sessions, the same command cadence, but every command local to the caller — `look`, `stats`,
`inventory`, nothing broadcast.

| 200 sessions | pulses kept | mean pulse | over 25 ms | commands | CPU |
|---|---|---|---|---|---|
| Crowded — 3 rooms, all moving | 70.9% | 223 ms | 48.7% | 83/s | 140–178% |
| Spread — nothing broadcast | 99.8% | 3.96 ms | 3.0% | 91/s | 2–7% |

The control handled **more** commands on a **twenty-fifth** of the CPU.

That one comparison relocated the entire investigation. Holding sessions is nearly free; the cost
is *occupants per room*. Without the control, the obvious next step would have been optimising
session handling, connection management, or serialisation — all of which were already fine.

> **Rule 5 — Before optimising, run the experiment that isolates the variable.**
> A single failing benchmark tells you that something is wrong. A control tells you *what*.

---

## 4. The diagnosis, without a profiler

The scaling curve pointed straight at it. On the crowded plan, doubling the sessions roughly
quadrupled the cost:

| sessions | pulses kept | mean | over 25 ms |
|---|---|---|---|
| 4 | 100% | 0.15 ms | 0% |
| 100 | 100% | 17.9 ms | 34.2% |
| 200 | 70.9% | 223 ms | 48.7% |

`4× cost for 2× input` is the signature of a quadratic, and it says where to look: something that
iterates the occupants, inside something else that iterates the occupants.

It was in plain sight once you knew the shape to look for:

```csharp
foreach (var viewer in viewers)
{
    var map = _layout.BuildMap(room, occupants, mobs, roomItems, viewer);   // ← per viewer
    viewer.Send(new OutboundEvent(EventTypes.Map, ...));
    viewer.Send(new OutboundEvent(EventTypes.Contents, BuildContents(..., viewer, ...)));
}
```

`BuildMap` sorts the occupants, sorts the mobs, walks the room's grid, and hashes every entity into
a cell — **once per viewer**. The only thing in all of that which depends on `viewer` is two
ternaries on a single entity:

```csharp
var isViewer = actor.CharacterId == viewer.CharacterId;
entities.Add(new MapEntity(actor.EntityId,
    isViewer ? "@" : actor.Icon, x, y,
    isViewer ? "you" : actor.Name, "player"));
```

The whole room was rebuilt N times to change one icon and one label.

> **Rule 6 — Let the scaling curve pick the suspect, then read the code.**
> A profiler would have confirmed this and taken longer. Sampled attribution tells you where time
> goes; the growth exponent tells you *why*, which is the part you can act on.

A second instance of the same defect sat next to it — a `ToLowerInvariant()` **inside** a `Select`
lambda, so it allocated a lowercased copy of the viewer's name once per entry per viewer. N²
strings for a value that never changed.

> **Rule 7 — A loop-invariant expression inside a lambda is invisible.**
> Nobody would write `for (…) { var x = expensive(); }` and not notice. The same code inside
> `.Select(e => … expensive() …)` reads as a comparison and hides the repetition.

---

## 5. Four fixes, in order of value

### Fix 1 — Hoist the invariant work out of the per-viewer loop

Placement is a pure function of `(room, entity, occupancy)`. Nothing about it depends on who is
looking. So the room is laid out **once** and each viewer's copy is patched.

**Result: the loop stopped missing pulses.** 70.9% → 99.9% of the schedule kept, and total time
inside pulse handlers fell from 114 s to 55 s of a 180 s window.

An important detail about how this *looked*:

| | before | after |
|---|---|---|
| mean | 223 ms | 76.8 ms |
| p50 | 16.1 ms | **36.4 ms** ← worse |
| over 25 ms | 48.7% | **53.7%** ← worse |

**Two headline metrics got worse while the system got twice as fast.** When a loop overruns,
`PeriodicTimer` coalesces ticks — the cheap pulses never happen as separate events, so the
surviving sample was a mix of enormous pulses and near-empty ones, and the median sat low. Once
every scheduled pulse fires, the same work spreads across more of them: the median rises, the mean
falls, the total halves.

> **Rule 8 — A percentile computed over a changing population is not a comparison.**
> Fixing the skipping changed *which pulses existed*. Use a metric with a fixed denominator —
> here, total handler time over a fixed wall-clock window.

### Fix 2 — Delete the operand, not the allocation

The `ToLowerInvariant()` was removed by comparing case-insensitively instead:

```csharp
string.Equals(entry.Keyword, viewer.Name, StringComparison.OrdinalIgnoreCase)
```

`Keyword` is already the lowercased name, so this decides exactly the same entries and allocates
nothing.

> **Rule 9 — The cheapest allocation is the one you stop needing.**
> The reflex is to make the allocation cheaper — a span, a pool, a cache. Ask first whether the
> value needs to exist at all.

### Fix 3 — Move the personalisation to the client

Fix 1 removed the expensive constant but left a cheaper quadratic: an N-element array copied per
viewer, to patch one element. That term cannot be removed while the server personalises at all.

But **marking the viewer is a rendering decision**, and the client is the only party that knows
whose screen it is. So the server now sends one immutable payload and the same instance goes into
every occupant's channel by reference; the client draws its own `@`.

**Result:** mean 76.8 → **34.6 ms**, and p99 came out of the unbounded top bucket for the first
time at 237.8 ms. Total handler time: 55 s → 25 s.

> **Rule 10 — Ask which side of the wire a piece of work belongs on.**
> Per-recipient customisation forces per-recipient work. Data that is identical for everybody can
> be built once, shared by reference, and — if it is immutable — sent to a thousand consumers at
> the cost of one.

This is safe by construction rather than by care: `OutboundEvent` and every payload beneath it are
immutable records, and a session does nothing with what it is handed but serialise it.

### Fix 4 — Coalesce refreshes per tick

With the per-viewer work gone, one term remained: the number of *refreshes* rose with the number of
movers, while the recipients of each rose with the occupancy. Twenty people walking through a room
in one 250 ms tick caused twenty rebuilds and twenty broadcasts — of which **nineteen were
superseded before anyone could perceive them.**

So `RefreshRoom` became `MarkRoomChanged`, and the game loop flushes the deduplicated set once at
the end of each pulse.

> **Rule 11 — In a tick-based system, ask what is observable at the tick boundary.**
> Intermediate states within a frame are not a correctness obligation. They are wasted work, and
> deduplicating them is free.

Two details that mattered:

- **Thread safety.** Mob AI and the spawner are launched fire-and-forget onto the thread pool, so
  they mark rooms from off the loop. A mark landing after the flush is simply sent next pulse — a
  quarter second later, which is already true of everything those systems do.
- **The test harness had to learn the same rhythm.** `WorldHarness.Execute` dispatches commands
  without a pulse, so it now flushes after each one — otherwise tests would assert against a
  dispatch path the game never takes.

---

## 6. The numbers, end to end

200 sessions, three rooms, 180-second measured window, server pinned at 2 CPU / 2 GB.

| | baseline | +hoist & operand | +client marking | +coalescing |
|---|---|---|---|---|
| pulses kept | 70.9% | 99.9% | 100.0% | *(see §7)* |
| mean pulse | 223 ms | 76.8 ms | 34.6 ms | |
| p50 | 16.1 ms | 36.4 ms | 24.8 ms | |
| p99 | *>250, unbounded* | *>250, unbounded* | 237.8 ms | |
| over 25 ms | 48.7% | 53.7% | 49.7% | |
| **handler time / 180 s** | **114 s** | **55 s** | **25 s** | |

---

## 7. What this did not fix, and the honest reading

Even at the end, roughly half of pulses exceed the 25 ms budget — a number that barely moved
throughout. That looks like failure and is worth reading carefully.

By the time p50 was 24.8 ms, **the budget line ran through the middle of the distribution.** Half
the pulses land just over it and will keep doing so however much is shaved, until the typical pulse
drops well below 25 ms. The early 48.7% was a *tail* of enormous pulses dragging a 223 ms mean; the
later 49.7% is a tight cluster around the line. Same percentage, completely different system.

Which reading is right depends on what the budget is *for*. The 25 ms is "10% of the 250 ms pulse"
— a proxy for *the loop is not at risk*. By the direct measures of that, the loop is healthy: every
scheduled pulse fires, and it is busy a fraction of the time. The proxy was calibrated from a
0.21 ms single-player idle measurement and never revisited against a full world.

§11 already reached that conclusion once, for command latency: verifying a target can reveal the
target was wrong.

> **Rule 12 — When a metric stops moving, check whether it is still measuring what you think.**
> A threshold set against an unrepresentative baseline can outlive its usefulness and start
> reporting failure at a system that is fine.

And the standing caveat on all of it: **the crowded plan is a deliberate worst case.** Real players
spread across 236 rooms, not three. These numbers are not a prediction of what 200 players cost.
They are the shape of a launch, a world event, or everyone standing in the starting room — which
are real, and which is why the worst case was worth measuring.

---

## 7b. A coda: the same method, a different bug

The apparatus built for §11's session target found a second defect on the way, and it is worth
reading because **none of the rules above changed — only the subject did.**

The server could not tell a departed player from an idle one for over sixteen minutes. Link-dead is
signalled when a write to the SSE socket fails, and a small write into a kernel send buffer succeeds
for a very long time after the peer stops acknowledging. The fifteen-second heartbeat the server
already sent looks exactly like a liveness check and is not one: it proves the *server* is alive.

| the client goes away by… | detection |
|---|---|
| its container being killed — a reset is sent | ~7 s |
| its network being severed — silence | **~16.7 min** |
| the same, through nginx | ~16.4 min |

Four things in that investigation generalise:

**An anecdote is not a measurement, in either direction.** The first report of this was "about five
minutes", noticed incidentally during another run. It was wrong, and it was wrong *optimistically*
— the truth was three times worse. A number nobody timed is a hypothesis wearing a number's clothes.

**Test the failure mode you mean, not the one that is easy to produce.** `docker kill` tears down a
container's network, which sends a reset, which the server sees in seven seconds. That is a real
scenario and it was already fine. The scenario that mattered — a phone entering a tunnel — sends
nothing at all, and reproducing it needed the container kept alive with its network severed. Two
experiments that look the same differ by three orders of magnitude.

**The obvious mitigation was measured before it was trusted.** nginx sets `proxy_send_timeout 60s`
and it was reasonable to expect that would bound the problem. It did not: the proxy has the identical
blind spot, its own writes succeed into its own buffer, and it changed 1,093 seconds to 1,072. Had
that been assumed rather than measured, the conclusion would have been "fine in production" and the
defect would have shipped.

**A test that cannot fail proves nothing, and one that measures the wrong thing is worse.** Twice in
this investigation a run reported a plausible number for the wrong reason: `/metrics` through nginx
returned the SPA's `index.html` — HTTP 200, no pulse samples — and the load apparatus initially sent
no heartbeats, which would have exercised the old-client fallback and shown no improvement at all.
The first was caught because the probe refuses to parse an empty histogram into a healthy zero. The
second was caught by asking what the apparatus was actually doing before believing its output.

> **Rule 13 — Reproduce the failure you mean, not the one that is convenient.**
> **Rule 14 — Measure the mitigation before you rely on it, especially when it is obvious.**

## 8. The rules, collected

1. A benchmark that does not reproduce the shape of production measures nothing.
2. A capacity target without a resource budget is not a target.
3. Measure at the layer where the work happens, not the layer that is easy to reach.
4. Ask what your metric cannot see.
5. Before optimising, run the experiment that isolates the variable.
6. Let the scaling curve pick the suspect, then read the code.
7. A loop-invariant expression inside a lambda is invisible.
8. A percentile computed over a changing population is not a comparison.
9. The cheapest allocation is the one you stop needing.
10. Ask which side of the wire a piece of work belongs on.
11. In a tick-based system, ask what is observable at the tick boundary.
12. When a metric stops moving, check whether it is still measuring what you think.
13. Reproduce the failure you mean, not the one that is convenient.
14. Measure the mitigation before you rely on it, especially when it is obvious.

## 9. Where the code is

| Piece | |
|---|---|
| [RoomLayoutService.cs](../src/Muwbta.Engine/Presentation/RoomLayoutService.cs) | Viewer-agnostic map build; the span and allocation work |
| [PlayerView.cs](../src/Muwbta.Engine/Presentation/PlayerView.cs) | `MarkRoomChanged` / `FlushChangedRooms`, shared payloads |
| [RoomBroadcast.cs](../src/Muwbta.Engine/World/RoomBroadcast.cs) | Build-once-send-many for room prose |
| [self.ts](../client/src/state/self.ts) | The client half of the protocol change |
| [RoomRefreshCoalescingTests.cs](../tests/Muwbta.Engine.Tests/Presentation/RoomRefreshCoalescingTests.cs) | The invariants, pinned |
| [SessionLivenessMonitor.cs](../src/Muwbta.Server/Game/SessionLivenessMonitor.cs) | The coda: reaping clients that stopped saying they were there |
| [PLAYTEST.md](PLAYTEST.md) | The load harness that produced every number here |
