# Abilities past level 20

The four Paths finish unlocking at level 20 and then give nothing for the next thirty levels. This
is the plan for filling them. Design only — nothing here is built.

Called *abilities* rather than *skills* throughout, because that is what the table, the entity, the
cache, the validator, the command, and every existing key are called. One word for one thing.

`PLAN.md` §4.5 says what an ability is and why the table rather than the catalogue is authoritative.
This says what the missing thirty levels should contain.

---

## 1. The problem, stated in numbers

Every Path unlocks at **1, 3, 5, 7, 10, 13, 16, 18, 20** and then stops. Warden has two extra at 8
and 9, Shade one at 12. Thirty-seven rows, and the last of them arrives at level 20.

| Path | Unlock levels | Count |
|---|---|---|
| Warden | 1, 3, 5, 7, 8, 9, 10, 13, 16, 20 | 10 |
| Adept | 1, 3, 5, 7, 10, 13, 16, 18, 20 | 9 |
| Shade | 1, 3, 5, 7, 10, 12, 13, 16, 20 | 9 |
| Hallow | 1, 3, 5, 7, 10, 13, 16, 18, 20 | 9 |

**Levels 21 to 50 are empty.** That is 60% of the level range, and because `XpForLevel` is
quadratic it is far more of the game than that: 190,000 experience buys levels 1 to 20, and
1,035,000 buys 20 to 50. **The empty half is 84% of the total climb**, and five and a half times the
half that has all the abilities in it. A player spends the overwhelming majority of the game
receiving nothing but bigger numbers.

The world design assumes otherwise. `WORLD.md` puts three whole realms above level 24 and its
storyline runs to 50.

## 2. What this costs, and what it does not

**No new engine work.** Every one of the four directions asked for is authorable against effects
that already exist and already have executors:

| Ask | Effects it needs | Status |
|---|---|---|
| Hallow: better heals, health and protection buffs | `heal.restore`, `buff.max-health`, `buff.defense` | All registered |
| Warden: better tanking, AoE taunts | `control.taunt` + `TargetingType.Aoe` | Both work |
| Adept: more damage, AoE spells | `damage.physical` + `Aoe` | Already used by Firestorm |
| Shade: damage over time and burst | `damage.overtime`, `damage.physical` | Both registered |

**AoE resolves by direction already**, which is the part worth confirming rather than assuming
([AbilitySystem.cs](../src/DikuWeb.Engine/Abilities/AbilitySystem.cs) `AreaTargets`). A harmful area
effect gathers every mob plus other players only where the room resolves `pvp`, never the caster and
never their party. A helpful one gathers the caster plus their party, or everyone in the room when
they have no party. **So a group heal works today** — it lands on your side, not on the strangers,
and not on the things you are fighting.

The eleven effect keys available:

| Key | Harmful | Required params |
|---|---|---|
| `damage.physical` | yes | `scalingFactor` |
| `damage.overtime` | yes | `tickDamage`, `tickIntervalPulses`, `durationPulses` |
| `heal.restore` | no | `baseHeal` |
| `buff.damage-up` | no | `outgoingMultiplier` (> 1.0) |
| `buff.defense` | no | — |
| `buff.max-health` | no | `maxHealth`, `durationPulses` |
| `debuff.weaken` | yes | `durationPulses`, and `outgoingMultiplier` (< 1.0) or `incomingMultiplier` (> 1.0) |
| `debuff.expose` | yes | — |
| `control.stun` | yes | `durationPulses` |
| `control.root` | yes | `durationPulses` |
| `control.taunt` | yes | `leadFraction` |

## 3. One engine change, and it is a constant

`AbilityValidator.ProgressionCompleteLevel` is **20**, and the gap check runs
`Where(a => a.UnlockLevel <= ProgressionCompleteLevel)`. So today the validator stops caring about
spacing at exactly the point this plan starts filling.

Left alone, thirty levels of new content would be the only part of the progression nothing checks —
and the check it would be missing is the one that catches the failure this plan exists to fix. It
should move to **50**, which turns `MaxLevelGap` into a real constraint across the whole range and
makes "Warden goes from level 32 to 40 with nothing new" a warning a builder sees.

That is a one-line change plus whatever it then reports about the existing rows. Nothing else in the
engine moves.

## 4. The shape of the back half

**Eight unlocks per Path, at 24, 28, 32, 36, 40, 43, 46, 50.** Thirty-two new abilities, taking the
catalogue from 37 to 69.

Spacing is four levels to 40 and three after it, which is deliberate: the XP curve steepens, so a
level late on is a much longer stretch of play than a level early on, and a constant four-level gap
would feel like an increasing drought. `MaxLevelGap` is 4, so this sits at the limit and never over
it.

**Three kinds of unlock, in a repeating pattern.** Every Path gets the same rhythm so that no Path
feels like it stops developing, and so a player who has levelled one knows what to expect from the
next:

