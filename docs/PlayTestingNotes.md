# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

- **`stat` and `stats`: done, and they never overlapped — they collided.** `stats` is a player's
  combat sheet about themselves, all of it derived through the same `EquipmentResolver` combat
  uses: damage range split into dice and Might bonus, swing speed, attack rating, the number an
  attacker has to beat, armour mitigation. `stat` was an **admin inspector** for any character, mob
  or item, printing raw stored state. Only the level and the gold were ever the same number.

  The problem was the names. `stat` demanded four characters and `stats` five, so **the admin verb
  owned the shorter prefix** — and `RequireAdmin` answers as though the verb were unknown, on
  purpose, so nobody can find admin verbs by fishing. A player typing the obvious abbreviation of
  their own screen got *"'stat' is not something you can do. Try 'help'."*

  Not merged: one verb would either change its output with your role, or accept a target for
  everyone and refuse players — the first is invisible, the second is inexplicable. **The admin one
  is `inspect` now** (three characters, `ins`), which is also what it does, and `stats` drops to
  four so `stat` reaches the sheet a player meant. `ins` is safe — `inventory` is registered first
  and holds every prefix up to `inve`.

  `VerbReachabilityTests` used to *assert* the old arrangement as correct. Being reachable is not
  enough on its own: a prefix has to reach the command the person typing it meant, and no
  arrangement of lengths makes a shared name do that.

- Should track changes in a changelog or use the github release functionality?

- ability cooldowns: **done**. Retuned to the 2s combat beat (§4.5), and the pending-cooldown
  display is built. Reworked since: the bar shows **only what is cooling**, as a name and a fill
  that drains, and the row is absent entirely when nothing is. Listing every ability the character
  had did not fit on screen past the mid-twenties, so the one thing worth seeing at a glance was
  behind a scrollbar. The chips no longer fill the input — nothing is clickable while an ability is
  ready, because a ready ability is not drawn at all.

- **discoverability, done with the above.** A level-up now names what it granted, one line per
  ability and per passive, and every intermediate level of a multi-level jump. Without it a new
  ability arrived as a row appearing in a panel that no longer exists, and a player had no way to
  learn the verb short of typing `abilities` on speculation.

- **the UX evaluation is done.** All eight findings in [UX.md](UX.md) are marked FIXED there,
  including the live defect (four Remove buttons asking for a CSS class nobody wrote) and the
  Follow my character checkbox.

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

- ~~**The world has nothing left to fight at level 6.**~~ Superseded: that was the two-zone
  Aldenmoor. The Reaches cover 1–50 and every level has a full-value target on paper — see the
  "authored and never played" entry below, which is now the live version of this concern.

- **Power levelling is now possible again**, and worth a decision rather than a discovery. With the
  party floor gone, a level 9 in a level 50 zone earns a full share of a level 50 mob. That is the
  same rule that makes the level 19 case right, so it cannot be fixed by putting the floor back —
  the honest lever is capping how far *above* your level a mob can pay.

- **Gatetown: done, all three.** The greeting and the starting room are both `GameConfiguration`
  now — data, not code — and the `the-reaches` configuration is imported and **activated**. Neither
  `GameLoop` nor `Program.cs` names a world any more, and `EngineContracts.cs` keeps its Millbrook
  default for development and tests. Aldenmoor is **not** deleted; `WORLD.md` §10.2 has why leaving
  it costs nothing and buys a builder sandbox. `bind` should work in Gatetown now that the zone's
  `respawn` flag is live — still worth typing once to confirm.

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

- **The builder was offering ten stat keys nothing reads.** Found from `ossara-leather-cap`: its
  `armor = 3` showed under "carried through unchanged" while three inert boxes sat above it
  labelled as armour. The armour rework retired `armorFlat`, `armorPercent` and `armorMultiplier`
  for a single `armor` rating and neither the item nor the mob editor was updated; the three vital
  multipliers (`healthMultiplier`, `focusMultiplier`, `staminaMultiplier`) have never been read by
  anything, in any version; and the ability editor offered `armorFlat` for `buff.defense` and
  `debuff.expose` where `DefenseEffect` reads `mitigation`, so the absorb half of every guard
  authored in the browser was silently zero.

  **The shipped content was never wrong** — `AbilityCatalogue` writes `mitigation`, and the imported
  items carry `armor`. Only the editors were stale, and only values typed *in the browser* were
  lost.

  `tools/check-builder-keys.py` now reads both languages and fails on a key no engine source names.
  Verified against the pre-fix tree: it reports all ten. It is deliberately one-directional — a key
  the engine reads and no form offers is a missing feature, a key the form offers and nothing reads
  is a lie, and only the second is silent.

