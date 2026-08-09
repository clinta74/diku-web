# Found bugs

Open findings and the order they get worked in. A bug leaves this file when it has a fix **and** a
test that would have caught it; the story of why it happened moves into `PLAN.md`, which is where
this codebase keeps its reasoning.

`PlayTestingNotes.md` is the inbox — anything noticed while playing goes there. This is the queue.

| # | Bug | Severity | Verified | Status |
|---|-----|----------|----------|--------|
| 1 | A fight your target leaves never releases you | **Blocking** | Unit test | **Fixed** |
| 2 | A mob you are fighting can wander out of the room | **Blocking** | Live transcript | **Fixed** |
| 3 | Nothing tells you your fight ended | Moderate | Live transcript | **Fixed** with #1 |
| 4 | The auth rate limiter is site-wide behind a proxy | Moderate | By inspection | Known, deliberate |
| 5 | "Melee starts after an ability" is unverified live | Coverage gap | — | Blocked on fixtures |

---

## 1. A fight your target leaves never releases you — **blocking**

**Symptom.** Your target leaves the room. You are left `Fighting` for the rest of the session:
every later `kill` is refused with *"You're already in combat!"*, and movement is refused with
*"You can't leave while in combat!"*. There is no way out except logging in again.

**Evidence.** Reproduces in a unit test with no mob AI anywhere near it — move the mob's
`RoomKey` mid-fight, pump, and the character is still `Fighting` with `CurrentTarget` set. Seen
live first, in `combat-basics`: `> kill rat` → two minutes later → `> kill rat` →
*"You're already in combat!"*

**Root cause.** `CombatSystem.RunCombatant` conflates two different departures:

```csharp
var attackerRoom = GetCombatantRoom(world, attackerId);
var targetRoom = GetCombatantRoom(world, targetId);
if (attackerRoom != combat.RoomKey || targetRoom != combat.RoomKey)
{
    combat.RemoveCombatant(attackerId);   // ← removes the attacker either way
    return;
}
```

When the **target** left, this removes the **attacker**. Two things then go wrong at once. The
removal never calls `EndCombatFor`, so the character keeps `CombatState.Fighting` and its
`CurrentTarget` — and because they are no longer in `Combatants`, the end-of-fight sweep that
*would* have called `EndCombatFor` no longer sees them. They are outside the fight and still
marked as being in one.

**Why it survived.** It needs the target to leave, which for a mob only happens through wandering
and for a player only through `flee` — and `flee` cleans up after itself explicitly. Nothing in
the suite moved a combatant mid-fight.

**Fix.** Separate the two cases. An attacker who left is removed *and* released; a target who left
is removed from the fight, leaving the attacker in it for `IsCombatActive` to judge at the end of
the pulse — which, since the head-count fix, correctly finds nobody left to hit and ends it
properly for everybody.

**Tests.** The mob leaves and the player is released and can fight again. The player is the one who
left. A three-way fight where only one of two mobs leaves keeps going. `flee` still works.

---

## 2. A mob you are fighting can wander out of the room — **blocking**

**Symptom.** *"You begin attacking a rat!"* and then, 1.2 seconds later, *"A rat leaves south."*
The fight is over before it started, and the player stands there while rats wander in and out
around them. Combined with #1, they are also stuck forever.

**Evidence.** `combat-basics`, Sunken Crypt, reproducible on most runs — the crypt rats wander
between the Crypt Chapel and the Ossuary continuously.

**Root cause.** `MobAiSystem.ShouldWander` gates on the sentinel flag, on the room's `noMob` flag,
and — one level up, in `Tick` — on being stunned. It never gates on `CombatState`.

**Why it is the same argument as the stun.** The stun guard's own comment says gating only its
swings "would have it strolling out of the room mid-stun, which reads as the stun having done
nothing". A fight is the same claim on a mob's attention, and a mob that strolls out of one reads
as combat having done nothing.

**Fix.** `ShouldWander` returns false while `CombatState == Fighting`. One guard, beside the
existing two, in the method that already answers this question.

**Careful.** `MobAiSystem.cs` currently holds uncommitted work in that exact method — the wander
cadence rewrite. **Land that first**, or take the fix from whoever is holding the file.

**Tests.** A fighting mob does not wander even when its turn has come. It resumes wandering once
the fight ends. A mob that is not fighting is unaffected — the cadence work has its own tests and
must stay green.

---

## 3. Nothing tells you your fight ended — moderate