| Level | Kind | What it is |
|---|---|---|
| 24 | **Reach** | The Path's first ability aimed at more than one target, or its first sustained one |
| 28 | **Refinement** | A better version of something owned since the teens — the old one stays useful |
| 32 | **Reach** | The second area or sustained tool, usually the expensive one |
| 36 | **Refinement** | |
| 40 | **Signature** | The ability the Path is remembered for. Long cooldown, large effect |
| 43 | **Refinement** | |
| 46 | **Reach** | |
| 50 | **Capstone** | One per Path. Rare, enormous, and the reward for the last climb |

**Costs and cooldowns rise with the band rather than staying flat.** A level 40 ability costing what
a level 13 one costs is a level 13 ability with a bigger number on it. The rule of thumb: cost
roughly `unlockLevel × 1.5` in its resource, and cooldown long enough that the ability is a decision
rather than a rotation slot — 240 pulses (60s) for a Signature, 600+ (150s) for a Capstone.

---

## 5. Warden — holding a room, not a target

Warden owns the frontline. Everything below is about **taking hits for other people**, which is a
different job from surviving hits yourself, and which the current list barely addresses: `taunt` is
single-target and arrives at level 8, and nothing after it helps hold more than one thing.

| Level | Key | Name | Effects | The idea |
|---|---|---|---|---|
| 24 | `warden.thunderclap` | Thunderclap | `control.taunt` (Aoe) | **The AoE taunt.** Everything hostile in the room takes you as its lead. The single ability this Path has most obviously been missing |
| 28 | `warden.bulwark` | Bulwark | `buff.defense` (Self) | Refines Shield Wall — a real defensive cooldown rather than a damage buff wearing its name |
| 32 | `warden.ground-and-centre` | Ground and Centre | `buff.max-health`, `buff.defense` (Self) | More bar to lose and harder to take it off you, together. The two-effect ability Last Stand proved the shape of |
| 36 | `warden.reprisal` | Reprisal | `damage.physical`, `control.taunt` | Refinement: threat that also hurts, so holding aggro stops costing damage |
| 40 | `warden.unbreakable` | Unbreakable | `buff.defense`, `buff.max-health` (Self) | **Signature.** The long-cooldown survival button a tank plans a fight around |
| 43 | `warden.sundering-blow` | Sundering Blow | `damage.physical`, `debuff.expose` | Refines Sunder: the target takes more from everyone, which is a tank contributing damage without dealing it |
| 46 | `warden.mass-provocation` | Mass Provocation | `control.taunt` (Aoe), `debuff.weaken` | The second AoE hold, and it softens what it grabs |
| 50 | `warden.last-wall` | The Last Wall | `buff.defense`, `buff.max-health`, `control.taunt` (Aoe) | **Capstone.** Take the room, and become very hard to remove from it |

**Why two AoE taunts.** Thunderclap at 24 is the workhorse; Mass Provocation at 46 is the one that
also weakens, for the fights where holding four things means surviving four things. A single
scaling taunt would have been simpler, but the engine has no notion of an ability that improves
with level — a row is a row (§4.5) — so a stronger version is a second row.

## 6. Adept — from one target to the room

Adept already has Firestorm and Cataclysm as area spells at 18 and 20, so the direction exists. What
is missing is everything after them.

| Level | Key | Name | Effects | The idea |
|---|---|---|---|---|
| 24 | `adept.conflagration` | Conflagration | `damage.overtime` (Aoe) | Area damage that keeps burning — the first spell that rewards fighting a group rather than surviving one |
| 28 | `adept.shatter` | Shatter | `damage.physical`, `debuff.expose` | Refines Bolt: hits hard and leaves the target softer for the next one |
| 32 | `adept.chain-lightning` | Chain Lightning | `damage.physical` (Aoe) | The big instant area nuke. Expensive enough to be a choice |
| 36 | `adept.unmaking` | Unmaking | `damage.physical`, `debuff.weaken` | Refinement: damage that also takes the target's own damage down |
| 40 | `adept.pyre` | Pyre | `damage.physical`, `damage.overtime` (Aoe) | **Signature.** Everything in the room, twice — once now and once over the next several seconds |
| 43 | `adept.arcane-surge` | Arcane Surge | `buff.damage-up` (Self) | Refines Amplify. The window you fire a Capstone through |
| 46 | `adept.gravity-well` | Gravity Well | `control.root` (Aoe), `damage.overtime` (Aoe) | Holds the room still and burns it. The Path's only real crowd control |
| 50 | `adept.unwriting` | The Unwriting | `damage.physical` (Aoe), `debuff.expose` | **Capstone.** The largest single number in the game, and it leaves what survives easier to finish |

## 7. Shade — burst now, bleeding after

The two halves asked for map exactly onto two effects the engine already has, so a Shade's back half
is about the interplay between them: land the sustained damage, then spend the burst while it ticks.

