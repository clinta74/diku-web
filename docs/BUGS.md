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
| 5 | "Melee starts after an ability" is unverified live | Coverage gap | — | **Closed** by the test dummy |

**#6–#25 come from the milestone review** of command, room, item, mob and quest, and are tabled in
their own section below rather than here — they share a cause, and reading them as a group is the
point.

---

# The milestone review

Three sweeps over command, room, item, mob and quest, plus a mechanical diff of every `Upsert*`
change record against the code that consumes it. Run before Phase 7, with the Reaches authored and
freshly imported.

**Everything here shares one cause.** Not a crash, not a race: *a field that reads as configured
everywhere a human can see it, and is connected to nothing at the far end.* That had already
happened five times before this review — the retired armour keys, the three vital multipliers, the
`dark` flag, four unpersisted item columns, `itemPower` — and only one of the five was caught by a
test. **The suite is green through every finding below**, because the defect is an absence and
absences do not throw.

The corollary is uncomfortable and worth stating: passing tests are not evidence against this
class. Only going and looking is, which is what #22–#24 are meant to replace.

| # | Finding | Severity | Verified | Status |
|---|-----|----------|----------|--------|
| 6 | Every quest's authored dialogue is unreachable — wrong key vocabulary | **Blocking** | Key histogram vs source | **Fixed** |
| 7 | Quest reward flags never reach the running server | **Blocking** | By inspection | **Fixed** |
| 8 | Equipment slots are never cleared on `drop`/`give`/`sell` — stackable | **Blocking** | By inspection | **Fixed** |
| 9 | `sell` bypasses the guards `drop`, `give` and `destroy` enforce | Moderate | By inspection | **Fixed** |
| 10 | Mob map icons are authored and read by nothing | Moderate | By inspection | **Fixed** |
| 11 | `stats` "Equipped Bonuses" filters out two thirds of the data | Moderate | Content tally | **Fixed** |
| 12 | Three help strings advertise abbreviations that go elsewhere | Moderate | By inspection | **Fixed** |
| 13 | Mob AI narration bypasses `MobLabel` | Moderate | By inspection | **Fixed** |
| 14 | Authoring keys still reaching players in four places | Moderate | By inspection | **Fixed** |
| 15 | `set` reports the value it was given, not the one it wrote | Minor | By inspection | **Fixed** |
| 16 | `quest <name>` shows `0/0` progress for a quest with no fetch step | Minor | By inspection | **Fixed** |
| 17 | Three dials authored and wired to nothing | Latent | Call-site sweep | **Fixed** (two deleted, one built) |
| 18 | `noMob` skips flag inheritance | Latent | By inspection | **Fixed** |
| 19 | `RoomFlag.Phase` is plumbed to the browser and rendered by nothing | Latent | By inspection | **Fixed** |
| 20 | `TryAggress` targets by arrival order; link-dead players soak aggro | Latent | By inspection | **Fixed** |
| 21 | Player targeting is exact-match in combat, prefix elsewhere | Latent | By inspection | **Fixed** |
| 22 | `VerbReachabilityTests` does not test what its comment says | Coverage gap | By inspection | **Fixed** |
| 23 | No guard that a change record reaches the cache and the database | Coverage gap | Found 2 live bugs | **Fixed** |
| 24 | No guard that a content key is one the engine reads | Coverage gap | Would have caught #6 | **Fixed** |
| 25 | Assorted dead code, duplication and inconsistency | Minor | By inspection | **Partly** |

---

## 6. Every quest's authored dialogue is unreachable — **blocking**

**Symptom.** Every quest giver in the game speaks in one of four generic templates — *"I have a job
for you: {summary}"*, *"Still working on {name}?"* — and never in the voice it was written in.

