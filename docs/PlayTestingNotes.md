# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

- Should track changes in a changelog or use the github release functionality?

- ability cooldowns: **done**. Retuned to the 2s combat beat (§4.5), and the pending-cooldown
  display is built — an ability bar above the input, greyed with a countdown while cooling.

- the UX evaluation is written up in [UX.md](UX.md) — eight findings, one of them a live defect
  (four Remove buttons that ask for a CSS class nobody wrote). The Follow my character checkbox is
  gone. The rest is a fix list at the end of that document, not yet done.

- **done**: all three. An ability carries a list of effects now (§4.5), and two new executors —
  `buff.defense` / `debuff.expose` for the to-hit and mitigation dials, and `buff.max-health`,
  which grants the health with the ceiling on first cast only and clamps back under it when it
  expires. `warden.last-stand` is authored as max-health plus guard rather than as a heal.
  The defence effect is split in two rather than taking a signed number, because the validator
  refused the first version: one executor covering both directions had to declare itself harmful,
  which made every defensive ability using it mixed-direction.

- **done**: all three, plus `consider`. A kill is now worth what it cost you, decided by exactly
  two numbers (§4.7): your level, and the level the mob *fights* at.
  `MobLevel.Effective` resolves the second one at spawn, beside `ResolvedXp` —
  `level × strength × √(health × damage)`, floored at the zone's `MinLevel`. The exponent is the
  one the XP curve already uses, so four times the combat power is twice the level. One level 8
  kobold template is a level 8 nuisance in Millbrook and a level 48 problem in the Deep.
  `XpRelevance` is your side of it: full value at or above your level, nothing below
  `min(level / 2, 30)`, a straight line between.
  **The party floor is gone.** Your level 9 / level 20 / level 19 mob example is exactly right —
  help with a fight you could have taken cannot be worth less than taking it. Everyone present
  splits the pot and each share is scaled by that person's own distance from the mob; gold is an
  even split regardless.
  `consider` reads the same two numbers, so the warning and the reward can no longer disagree —
  they did, at high level: a level 44 mob told a level 50 *"you are much stronger"* and then paid
  77%. Above your level it is untouched. It also stops printing the template key at players.
  `attack` is the verb, with `kill` kept working and out of `help`. The mobile client's Attack
  button sent `attack <target>` to a server that had no such verb, so it has been broken since it
  was written.

- **The world has nothing left to fight at level 6.** Not a bug in the above, its consequence: the
  only mobs authored are levels 1 and 2, both zones leave every multiplier at 1.0, and both declare
  `min_level` 1 — so nothing gets lifted and from level 6 the floor is 3. Needs content, or bands
  and dials that say what the zones are for. `aldenmoor.sunken-crypt` is currently 1–50.

- **Power levelling is now possible again**, and worth a decision rather than a discovery. With the
  party floor gone, a level 9 in a level 50 zone earns a full share of a level 50 mob. That is the
  same rule that makes the level 19 case right, so it cannot be fixed by putting the floor back —
  the honest lever is capping how far *above* your level a mob can pay.