- **`itemPower` is recorded and never read.**
  [ItemSpawner.cs:51](../src/DikuWeb.Engine/Spawning/ItemSpawner.cs#L51) copies `BaseStats`
  verbatim; only `ItemValue` is resolved, into the price. The dial is snapshotted into
  `SpawnMultipliers` and then nothing reads it. `WORLD.md` §7.3 assumed the mob trick transferred —
  author one baseline set, let the realm dial place it — and it does not, so every realm's set is
  authored at final numbers instead. Implement it or delete it; carrying a dial that reads as
  configured and does nothing is the failure mode `Engine__StartingRoom` already demonstrated.

- **Room terrain: done.** All 224 rooms carry a 21×9 grid (`WORLD.md` §10.1). Generated per zone
  kind and seeded from the room key, so regeneration is byte-identical and a re-import is safe to
  repeat. `check-bundle.py` now refuses ragged grids, characters missing from a legend, and rooms
  with under 40 cells to stand on — the last is the silent one, since occupants are simply not
  drawn when a room has nowhere open.

  **Re-import all six bundles to see it.** Import upserts rooms, so existing rooms pick the terrain
  up; nothing needs deleting first.

- **The Reaches are authored and have never been played.** 224 rooms, 18 zones, 67 mobs, 15 quests
  in `content/`. Every zone's effective levels were checked against its band and every level 1–50
  has a full-value target, but that is arithmetic, not play. Specific things to watch on the first
  run through:
  - **The four gates are the only progression lock.** `attuned.grask`, `attuned.azhen`,
    `attuned.nemhal`, `attuned.the-unlit`, each granted by the last quest of an act. If a chain is
    unfinishable the realm behind it is unreachable, so walk each act end to end before anything
    else.
  - **Mob health and damage were set from the chassis shapes, not from fights.** The armour and
    to-hit rework landed the same day; nothing in `content/` has been fought with the new curve.
  - **`the-unlit.the-crossing` is flat at effective 46** (`WORLD.md` §10.4) — in band, but every
    mob in the zone fights at the same number.
  - **Quest XP was scaled off `XpProgression`, not measured.** Act V pays 420,000 for one turn-in;
    that is a guess about what the last two levels should cost.

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


  
  - **wander cadence: done, and it was content rather than code.** The stagger already existed —
    `MobAiSystem.ScheduleWander` draws each mob's next move from ±50% of its authored interval, per
    mob, precisely so two spawned into one room stop moving in lockstep. What made it read as spam
    was the number: **all 72 authored intervals were 24 pulses — six seconds**, one value
    copy-pasted across all five realm generators, and only four templates actually wander. Four
    terrace crows in a three-room pocket at six seconds each, with two lines per move (leaves /
    arrives), is a line every second or two.

    | Template | Was | Now |
    |---|---|---|
    | a terrace crow | 6s | **60s** (240 pulses) |
    | a brass flitter | 6s | **90s** (360 pulses) |
    | a pier gull | 6s | **45s** (180 pulses) |
    | a vigil moth | 6s | **18s** (72 pulses) |

    The spread still applies on top, so a crow moves every 30–90s and the moth every 9–27s. Re-import
    to pick it up; mobs already standing do **not** need respawning, because `ScheduleWander` reads
    the interval off the live template cache on every draw.

  - **`mob(...)` in the generators now takes `wander=`**, defaulting to 24 and inert unless the
    behaviour says wanders. The default is what produced this bug, so a wandering mob is expected to
    override it — worth a glance whenever a new one is authored.
  - **helping in combat: done, and the premise was half wrong.** The second attacker *was* hitting
    the same mob — `NameMatch.Best` keeps the earlier candidate on a tie and `MoveMob` appends, so a
    crow that wanders in is always last and cannot steal the tie. Pinned by
    `DuplicateTargetTests`. What was actually broken is that **nothing on screen could tell you
    that**: two crows printed the same line, so a right answer and a wrong one looked identical.

    Shipped as three parts (`PLAN.md` §4.16): a mob is labelled `a terrace crow (2)` **only** when
    the room holds another of the same displayed name; `attack crow 2` reaches the one so labelled;
    and `assist <player>` (`as`) attacks whatever that player is attacking, bypassing the name search
    entirely. The ordinal is positional — when (1) dies, (2) becomes (1) — which is why `assist`
    exists: the number is for reading, naming a person is for aiming.

    **Not done, and deliberately:** the default was *not* changed to prefer a mob out of combat.
    With `assist` in place the uncontested-first rule would make helping *harder* — the common case
    becomes "join the fight", and a default that walks away from it pulls a second mob every time
    somebody types the plain verb. Worth revisiting only if pulling deliberately turns out to be the
    commoner intent in play.

  - **`attack` still refuses to switch targets mid-fight** — *"You're already in combat!"* — and
    `assist` now inherits that rule for consistency. Untested against play: if mis-targeting in a
    group turns out to be common, the fix is relaxing it for both verbs at once, not for one.

  - **An aggressive mob always jumps the same player.** `MobAiSystem.TryAggress` takes
    `occupants.FirstOrDefault()`, so who gets attacked is decided by room-list order rather than by
    anything about the party. Found while investigating the above; not fixed.

  - **quest items were named by key: done, and there were three of them.** Reported as
    `You don't have enough ossara-fallen-marker.`; the other two were the **progress** line
    (`Progress: 1/4 ossara-fallen-marker`, read every time anyone checks a quest) and the reward
    listing. The earlier `DisplayName` sweep could not have caught these — it fixed every site with
    an *instance* to ask, and a quest names its item by key precisely because the player may be
    holding none of them and still has to be told what to find. Now `Progress: 1/4 — a fallen road
    marker`, and the short turn-in says the numbers: `You need a fallen road marker (x4). You have
    1.` The `(xN)` shape is the pack listing's (§4.14); no pluralisation, because item names carry
    their own article and "3 a fallen road markers" is what naive pluralising produces.

    Two other key-printing sites are deliberately left: `AbilityCommands:358` and
    `ShopCommands:104` both fire when the template is *missing*, so the key is the only thing there
    is to say.

- **autofollow: done** (`PLAN.md` §4.17). `autofollow <player>`, four characters to `auto`, group
  only. Naming the same person again turns it off; bare `autofollow` also turns it off.

  - **Directional moves only**, and a non-directional one — recall, portal, `goto`, death respawn —
    **ends** every follow pointed at that character rather than being ignored. The break lives in
    `WorldState.Move` with breaking as the *default*, so a relocation added later gets it without
    knowing the feature exists; `walked: true` is passed by exactly one caller.
  - **Circles resolved both ways.** Following somebody who follows you says so; a longer ring
    (A→B→C→A) is walked and refused too. Propagation still carries a visited set, because the chain
    can be re-pointed between steps.
  - **A refused step ends the follow and says why.** The follower is asked the same questions the
    mover was — including the exit gate, deliberately, since skipping it would walk anybody past any
    lock.
  - Chains work: C follows B follows A moves all three.

  **Untested in play**, and the two things to watch: whether the per-follower room refreshes are
  noticeable when a party of five moves, and whether "a step it cannot take ends it" is too strict
  in practice — a party crossing a zone with one gated door loses everyone behind it at once.


  - **light: done, and the flag had never been read at all.** `dark` was registered in Phase 5 with
    the summary *"description withheld without a light source"* and nothing anywhere consulted it —
    so the four zones authored dark (**the Owing, Thessivar, Keshvaun, the Regard**, an act each in
    Grask, Azhen, Nemhal and the Unlit) rendered exactly like every other room. The missing items
    were the visible half of a feature that did not exist.

    `PLAN.md` §4.18 has the rules. In short: `isLightSource` is a column on the item template beside
    `isLore` and `isNoDrop`; **any equipment slot counts**, so a helm or a pendant works; **worn or
    wielded only**, because a lamp in the pack is a lamp you have not taken out; and **light belongs
    to the room**, so one lantern lights it for a party of six rather than five people each buying
    one. Mobs carry none — a lit room whose light is standing in the corner waiting to be killed is
    a light you cannot take with you.

    The dark takes the room's **title as well as its description** (the phone header draws from the
    title), every occupant, mob and item, and everything on the map — which keeps its dimensions so
    the panel does not jump. **Exits survive**, deliberately: walking is the only way out, and on a
    phone the exit pad is that list.

    | Item | Sold by | Slot | Cost |
    |---|---|---|---|
    | a pitch torch | Gatetown trader and provisioner, Grask provisioner | off hand | 6 |
    | a hooded pit lamp | Grask outfitter | trinket | 48 |

    **The torch already existed** — `slot: null`, so unequippable, so useless. It now takes the off
    hand, which is the whole cost of light; the pit lamp is what you buy to get that hand back.
    Its description promised *"burns about an hour"* and nothing models fuel, so that line is gone
    rather than left as a claim a player can catch the game out on.

    **Re-import all six bundles**: `formatVersion` is now **9**, and a v8 bundle carrying a lantern
    would import a lantern that does nothing.

    **Not done, deliberately:** targeting is untouched — `attack rat` still finds the rat, because
    the name search reads the world rather than the frame. Fighting blind is the classic behaviour.
    Fuel is not modelled either; a torch does not burn down.

  - **Found on the way, and worse than the thing that found it: `WorldWriter` never persisted any
    of an item's flags.** The round-trip test written for the light source failed, and the cause was
    that neither the create nor the update branch of `UpsertItemTemplate` set `isLore`, `isNoDrop`
    or `paths` — since the `ItemRestrictions` migration added them. The API accepted them, the
    applier put them in the running cache, `/validate` was happy, the exporter wrote them and the
    importer read them. Nothing ever wrote a row.

    So **every restriction authored in the builder or landed by an import survived exactly as long
    as the process did**, and reverted silently on the next restart when the cache reloaded from a
    database that had never been told. Twenty items in `content/` carry one. The same defect class
    as the stale editor keys: a field that reads as configured everywhere a builder can see, and is
    connected to nothing at the far end.

    All four are written now, and `An_item_templates_flags_survive_a_round_trip` pins them together
    — they failed as a group and they are fixed as a group. A sweep of every other `Upsert*` case
    against its change record found no second instance; `UpsertGameConfiguration.Live` is untouched
    on purpose and says so.

    **Re-import to repair**: the rows in a running database are still wrong, because nothing has
    written them yet.

  - **Wielding a shield or a torch no longer blames your training.** `wield` said *"you've not the
    training to strike with it"* for anything in the off hand, but an item with no declared attack
    delay never swings however trained you are — so the message named a fix that does not exist.
    The `stats` screen has always said the right thing one screen over. Found because the torch is
    an off-hand item and every player who buys one now meets it.

  - **caster focus: done.** Adept and Hallow recover focus at twice the rate, standing, resting and
    sleeping alike. All three vitals shared one percentage, so the two Paths that spend focus to do
    anything got it back at the rate of the two that keep it as a small reserve — an Adept's empty
    bar was the better part of an hour on their feet. Only focus moves. `RegenCalculator` takes the
    Path rather than defaulting it, because a caster handed the martial rate is silent.

  - **group vitals: done.** A `party` frame carries every member's health, focus and stamina, their
    level and Path, who leads, whether they are in your room, and whether they are link-dead; the
    client draws it under your own meters, one compact row each, absent entirely when ungrouped.
    `group` already printed health, but only when typed, and the number you need is the one you did
    not ask for.

    Compared rather than pushed, so joining, leaving, being kicked, walking off, dropping link and
    simply taking a hit are all covered without any of them knowing the panel exists.

    **Untested in play:** whether three six-cell bars per member is readable at a glance on a phone
    with five in the group, and whether *elsewhere* is the right thing to say about somebody one
    room away versus one realm away — it does not currently distinguish.