**Symptom.** When a fight ends because the other party left, the player gets no line about it. They
see *"A rat leaves south."* — an ordinary room-departure message — and must infer that the fight
they were in is over. On the receiving end of #1 they cannot even infer it, because it is not.

**Why it matters beyond politeness.** Every other way a fight ends is narrated: a death says
*"A rat falls."*, fleeing says *"You manage to escape!"*, a refused target explains itself. This is
the one exit with no words on it, which is why #1 was invisible for so long — the silence was
indistinguishable from the bug.

**Fix.** Say so, once, to whoever is left, when the fight ends for this reason. Falls out of #1's
fix naturally, since that is where the case becomes distinguishable in the first place.

---

## 4. The auth rate limiter is site-wide behind a proxy — known, deliberate

`RateLimiting` partitions the auth policy by remote address. Behind nginx every request carries the
proxy's address, so the sign-in budget becomes a single global bucket: one person failing logins in
a loop can lock out the whole site.

Left as-is on purpose and recorded here so it is not rediscovered as a surprise. Honouring
`X-Forwarded-For` safely needs a trusted-proxy list, which this repo does not have and should not
grow casually — a limiter that trusts a spoofable header is worse than a coarse one. `Program.cs`
carries the deployment note; the number is configurable in the meantime.

**Fix when it matters:** a trusted-proxy allowlist in configuration, `ForwardedHeaders` restricted
to it, then partition on the resolved client address.

---

## 5. "Melee starts after an ability" is unverified live — coverage gap

Half of the 2026-08-09 note is guarded only by engine tests. A level-1 Kick one-shots a city rat,
so `ability-opens-a-fight` can never observe the Warden's own weapon starting to swing afterwards —
there is never anything left to swing at, and the starter world contains nothing tougher.

Not a bug. It becomes coverage the moment plans can build their own content, which is the hosted
target and world fixtures (`PLAYTEST.md`, milestone 4).

---

# Work plan

Ordered by what unblocks what. #1 and #2 are separable but they were found together and they
compound, so they shipped together.

### Step 1 — Release a fight whose target left (#1, #3) ✅ **done**

`CombatSystem.RunCombatant` now asks the two questions separately. An attacker who left is removed
*and* released; a target who left is removed, everyone aiming at them forgets them, and the
attacker stays in the fight for `IsCombatActive` to judge — which, since the head-count fix, finds
nobody left to hit and ends it properly for everybody.

`ForgetTarget` clears `Character.CurrentTarget` as well as the fight's own `PlayerTargets`, because
that is the copy `kill` reads and the two disagreeing was the whole failure. And the ending is
narrated — *"You stop fighting a zombie."* — since every other exit from a fight has words on it.

Seven tests, including the reported transcript verbatim and the shapes that must keep working:
the attacker leaving, one of two mobs leaving, and `flee`.

### Step 2 — Stop a fighting mob wandering off (#2) ✅ **done**

One guard at the top of `ShouldWander`, above the cadence work. Three tests: a fighting mob stays,
a released one wanders again — the guard must not be a life sentence — and an idle one is
unaffected.

### Step 3 — Re-run the plan library and clear the flag

`combat-basics` is deliberately left flagged and says so in its own `about:`. After steps 1 and 2 it
should read clean; if it does not, the transcript says why. Remove the KNOWN FAILING paragraph.

```
dotnet run --project tools/DikuWeb.Playtest -- --server http://localhost:5050 \
    --plans tools/DikuWeb.Playtest/plans
```

**Done when:** all six plans read clean except `shopping`, which needs `--admin-user` and says so.

### Step 4 — Hosted target and world fixtures (#5)

`PLAYTEST.md` milestone 4: boot the server in-process against a throwaway Postgres, and let a plan's
`world:` block build its own mobs through the builder API. This is what closes #5 and what makes
every content-dependent plan reproducible rather than a race against a spawner.

It also removes the reason `combat-basics` had to go to the crypt in the first place.

### Step 5 — Have the apparatus tidy up after itself

`deletecharacter <name>` now exists for admins, which gives the apparatus a way to retire the
characters it creates. A plan with an Admin in its cast can already call it; the better shape is
the runner doing it at teardown whenever an admin is available, so the world stops accumulating
`Theronqxbfm`s.

The accounts still remain — there is no delete-account verb and there should not be one lightly —
but characters are what clutter `who` and the room listings. See PLAN.md for the SQL purge.

### Not in this queue

#4 stays open and deliberate until there is a deployment behind a proxy that needs it. Phase 6's
remaining items — world export/import, and backups plus a recovery runbook — are features, not
bugs; they live in `PLAN.md` §8.