**Evidence.** The engine reads four keys out of `Quest.Dialogue`
([QuestCommands.cs:114,122,346,715](../src/Muwbta.Engine/Commands/QuestCommands.cs#L114)):
`giverOffer`, `giverInProgress`, `giverComplete`, `turninReady`. The key histogram across all six
bundles in `content/`:

```
{'offer': 35, 'progress': 35, 'complete': 35, 'already': 32}
```

**Zero overlap.** All 35 quests, ~137 lines of authored prose, unreachable.

**Root cause.** `Dialogue` is a free `Dictionary<string, string>` and passes through every layer
untouched — importer, writer, applier — so nothing in the round trip is in a position to notice.
The builder editor (`client/src/builder/quests/quests.ts`) and `BuilderApiTests` both agree with the
engine, so the content bundles were authored against a spec that existed nowhere else.

**Fix.** Rename in content: `offer→giverOffer`, `progress→giverInProgress`, `complete→turninReady`,
`already→giverComplete`. Needs a re-import.

**Test.** #24 — a bundle check that refuses a `dialogue` key the engine does not read. This is the
finding that argues for it: a free-form bag is exactly where a vocabulary drifts silently.

---

## 7. Quest reward flags never reach the running server — **blocking**

**Symptom.** Finish the last quest of an act and the gate to the next realm stays shut. Restart the
server and the same character walks through.

**Evidence.** `ApplyUpsertQuest`
([WorldMutationApplier.cs:1245](../src/Muwbta.Engine/Mutations/WorldMutationApplier.cs#L1245))
builds the cached `Quest` from 18 fields. `RewardFlagKey` is not one of them. Every other layer
carries it — the domain object, `QuestConfiguration`, `UpsertQuest`, `BuilderEndpoints`,
`WorldBundle`, the exporter, the importer, and `WorldWriter`, which writes the row correctly.

So the **database is right and the live cache is wrong**, and
[QuestCommands.cs:809](../src/Muwbta.Engine/Commands/QuestCommands.cs#L809) reads null and grants
nothing. A restart reloads the cache from the database and it works.

**Blast radius.** `content/README.md` calls the four attunement flags *"the only progression lock"*
in the game. This is the exact path used to import the world into a running server.

**Why it survived.** `WorldHarness` builds `Quest` objects directly and never goes through the
applier, so no test drives the live path. `RewardFlagKey` appears in no test file at all.

**Fix.** One line. **Test.** One that goes through `WorldMutationApplier`, plus #23.

---

## 8. Equipment slots are never cleared on `drop`, `give` or `sell` — **blocking**

**Symptom.** A character can wear an unbounded number of items in one slot and gets the armour of
all of them.

**Evidence.** [WorldState.cs:517](../src/Muwbta.Engine/World/WorldState.cs#L517) (`PickUpItem`) and
`:532` (`DropItem`) both change ownership and leave `EquippedSlot` untouched. So:

1. `wear ring` — `EquippedSlot = Trinket`.
2. `drop ring` — still `Trinket`, but `InventoryOf` no longer sees it.
3. `wear ring2` — the occupied-slot check scans `InventoryOf`, finds the slot free, equips.
4. `get ring` — ownership restored, `EquippedSlot` still `Trinket`.

Two items now report the same slot, and `EquipmentResolver.ResolveDefenderStats` sums `armor` and
`defense` across every one of them with no per-slot dedup. The same trick stacks `damageMultiplier`
on `MainHand`.

**Second effect.** An item dropped while worn is picked up by another character *already equipped*,
bypassing `TryEquip` — the only place `ItemRules.RefusePath` is consulted. Today's content survives
by coincidence: every Path-restricted item is also `isNoDrop`, so `drop` refuses first. That is an
authoring accident, not a rule.

**Fix.** Clear `EquippedSlot` in both primitives, *and* refuse in the commands — the resolver must
never be able to see two items in one slot whichever path put them there.

**Test.** The four-step sequence above, asserting the resolver sees one item in the slot.

---

## 9. `sell` bypasses the guards the other three exits enforce

`Sell` resolves against `InventoryOf`, which returns equipped items too, and its only guard is
`IsQuestItem` ([ShopCommands.cs:263](../src/Muwbta.Engine/Commands/ShopCommands.cs#L263)). So you
can **sell the weapon in your hand** — `destroy` refuses this explicitly — and **sell a `noDrop`
item**, whose refusal in `drop` tells the player that destroying it is the only sanctioned way to be
rid of it. The shop is an unsanctioned way that also pays.

---

## 10. Mob map icons are authored and read by nothing

`MobTemplate.Icon` is `required`, persisted, exposed in the builder, and authored across all 68
templates on a deliberate scheme — `r` vermin, `c` flyers, `d` canines, `@` named NPCs. **`Mob` has
no `Icon` property at all**, so unlike `ItemSpawner`, nothing copies it at spawn, and both render
paths use `DisplayName[0]`
([RoomLayoutService.cs:126](../src/Muwbta.Engine/Presentation/RoomLayoutService.cs#L126),
[PlayerView.cs:513](../src/Muwbta.Engine/Presentation/PlayerView.cs#L513)).

Because nearly every mob name begins with an article, **the map is a field of lowercase `a`.** Item
icons are read correctly, which is what makes the map look deliberate rather than broken.

---

## 11. `stats` promises equipped bonuses and shows two thirds of nothing

[CommandRegistry.cs:1282](../src/Muwbta.Engine/Commands/CommandRegistry.cs#L1282) prints the
heading unconditionally, then filters entries on `Key.Contains("Multiplier")`. The tally across
`content/`: `armor` ×36, `damageMultiplier` ×35, `bonus` ×29, `defense` ×5. The filter admits one of
the four. Every armour piece and every `bonus` item prints a heading with nothing under it.

`examine` uses the same bag with no filter and is correct — so the builder view is right and only
the player view is wrong, which is why nobody caught it.

---

## 12. Three help strings advertise abbreviations that go somewhere else

`Matches` accepts any prefix and `Find` is first-match-wins over registration order.

| Help says | What happens |
|---|---|
| `examine <item> (x)` | `x` is not a prefix of "examine" and can never match. No `x` alias exists. |
| `remove <item> (r)` | `remove` needs 2 characters, so `r` cannot reach it. `rest` does — **`r dagger` sits the player down**, since `Rest` ignores its argument. |
| `cast <ability> [target] (c)` | `consider` is registered first and takes `c`. `c bolt rat` answers *"You don't see 'bolt rat' here."* |

The `cast`/`consider` collision was already known and commented in `ChannelCommands`; the help text
was never updated. See #22 for the test that should have caught all three.

---

## 13. Mob AI narration bypasses `MobLabel`

Five sites print `template.Name` raw — idle emote, wander leave and arrive, and both aggression
lines ([MobAiSystem.cs:162,316,317,397,403](../src/Muwbta.Engine/Inhabitants/MobAiSystem.cs#L162)).
Every other narration path uses `MobLabel.For`. So in a room with two terrace crows, `look` and the
combat log say *"a terrace crow (2)"* while the AI says *"a terrace crow"* — reintroducing the exact
ambiguity `MobLabel` was written to close, on the lines a player sees most often.

---

## 14. Authoring keys still reaching players

- `bind` prints a raw `RoomKey` — *"You bind your soul to this place:
  aldenmoor.millbrook.north-gate"* — with the room's `Title` in hand.
- `list` prints an item template key to any customer.
- `SpawnerSystem` hand-rolls the `IsNullOrEmpty(Name) ? Key : Name` fallback in three places, two of
  which have a `DisplayName` property that exists precisely to stop that.

---

## 15. `set` reports the value it was given, not the one it wrote

Health, focus and stamina are clamped to their maxima and level is floored at 1, but the report
prints the pre-clamp input and the target is told the same thing. `set kael health 9999` prints
*"health 20 → 9999"*. The method's own doc says the point of reporting both is that *"an admin who
cannot see what it was cannot tell whether they fixed it."*

---

## 16. `quest <name>` shows a progress bar that can never move

`RequiredItemKey` is nullable by design. When it is null the match is never true, so a quest with no
fetch step renders `Progress: 0/0 — something`.

---

## 17. Three dials authored and wired to nothing — latent

| Dial | Authored in | Read by |
|---|---|---|
| `itemPower` | 4 worlds/zones, up to 1.4 | nothing — `ItemSpawner` copies `BaseStats` verbatim |
| `spawnDensity` | 8 zones, 0.6–1.4 | nothing — the sweep refills straight to `TargetCount` |
| `Spawner.RespawnSeconds` | every spawner, default 30 | nothing — the sweep ran every 15s regardless |

All three have a live arm in `Multipliers.Resolve` or a field on the entity, a builder control, a
bundle field, and an exporter entry. `MultiplierType.ItemPower` and `.SpawnDensity` have **zero
production call sites**; the only caller is a unit test asserting the arithmetic of a function
nothing invokes, which is what makes them look alive.

**Decision at the time: delete all three.** Wiring them up changes balance on content nobody has
played, which needs play behind it.

**Revised for `respawnSeconds`, and the revision is the interesting part.** Deleting it surfaced the
question the dead field had been hiding: *how do you make one thing rarer than another?* There was
no answer, and the absence of one is a design hole rather than a tidy codebase. So it is **built**
now, not deleted — default 60 seconds, one replacement per window, instant fill on a cold start
(PLAN.md §4.8). The other two stay deleted: `itemPower` duplicates authoring item stats at final
numbers, and `spawnDensity` duplicates `TargetCount`.

Worth recording plainly: the sweep's 15-second cadence *was* the respawn rate for every mob and item
in the game, which is why a room could be camped indefinitely. That was never a decision anyone
made — it was the default of a field nothing read.

---

## 18. `noMob` skips flag inheritance — latent

Every other flag reader goes through `WorldState.IsFlagSet`, which resolves room → zone → world →
default. `noMob` reads `room.Flags` directly at
[MobAiSystem.cs:198](../src/Muwbta.Engine/Inhabitants/MobAiSystem.cs#L198) and `:291`. A builder
who sets `noMob` on a zone — which the builder offers, and which the flags tab will show as "on,
from zone" — gets a flag the AI ignores. No content sets it, which is why nothing has noticed.

---

## 19. `RoomFlag.Phase` is plumbed to the browser and rendered by nothing — latent

`Phase` exists to be the reader's warning label. It is serialised by the API, typed in the client,
fetched into context, and then dropped by both consumers. So `indoors` — 30 authored rooms, zero
readers, honestly marked `"later"` in the registry — looks identical to `pvp` in the builder.

**This is the field that was supposed to prevent the `dark` defect, and it is itself an instance of
it.** Rendering it is the cheapest structural fix in this document and it inoculates every future
flag.

---

## 20. `TryAggress` targets by arrival order — latent

`occupants.FirstOrDefault()` over a list in strict arrival order, so an aggressive mob always
attacks whoever has stood in the room longest. In severity order:

1. **Link-dead players soak all aggro.** A disconnected character stays in the room and in the
   occupant list for the whole grace window, and neither `ShouldAggress` nor `TryAggress` nor
   `CombatSystem` filters them. A body that walked in first absorbs every aggressive mob in the room
   and can be killed while offline.
2. Tanking is impossible and unimplementable — the opening target is fixed by walk-in order.
3. Every aggressive mob in a room picks the same player, deterministically.

**Limited by**: only the *opening* target is chosen this way. Once engaged, the mob switches to
whoever has done the most damage. Changing the rule is a design decision and is **out of scope**;
filtering link-dead characters is not, and should be split out.

**Correction — item 2 was wrong.** Tanking is built and works. A hate list is a cumulative damage
meter, `CombatSystem.ResolveTargetOf` re-reads `GetTopHater` every round, and `taunt` writes threat
through `Combat.ForceTopHater` — so both routes pull an *engaged* mob, and `taunt` engages, so a
Warden can even taunt-pull. Only the opening target was out of reach, which makes the real complaint
narrower and sharper than what was written: **an add walking into a fight and ignoring the tank
holding it.**

**Fix.** The rule is now the room's threat leader, otherwise at random, with link-dead characters
excluded from eligibility — recorded in full in `PLAN.md` §4.6. Three parts:

- `EligibleTargets` drops link-dead occupants. `ShouldAggress` counts eligible occupants rather than
  all of them, so a room holding nothing but dropped connections starts no fight instead of engaging
  a body and swinging at it for the grace window.
- `PickAggressionTarget` takes the eligible occupant with the highest threat against any one mob in
  the room's fight, above `CombatEngagement.OpeningThreat`, and rolls `IRandomSource` when nobody
  qualifies. The floor and the use of a maximum rather than a sum are both there to stop a bare
  opening seed reading as earned threat — without them the first person a mob rolled onto would
  become the room's leader and every other mob would pile onto them, which is the old determinism
  with a random first step.
- `TryAggress` now goes through `CombatEngagement.Engage`. It was the fourth hand-rolled copy of the
  six steps and had already drifted the way that file's own doc warns about: it called
  `AddToHateList(mobId, targetId, 1)` unconditionally instead of honouring the `HateOf == 0` floor,
  handing a free point of threat to whoever a mob re-aggressed on.

**Deliberately not fixed:** going link-dead *mid-fight*. `CombatSystem` keeps swinging and the
player may die and rebind, because mobs disengaging on a lost connection would make pulling the plug
the safest escape in the game. There is a test asserting it so it does not get tidied into a fix.

**Test.** `AggressionTargetTests` — nine cases, **six of which fail against the arrival-order rule**:
the link-dead body is passed over, a room of only link-dead bodies starts no fight, an add joins on
whoever holds the room, the leader is threat and not arrival order, the seed floor holds in both
directions (a theory over twelve seeds, because "not forced" is the claim and one seed could agree by
luck), an unprovoked mob does not always pick the same player, and being jumped leaves you engaged but
untargeted. Plus `ThreatTests` for `Combat.HighestHateHeldBy` being a maximum and not a sum.

---

## 21. Player targeting is exact-match in combat, prefix everywhere else — latent

`attack`, `assist`, `consider`, `cast <target>` and `autofollow` use exact `string.Equals` on the
name. `tell`, `group invite`, `group kick` and every admin verb use `NameMatch.Best`. So
`tell kae hello` works and `attack kae` does not. `ResolveTarget`'s own doc claims targets are
*"matched the same way every other targeting command matches"*, which is true for mobs and false for
players two lines above.

---

## 22. `VerbReachabilityTests` does not test what its own comment says — coverage gap

The comment is right and important: *"A verb whose own MinLength prefix reaches something else is
half-dead."* The assertion only checks that the prefix resolves to **something**, never that it
resolves to **that verb**. Comparing names would have caught all three of #12.

It also does not parse the `(abbr)` parenthetical out of each `Help` string and check it resolves —
which is the other half of #12, and the half that lets a help text lie.

---

## 23. No guard that a change record reaches both the cache and the database — coverage gap

Every content edit travels as an `Upsert*` record that must be consumed twice: by
`WorldMutationApplier` into the live cache, and by `WorldWriter` into Postgres. Nothing checks that
either consumer reads every field.

**This has now failed twice.** `isLore`/`isNoDrop`/`paths`/`isLightSource` were never written to a
row, for months; `RewardFlagKey` (#7) never reaches the cache. Both were found by running the same
throwaway script by hand.

Make it a test in `tests/Muwbta.Engine.Tests/Architecture/`, beside `CoordinateIsolationTests` —
the established precedent for enforcing a promise by scanning rather than by discipline.

---

## 24. No guard that a content key is one the engine reads — coverage gap

`tools/check-builder-keys.py` asks *"does the engine read this key the form offers?"* and says in
its own docstring that it deliberately does not ask the reverse. There is a third question neither
tool asks: **does the engine read this key the content authors?** That is #6, and #17, and the mob
`roams` key the engine reads and no content sets.

`check-bundle.py` already parses `RoomFlags.cs` and `RoomLayoutService.cs` straight out of the C#
(both now *referenced* rather than parsed, since the checker was ported to C# — see `BundleValidator`)
rather than keeping a copy, so the technique is in the repo — it just has not been pointed at
`dialogue`, `behavior` or `baseStats`.

---

## 25. Dead code, duplication and inconsistency — minor

- **Dead**: `FlagSet.Empty`, `FlagSet.TryGet`, `RoomKey.IsEmpty`, `RoomFlagKind` (a one-member enum
  nothing reads), two `ResolveAttackerStats` overloads, `DamageCalculator.StatsFrom` and
  `DefenderStatsFrom`, `QuestCache.Contains`, `MobBehavior.EmotesOf`. Because one dead overload is
  the only caller that can produce it, the most intricate branch in `EquipmentResolver` — the one
  with the five-line comment — is unreachable in production.
- **Four near-identical word-splitters** with three different contracts: one lowercases, one does
  not, one returns nulls.
- **~20 sites bypass `ItemInstance.DisplayName`**, which exists because the mob half of the same
  fallback *"was written out by hand in a dozen places and missed in two."* An instance with an
  empty name renders **"You take ."** — worse than the key fallback the property guarantees.
- **Refusal styling splits along file boundaries.** `CommandRegistry` and the social verbs style
  nearly every refusal `"bad"`; `CombatCommands`, `ShopCommands`, `QuestCommands` and
  `RestCommands` are mostly bare. A failed `attack` and a failed `drop` look like different kinds of
  event.
- **`BuilderCommands` still holds the mutable statics** that every sibling file documents as a fixed
  test-race bug, and **runs two verbs' work off the loop thread** fire-and-forget, defeating the
  loop's own error boundary.
- `rflag` treats any unrecognised word as "on". `RequiredCount = 0` completes a quest for free.
  `abandon` takes the first unranked substring match and is destructive and unconfirmed. Buy price
  ignores the `itemValue` multiplier that sell price honours. `SpawnMultipliers` records zone-only
  while claiming world × zone, and nothing reads it for items. Three orphaned XML doc comments. A
  duplicated level-up loop.
- ~~Mobs never heal or reset.~~ **Fixed.** `Mob.Disengage` restores a mob that leaves a fight alive,
  and `RegenSystem` now sweeps mobs as well as players — see `PLAN.md` §4.6 and `MobRecoveryTests`.
  Leashing was the half of the deferred question that stays deferred; mobs still do not walk home.

**Done in this pass:** the five zero-caller members, the three word-splitters, all 29 hand-written
`DisplayName` sites, both misplaced doc comments, the two dead branches, `rflag`'s fallthrough, and
the `BuilderCommands` statics.

**Still open, and why.** `EquipmentResolver.ResolveAttackerStats`'s two overloads are called only by
tests, so removing them means rewriting a test file that covers real arithmetic through a different
door. `RoomFlagKind` is a field on the `RoomFlag` record, so removing it reshapes the record for no
behaviour. The rest — refusal styling, `RequiredCount = 0`, `abandon`'s unranked match, the buy/sell
multiplier asymmetry, `SpawnMultipliers`, and the two builder verbs running off the loop thread —
each change what something *does*, and this pass was behaviour-preserving by agreement. Mobs never
healing was on that list and has since been taken off it, as a design decision rather than a tidy-up.

---

## 26. `warden.last-stand` granted 1000 maximum health at level 20 — **fixed**

Found by reading the derived ability descriptions (PLAN.md §4.5), which is the whole argument for
having them: the dial had been sitting in the catalogue in plain sight and nobody had put it beside
its neighbours.

| Ability | Level | `maxHealth` |
|---|---|---|
| Ground and Centre | 32 | 120 |
| Unbreakable | 40 | 200 |
| The Last Wall | 50 | 400 |
| **Last Stand** | **20** | **1000** |

A Warden at 20 does not have anything like 1000 maximum health, so this multiplied the bar several
times over, and it is granted as health as well as ceiling.

**Now 80**, which opens the line rather than dwarfing it. Applied to the catalogue and exported to
the live database with `tools/export-abilities.cs` — see #27 for why that tool exists.

---

## 27. `warden.shield-wall` was a damage buff wearing a defensive name — **fixed**

`buff.damage-up` at 1.4, named "Shield Wall", flavoured *"Set yourself. Nothing moves you for a
while."* Both the name and the prose promise defence; the ability raises damage by 40%. The Warden's
actual guard at that end of the ladder is Bulwark at 28, so this is not a duplicate — it is one
ability describing itself as another.

**Retuned to `buff.defense`, keeping the name and the flavour** — 6 defence and 6% mitigation for
25s, a step under Bulwark's 8/8. The name was the half worth keeping: Battle Fury at 5 is already a
damage buff, so a second one at 13 was redundant, and it left the Warden with no guard at all until
28.

Both fixes needed a way to reach a running server, because `AbilityCatalogue` seeds only a *fresh*
database and the startup reconcile plants what is missing without ever updating — deliberately, so
a restart cannot revert a builder's work. Hence `tools/export-abilities.cs`, which writes named
catalogue rows out as upsert SQL with the derived description above each one, so what a statement
will do is readable before it is run:

```
dotnet run tools/export-abilities.cs warden.last-stand warden.shield-wall -o backups/fix.sql
```

It names keys rather than exporting everything by default, because an upsert overwrites: `--all`
would push the catalogue over the top of every retune made through the editor, silently.

---

## 28. A buff refresh kept the weaker magnitude — **fixed**

`WorldState.ApplyEffect` dedupes on `(EffectKey, SourceEntityId)`, and `EffectStackingRule.Refresh`
reset the expiry while keeping **everything else** — including the first effect's numbers. Since every
one of a Path's maximum-health buffs applies `buff.max-health` from the same caster, they all collided
with each other:

| Cast | Was worth | Should be |
|---|---|---|
| Fortitude (+150) then Sanctuary (+220) | +150, on Sanctuary's clock | +220 |
| Ambush (tick 5) then Hemorrhage (tick 16) | tick 5 | tick 16 |
| Hemorrhage then Ambush, repeatedly | tick 16, **forever** | tick 16 until it expires |

That last row is the one that was exploitable: a cheap ability refreshed a expensive effect's clock
without touching its magnitude, so a Shade could hold Hemorrhage open indefinitely with Ambush.

**Now a stronger application replaces a weaker one outright and a weaker one is ignored entirely** —
no refresh, no stack, no extension. Strength is the unlock level of the ability that applied it,
carried on the effect as `SourceUnlockLevel` and stamped by `AbilitySystem`. Equal strength keeps the
old `Refresh`/`Stack` behaviour, which is what a recast of the same ability is — and what every mob
attack rider is, since those carry no ability and sit at zero.

Found while planning shared cooldowns (PLAN.md §4.5), by working out whether the Warden's four walls
could stack. They could not, but only by accident, and the accident was doing more harm than the
stacking would have.

---

## 29. Two abilities typed in one pulse both landed — **fixed**

A cast is recorded as used when it **resolves**, not when the command is parsed, and
`GameLoop.DrainInbound` handles up to `MaxCommandsPerPulse` commands before `abilitySystem.Tick`
runs. So two `bolt` commands in the same pulse both queued, both charged their cost, and both
resolved — and the same window would have made a shared timer bypassable by typing fast.

**A character now starts one action at a time**, refused at the top of `AbilityCommands.Cast` beside
the stun and rest gates, worded by the kind of whatever is in flight: *"You are already casting
Bolt"*, *"You are still in the middle of Kick"*.

---

## 30. The SQL export dropped every item restriction and every exit gate — **fixed**

`tools/export-content.sql` is a hand-written copy of the schema. Two of its nine column lists had
drifted, and both drifted the quiet way — a missing column does not break the restore, it applies
cleanly and writes the column default, so the world comes back looking right:

- **`item_templates`** never listed `is_lore`, `is_no_drop`, `is_light_source` or `paths`, missing
  since the `ItemRestrictions` migration. A restore un-bound every epic reward — twenty items carry
  one — and put out every lamp, so the four dark zones became unreadable with no item able to answer
  them.
- **`room_exits`** never listed `required_flag_key`, `required_item_key` or `refusal_message`. **A
  restore opened every locked door and portal in the game** (§4.15), the four attunement gates
  included — the same damage as #7 by the same mechanism, in a different table.

That is the fourth and fifth drift in this one file, after `spawners.sentinel`, `quests.auto_start`
and `quests.reward_flag_key`. All five were found by diffing the list against the schema rather than
by reading it, and four of the five by somebody who had come here to do something else.

**Fixed, and now guarded.** `ExportScriptCompletenessTests` builds the EF model, parses the script,
and fails on any column of the nine tables its `INSERT` does not name — and, separately, on any it
names that no longer exists, which is the `sentinel` case. Demonstrated failing against the pre-fix
file first. The exemption list is empty and every future entry is a hole in the guard.

---

## 31. Nothing in the world could be held in an off hand — **fixed**

Reported as *"it's hard to get off-hand weapons"*. There were none. All 35 authored weapons were
`MainHand`; all six off-hand items were shields or the torch, and every one of those had no
`attackDelayPulses`, which is the only thing that makes an item a weapon.

So an entire built subsystem had no reachable content: `DualWield` (Shade 3, Warden 5),
`Ambidextrous`, `OffHandDamageShare`, `AttackSlot.OffHand` in `CombatSystem`, the off-hand block in
`stats`, and `wield`'s *"you've not the training to strike with it"* — **which has never fired for
any player**, being gated on an off-hand item that declares a speed. A Shade is told at level 3 that
they can strike with a weapon in their off hand.

The same defect class as the rest of this file, in the rarer direction: usually a field is authored
and read by nobody; here an engine was built and authored for by nobody. `check-builder-keys.py` is
one-directional by design and could not have caught it.

**Fixed** by making `slots` a list and adding `isTwoHanded` (`PLAN.md` §4.19), and by retagging 17
weapons by shape. `BundleValidator` now errors on a template that declares an attack speed and
reaches neither hand — a weapon that can never swing — which is the shape this was.

---

## Not in this queue

Design questions the review raised and deliberately did not answer: **mob leashing** and
containers — `ContainerItemId` is read once as a filter and written nowhere. Each
needs a decision before it needs code. **Quest accept/decline has since been decided** and
written up in `PLAN.md` §13, together with the verb-namespace question it raises — it is scheduled
work now rather than an open question.

**The weapon ladder overlapped by level** — recorded here as a play question, and answered from
play. The rule taken: *an epic reward is a small amount better than anything the player could have
bought or looted by the time it is awarded.* Ten percent, in `WeaponBalanceTests.EpicMargin`.

Three rewards were failing it outright rather than merely overlapping. **`epic-hallow-1` and
`epic-adept-1` both sat at 1.50 dps against the 1.67 of `ossara-short-blade`** — which is on a shelf
in Gatetown at level 1, so a five-quest chain finishing at level 8 handed you a downgrade from the
starter shop. `epic-adept-2` tied the grask line exactly.

It survived because every guard compared **epics to epics and shop lines to shop lines**. Both
ladders climbed correctly and nothing measured the distance between them.
`An_epic_beats_anything_buyable_or_lootable_when_it_is_awarded` is that measure. It derives the bar
from content rather than a table — a weapon is reachable through a shopkeeper's `sells`, a mob's
loot, or an item spawner, at the `minLevel` of the shallowest zone its source stands in — so moving
a shopkeeper moves the bar with it.

Eleven weapons retuned, all dice, no delays and no accuracy. The whole of tier 1 lifted, because the
Path ranking pushes upward from whichever line is lowest and the Adept's is always the floor. The
Adept line is now 2.00 / 2.50 / 3.50 / 4.50 / 6.00 and clears its bar by 11–20% at every tier.

**And the top shop line was unobtainable.** `unlit-long-blade`, `unlit-binding-spike` and
`unlit-standing-hammer` were authored, dropped by nothing, sold by nobody — there is no Unlit
shopkeeper — and priced at `baseValue: 0`, which is the tell: nothing had ever been meant to sell
them. They are drops in the Unlit now, at 0.12 off the three common dead, and priced on the
35 → 130 → 480 → 820 curve the other lines follow. That raised the tier-5 bar to 5.94 and moved
`epic-adept-5` and `epic-hallow-5` with it.

The one earlier case is still fixed and still guarded: `epic-adept-2` once had the same dice, delay
and attack rating as `grask-dredge-hook`, and `No_two_weapons_share_a_stat_line` stops it recurring
— it caught this retune making `epic-hallow-1` an exact copy of the same weapon.

Two have since been decided and built, and are recorded in `PLAN.md` §4.6 rather than here: the
**aggression target rule** (#20 — the room's threat leader, otherwise at random, link-dead characters
excluded) and **mob regeneration** (a heal on disengage plus the regen tick). Leashing was the other
half of that second question and is the half still open: a mob does not walk home, so `Mob` carries no
home room and the wander rules are untouched.

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

## 5. "Melee starts after an ability" is unverified live — **closed**

Half of the 2026-08-09 note was guarded only by engine tests, because a level-1 Kick one-shots a
city rat and there was never anything left to swing at.

Closed by a **test dummy**: `test-dummy` in The Well Yard, 400 health, no attacks, no experience,
spawned by a *sentinel* spawner so it never wanders. `ability-then-melee` now opens on it with a
kick and watches the Warden's own weapon come round — deterministically, because the target cannot
die to the opener.

```
Your Kick hits a test dummy for 10.
You hit a test dummy for 1 damage.
```

A target that cannot die and does not meaningfully fight back is worth more here than a realistic
one: a plan that has to survive its own opponent is measuring the balance rather than the thing
under test.

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

### Step 3 — Re-run the plan library and clear the flag ✅ **done**

All six read clean except `shopping`, which degrades as designed without `--admin-user` and says
so. `combat-basics` shows an exchange running its full eighteen seconds with **another rat
wandering in partway through** — the proof that the guard stopped the right mob rather than
freezing the zone.

It also caught one of the plans passing for the wrong reason. `A group fight ends` checked release
with `kill rat`, but `kill` looks up its target *before* checking whether you are already fighting,
so once the rat was dead the answer was "You don't see 'rat' here" and the check was never reached.
Movement is refused only for being in combat, so both members now walk out instead — unambiguous.

```
dotnet run --project tools/Muwbta.Playtest -- --server http://localhost:5050 \
    --plans tools/Muwbta.Playtest/plans
```

### Step 4 — Hosted target and world fixtures — **partly done, and partly overtaken**

The *purpose* of fixtures was determinism, and a permanent `test-dummy` bought most of it for a
fraction of the work — see #5. A standing dummy is arguably the better answer anyway: it is content
a human can also walk up to and hit.

**Still outstanding: the hosted target itself** — booting the server in-process against a throwaway
Postgres (`PLAYTEST.md`, milestone 4). What it would still buy:

- a run that needs no server started by hand, and no admin credential, since it owns the database;
- no litter at all, because the database goes with the run;
- reproducibility from empty, which the dummy does not give — it lives in one dev world and nothing
  recreates it elsewhere. **If that database is ever reset, `test-dummy` and its sentinel spawner
  have to be rebuilt**, and today the only record of how is the `about:` block in
  `ability-then-melee` and this paragraph.

That last point is the real argument for finishing it.

### Step 5 — Have the apparatus tidy up after itself ✅ **done**

A janitor runs after every run: one Admin actor that deletes every character the run created, by
the name the world actually gave it. Default on wherever an admin credential makes it possible,
`--no-cleanup` to opt out. Ten characters a run now become one — the janitor's own, which it cannot
delete, and which the summary names rather than hides.

Reviewing it turned up two things worth having:

- **`role:` never worked.** The loop is told an actor's role on the `EnterWorld` message and never
  looks again, so promoting somebody already standing in the world left the loop believing the role
  they arrived with. Promotion now happens between registering and entering. `shopping` was the
  plan that would have caught it and had been degrading gracefully past it the whole time.
- **The apparatus outgrew the auth limiter.** Ten attempts a minute per address; a seven-plan run
  spends eleven. Two runs back to back and the second one's janitor is refused. Registration and
  sign-in now wait out a 429 the way commands do — see #4, of which this is the same coarse
  partition seen from the client side.

The accounts still remain: there is no delete-account verb and there should not be one lightly.
See `PLAYTEST.md` for the SQL purge.

### Not in this queue

#4 stays open and deliberate until there is a deployment behind a proxy that needs it. Phase 6's
remaining items — world export/import, and backups plus a recovery runbook — are features, not
bugs; they live in `PLAN.md` §8.