- **When Gatetown is imported, do these three in one pass.** All of them are waiting on the rooms
  existing to point at, and none of them is worth doing before that:

  1. **The login greeting is hardcoded to the retired world.**
     [GameLoop.cs:489](../src/DikuWeb.Engine/GameLoop.cs#L489) sends
     `"Welcome to Aldenmoor, {name}."` to every player on every login, whichever world they are
     actually standing in. Deriving it from the starting room's world is *not* the fix — "Welcome
     to Ossara" is also wrong, because the setting is the Reaches and the realm you begin in is
     incidental. It wants to be a configured greeting.
  2. **Point `Engine__StartingRoom` at `ossara.gatetown.the-gate-yard`.** Configuration, not code
     ([Program.cs:65](../src/DikuWeb.Server/Program.cs#L65)) — the `EngineContracts.cs` default
     stays on Millbrook and stays correct for development and tests. Aldenmoor is **not** deleted;
     see `WORLD.md` §10.2 for why leaving it costs nothing and buys a builder sandbox.
  3. **Bind works again the moment this lands.** `bind` has been refusing everywhere because no
     content sets the `respawn` flag; `ossara.gatetown` sets it at the zone level, so this is the
     first import that makes the verb do anything. Worth actually typing it.

- **Armour and to-hit: done**, and it was worse than the armour question that started it. `armorFlat`
  was all-or-nothing at every level, but the d20 had *already* stopped working on its own: attack
  rating grew at `level/2` against a defence growing at `level/4`, so a player hit on every swing
  from level 15, mobs did from level 30, and by level 50 every landed mob blow was a critical
  because the crit rule read overshoot. `PLAN.md` §4.6 has the new arithmetic. In short: both sides
  carry `level/2` so the die decides again, the needed roll is clamped to 2–20 so neither certainty
  is reachable, a crit is a natural 20, and armour absorbs `armor / (armor + 100)` capped at 75%.
  Items author `armor` and `defense`; the retired keys are gone.

  **What still wants checking with a controller in hand**, none of it blocking:
  - `MobAttackBaseline` is **6** — the constant standing for a mob's competence, since its `level/2`
    now cancels against the defence. It is the single dial that makes every fight in the game
    bloodier or gentler, and it was picked from a spreadsheet rather than from play.
  - **Mob damage scaling: fixed.** It was `1d4 + level/3`, which fell behind badly enough that
    fights got *longer* with level — about 30 landed blows to kill a player at level 1 and 53 at
    level 50 — and whose spread collapsed to 17–20 at level 50, so the dice had stopped mattering.
    A silent mob now rolls `(1 + level/2)` to `(4 + 3·level/2)`, which holds the ratio near 14–25
    blows across the whole range and keeps a d4's spread. The flat `level/3` adder is gone; all the
    scaling is in the dice, and an authored `damage` no longer picks up a hidden bonus.
    Still unmeasured: this targets ~20 landed blows to kill a tier-geared character, which was
    chosen, not observed.
  - The twelve `Guard(...)` values were converted from flat amounts to percentage points by holding
    their relative order, not by measurement.

- **`itemPower` is recorded and never read.**
  [ItemSpawner.cs:51](../src/DikuWeb.Engine/Spawning/ItemSpawner.cs#L51) copies `BaseStats`
  verbatim; only `ItemValue` is resolved, into the price. The dial is snapshotted into
  `SpawnMultipliers` and then nothing reads it. `WORLD.md` §7.3 assumed the mob trick transferred —
  author one baseline set, let the realm dial place it — and it does not, so every realm's set is
  authored at final numbers instead. Implement it or delete it; carrying a dial that reads as
  configured and does nothing is the failure mode `Engine__StartingRoom` already demonstrated.

- **Review solo and group balance for all four Paths, band by band.** Thirty-two abilities were
  added at levels 24–50 (`ABILITIES.md`) against no play data at all: every number in them is a
  first guess, and four Paths times eight new abilities is a lot of first guesses interacting.

  The target the tuning is *for*, so it is not rediscovered per Path:

  - **Ordinary grinding must be soloable, on every Path.** A player should be able to reach the
    next band by fighting the zones their level points at, alone, without a Path being the reason
    it does not work. Hallow and Warden are the two to watch — they are the ones whose new
    abilities buy survival rather than damage, so they clear more slowly and can end up gated on
    finding company rather than on levelling.
  - **The storyline should want a small group, wherever it asks for kills.** Not a raid and not a
    hard gate: the set-piece fights in each act's zone should be the ones you bring two or three
    people to, and the ones that feel *better* with a Hallow buffing beforehand and a Warden
    holding. That is the point at which the group abilities added here should stop being optional.

  Both halves are content dials rather than ability dials — a zone's `strength` and its spawner
  counts decide whether a fight is a solo fight — so this wants doing **after** the Reaches are
  authored and can actually be played, not before. Check it per Path per band, and check the
  Hallow buff window specifically: the maintainable protections are meant to be set before a fight
  and still standing at the end of one, so if a band's fights outlast them the design has drifted.