| Level | Key | Name | Effects | The idea |
|---|---|---|---|---|
| 24 | `shade.rupture` | Rupture | `damage.physical`, `damage.overtime` | The first real bleed. Hits, then keeps hitting |
| 28 | `shade.exploit` | Exploit | `damage.physical`, `debuff.expose` | Refines Quick Strike: cheap, fast, and makes everything after it land harder |
| 32 | `shade.flurry` | Flurry | `damage.physical`, `buff.damage-up` (Self) | Burst that buys more burst |
| 36 | `shade.hemorrhage` | Hemorrhage | `damage.overtime` | Refinement: the long bleed, cheap, meant to be kept running |
| 40 | `shade.execution` | Execution | `damage.physical` | **Signature.** The biggest single-target instant in the game, on a cooldown that makes it a moment |
| 43 | `shade.shadowstep` | Shadowstep | `damage.physical`, `control.stun` | Refines Ambush — opens or interrupts, and hurts either way |
| 46 | `shade.thousand-cuts` | A Thousand Cuts | `damage.overtime` (Aoe) | The Path's one area tool, and it is bleeds rather than a nuke, which is the right shape for it |
| 50 | `shade.severance` | Severance | `damage.physical`, `damage.overtime`, `debuff.weaken` | **Capstone.** Burst, bleed, and the target hits back softer while it dies |

**Death Mark at 20 stays the setup and is not replaced.** The back half is built to be spent through
it rather than around it.

## 8. Hallow — keeping people alive, and keeping them standing

The Path's asks are the three helpful effects, and the distinction worth authoring to is that
**healing undoes damage, while health and protection change how much damage there is to undo.**
Hallow should end the game doing both, and the group versions are what make it a support Path rather
than a slower Adept.

| Level | Key | Name | Effects | The idea |
|---|---|---|---|---|
| 24 | `hallow.communion` | Communion | `heal.restore` (Aoe) | **The group heal.** Lands on your party, or the room when you have none |
| 28 | `hallow.fortitude` | Fortitude | `buff.max-health` (Aoe) | Everyone gets a bigger bar before the fight, not a smaller hole after it |
| 32 | `hallow.aegis` | Aegis | `buff.defense` (Aoe) | Group protection — the other half of the same idea |
| 36 | `hallow.mending-tide` | Mending Tide | `heal.restore` (Aoe) | Refinement: the strong group heal, expensive, for when Communion is not enough |
| 40 | `hallow.sanctuary` | Sanctuary | `buff.defense`, `buff.max-health` (Aoe) | **Signature.** The whole group harder to kill at once, for a while |
| 43 | `hallow.absolution` | Absolution | `heal.restore` (SingleTarget) | Refines Restore: the emergency single-target heal, large and fast |
| 46 | `hallow.consecration` | Consecration | `buff.damage-up` (Aoe) | Support that is not healing. The group hits harder |
| 50 | `hallow.the-long-vigil` | The Long Vigil | `heal.restore`, `buff.max-health`, `buff.defense` (Aoe) | **Capstone.** Heal the group, raise its ceiling, and armour it, in one cast |

**Hallow gets no new damage past 20**, deliberately. Wither and Sap already exist and the Path is
playable solo through them; adding damage to the back half would make Hallow a worse Adept in the
place it should be an irreplaceable Hallow.

---

## 9. Verification

Every row above is checkable before a line of it is authored, and should be:

- **Validator rules** — a key prefixed with its Path, a non-empty name, `CostValue > 0`, a
  non-negative cooldown, an unlock level in 1–50, and at least one effect
  ([AbilityValidator.cs](../src/DikuWeb.Domain/Abilities/AbilityValidator.cs)).
- **Required params present** — every effect in §2's table carries the parameter it actually reads.
  An effect missing its one meaningful key is an ability that runs and does nothing.
- **No effect listed twice in one ability**, which the validator refuses: the second refreshes the
  first rather than stacking, so the ability is quietly weaker than it reads.
- **No mixed directions** — the validator refuses a list mixing harmful and helpful effects, which
  is why no ability above heals and damages in the same row.
- **Buff and debuff directions** — `buff.damage-up` above 1.0, `debuff.weaken` with
  `outgoingMultiplier` below 1.0 or `incomingMultiplier` above it. Getting the second backwards
  made every weaken in the game strengthen its target once already.
- **Spacing** — with `ProgressionCompleteLevel` raised to 50, no gap in any Path exceeds
  `MaxLevelGap`. The table in §4 is built to that and sits exactly at 4 up to level 40.
- **One unlock per level per Path** — the validator warns on two, since one level would give twice
  and its neighbours nothing.

## 10. How it lands

Abilities are content and travel in the `WorldBundle` (`FormatVersion` 6). Two routes, and the
choice matters:

- **`AbilityCatalogue`** is what a *fresh* database is seeded with, and `ReconcileAbilitiesAsync`
  plants only what is missing — it never updates or deletes, so a retune survives a restart. Adding
  rows here means a new deployment gets them automatically, and an existing one gets them on next
  boot.
- **The builder API and a bundle import** reach a running server, which is the route for tuning.

Both are correct; the catalogue is the right home for the initial thirty-two, because the point is
that every deployment has them. Retuning afterwards happens in the table.

**This does not need `WORLD.md`.** Abilities belong to a Path rather than to a Reach, and a scoped
bundle export carries all of them or none for that reason. The two documents meet only at level
bands: `WORLD.md` puts content at 24, 34, 46 and 50, and this puts a reason to have levelled at
each of them.
