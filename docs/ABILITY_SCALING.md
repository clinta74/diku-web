# Ability damage and level

A design note on the open issue *"ability damage never scales with level"*, on whether the ability
line is the answer to it, and on what the balance harness found when the question was actually
measured.

Everything here comes from `content/` and `src/` on this branch, or from
`tools/DikuWeb.Balance` run against them. Numbers marked **measured** come from 41 simulated fights
per cell; everything else is derived from an authored number or a formula in the code.

> **Measured against a fresh export of the live database** (`build/live.json`, 2026-08-21), not
> against `content/`, which is an older export and was stale for the epic weapons. Pull your own with
> `dotnet run tools/export-bundle.cs -- -o build/live.json` — it reads through `WorldExporter` with
> no server running, so nothing migrates, seeds, or reconciles on the way past.

---

## 1. What is flat

`DamageEffect.Middle` is the whole of it:

```csharp
public const int UnscaledBaseDamage = 10;
var scaledDamage = (int)(UnscaledBaseDamage * scalingFactor);
```

No caster, no level, no attribute. `Apply` receives the caster and discards it. Every authored
`scalingFactor` falls between `1.1` and `3.5`, so **every direct-damage ability in the game deals
between 11 and 35 damage, at every level, forever** — and that 3.2× spread is the gap between
*different abilities*, not growth in any one of them.

### It is the only quantity in combat with no level term

| Quantity | Level 1 | Level 50 | Growth |
|---|---|---|---|
| Mob attack dice **(measured)** | 2–5 | 40–70 | **16×** |
| Typical mob health **(measured)** | 22 | 747 | **34×** |
| Authored heals | 15 | 260 | 17× |
| Player max health | 36–60 | ~291–315 | 7× |
| Weapon dice | 2–3 | 9–18 | 6× |
| Resource pool | 100 | 257 | 2.6× |
| DoT tick | 5 | 22 | 4.4× |
| **Direct ability damage** | **11** | **35** | **3.2×** |

---

## 2. Why it happened: the constant is invisible to the author

Heals author `baseHeal: 260`. DoTs author `tickDamage: 22`. Direct damage authors
`scalingFactor: 3.5`.

The first two are absolute numbers, and an author writing `260` is looking at a health bar and can
see whether it is right. They compensated by hand, and the heal curve (17×) is the best-scaling
authored curve in the game.

`scalingFactor: 3.5` reads like "three and a half times". It is 35 damage. The multiplier is over a
constant no builder ever sees, in a file no builder opens — so **the one effect whose numbers were
hidden behind an abstraction is the one effect that never got tuned.** Any authored dial expressed
as a ratio over a private constant is a dial whose author cannot check their own work.

---

## 3. Is mob defence offsetting it? No — abilities don't touch defence

Ability damage does not pass through mob defence at all. PLAN.md §4.6:

| | to-hit roll | armour | damage buffs/debuffs |
|---|---|---|---|
| Weapon swing | yes | yes | yes |
| Ability damage | no | **no** | yes |

So the only thing between a cast and a mob is the raw health pool — the quantity that grew 34×.
Armour bypass does move the comparison in abilities' favour (typical mob armour runs 0 at level 1 to
139 at level 50, worth roughly 1.6× to a caster by the end), but that is a 1.6× tailwind against a
34× headwind.

---

## 4. What the harness measured

`tools/DikuWeb.Balance` fights a Path at a level, in that realm's gear, against the median-health
combat mob of a zone whose authored band contains that level. Then it fights the identical fight
again with the ability bar switched off.

### 4a. The direct-damage line loses ~93% of its value; the wound line does not

One cast, as a share of a typical mob of the level it unlocks at — **measured**:

| | Warden | Temper | Adept |
|---|---|---|---|
| **First damage ability** | Kick (L1) **50.0%** | Quick Strike (L1) **54.5%** | Bolt (L1) **54.5%** |
| Mid | Crushing Blow (L16) 13.2% | Heart Strike (L16) 15.8% | Disjunction (L16) 14.5% |
| | Reprisal (L36) 4.3% | Flurry (L32) 4.8% | Chain Lightning (L32) 5.1% |
| **Capstone** | Sundering Blow (L43) **3.8%** | Temple Strike (L43) **3.6%** | The Unwriting (L50) **4.0%** |

Every Path's direct-damage line starts at roughly half a target and ends at roughly a twenty-fifth.

The wound line, authored in absolute numbers, does not do this:

| Path | Ability | Level | Total | Share of target |
|---|---|---|---|---|
| Adept | Scorch | 10 | 45 | 62.5% |
| Adept | Conflagration | 24 | 98 | 38.9% |
| Adept | Pyre | 40 | 162 | 42.7% |
| Temper | Body Blow | 10 | 25 | 34.7% |
| Temper | Rupture | 24 | 70 | 33.7% |
| Temper | Hemorrhage | 36 | 176 | **47.7%** |
| Temper | Collapse | 50 | 308 | 45.9% |

**This is the finding in one comparison.** Two kinds of ability, in the same file, tuned by the same
authors, against the same health curve — and the one that authors an absolute number held its value
across fifty levels while the one that authors a ratio lost nine tenths of it.

### 4b. I was wrong that abilities are decoration

An earlier draft of this note concluded, from sustained-damage-per-cooldown arithmetic, that
direct-damage abilities contribute 2–5% of output at level 50. That is wrong, and the harness is why:
a real fight is long enough for the cooldowns to come back many times over. **Measured** share of
total damage dealt:

| Path | L1 | L20 | L35 | L50 |
|---|---|---|---|---|
| Warden | 64% | 62% | 43% | **41%** |
| Temper | 73% | 68% | 59% | **78%** |
| Adept | 73% | 72% | 53% | **78%** |
| Hallow | 0% | 48% | 22% | **35%** |

And switching the bar off is decisive nearly everywhere — kills are **55–89% faster** with it, and at
a third of the cells measured the bar is the difference between winning and never winning at all.

The refined claim is narrower and better supported: **the flat base hurts the Path that depends on
direct damage.** The Warden is the only Path with no wounds at all, and the Warden's share is the
only one that falls with level — 64% → 41%. Temper and Adept are carried by their DoTs.

### 4c. The Warden's damage ceiling is set at level 16

Crushing Blow (L16, factor 2.0) is the Warden's largest damage ability. Reprisal (L36, 1.6) and
Sundering Blow (L43, 1.9) are both smaller, cost more, and have longer cooldowns. **Levels 17–50 give
a Warden no ability damage growth whatsoever.** No other Path has this, and it is a content bug that
the flat base was hiding.

### 4d. Two more scaling failures the harness turned up

Neither is about abilities, and one of them was misdiagnosed twice before it held still.

**The resource bar is a flat ~9 casts at every level, while fights grew fifteen-fold.** Pool and cost
actually track each other reasonably — a Warden holds 10 casts' worth at level 1 and 9.4 at level 50,
because `VitalCalculator` (`starting + 3·(level-1)`) and the authored `costValue` curve happen to
grow together. What did not grow is the fight. A level 1 kill takes six seconds and a level 50 kill
takes ninety, so a bar sized for a whole fight at the bottom covers a third of one at the top.

**In-combat regeneration is not the fix, and this is the part I got wrong first.** `RegenCalculator`
returns 2% of a maximum per 60-second tick, which reads like the obvious culprit. It is not:
multiplying it by **eight** moves the count of unwinnable cells from 7 to 6, and ticking it more
often at the same rate moves nothing at all. The constraint is the bar's *size* against a cast's
cost, not the rate it refills - so the lever is `FocusMax`/`StaminaMax` or the authored costs, and
not `RegenCalculator`.

**The Adept and the Hallow hold half the Warden's bar.** This is where the real asymmetry is: 8-10
casts for a Warden at every level, 3.3-5.5 for an Adept.

**Solo endgame is a level 50 problem, not a level 40 one.** Win rates against the median combat mob
of a level-appropriate zone, in standard gear, with the level term in place - **measured**:

| Path | L40 | L45 | L50 |
|---|---|---|---|
| Warden | 100% | 100% | **39%** |
| Temper | 100% | 100% | 100% |
| Adept | 100% | 100% | **32%** |
| Hallow | 100% | 100% | **35%** |

**Whether solo is meant to work at all is a design decision, not a measurement.** The harness plays
one character with no consumables, no fleeing and no group, and a MUD may reasonably expect a party
at the top. Every constant in §7 depends on which way that goes.

---

## 5. What should change

### 5.1 The two questions are orthogonal, and conflating them is the bug

> *"If the abilities did scale with level then why have new versions with increased power?"*

Because **level scaling moves the whole line up together, and the authored factor separates its
members.** These are different axes:

- The **level term** answers "how big is a spell cast by someone this powerful, in a world this big".
  It applies to every ability equally.
- The **authored factor** answers "how big is *this* ability next to its siblings". Disjunction
  (2.2) is 83% larger than Bolt (1.2) — at level 16, at level 50, at every level.

With no level term the factor is doing both jobs and can only do one. That is why the line looks like
a contradiction: the only way to make a level 50 ability feel bigger was to make its *number* bigger,
and 3.5 was as far as that could go. So the authors reached for the cooldown instead — and the
ledger in §4a shows what that bought.

### 5.2 Should a level 50 Adept's Bolt hit harder than a level 1 Adept's? **Yes.**

The decisive argument is not flavour: **the cost already scaled and the effect did not.** Bolt costs
15 focus out of a 50-focus pool at level 1 — 30% of everything the character has — and 15 out of
~212 at level 50, which is 7%. Price and value fall to nothing at the same rate. The button does not
become bad; it becomes *absent*. Scaling the effect is exactly what keeps a level 1 ability a working
part of a level 50 kit, which is what the ability-line idea is reaching for.

Damage should stay **absolute**, not a percentage of the target. A level 50 Adept obliterating an
Ossara rat is correct and legible; percent-of-health damage is a different game and breaks against
every boss.

### 5.3 The shape

One level term, on the base, read from the caster:

```csharp
damage = BaseAtLevel(caster.Level) × scalingFactor
```

`BaseAtLevel` replaces `UnscaledBaseDamage`. It is the only new concept, it lives in one place, and
every authored `scalingFactor` keeps its current meaning and value. **No content re-authoring is
needed to make the mechanism work** — the factors already encode the lines; the constant they
multiply is what is wrong.

**Level, not attribute.** The comment on `UnscaledBaseDamage` proposes "level and casting attribute".
The attribute half does not work, for the reason `AbilityProgression.OffHandMasteryLevel` already
documents about Agility: attributes start at 10, cap at 20, and grow 2/level for a Path's primary —
so an Adept's Insight and a Temper's Might are **both capped by level 6**. A modifier frozen at +5 for
88% of the game is not a scaling axis.

**One term, not two.** A per-ability "maturity" ramp is tempting and belongs out of v1: at any sane
rate it is worth about ±30%, which is inside the ±20% variance already on every roll plus the buff
multipliers on top. A term that lands inside the noise cannot be perceived, and it doubles the tuning
surface to deliver it.

### 5.4 What defines a line, if the numbers alone cannot

A line member must beat its predecessor **on the axis it exists for**. Three roles:

| Role | Cooldown | Cost | Damage | Example |
|---|---|---|---|---|
| **Filler** | short (≤15 s) | low | moderate | Bolt, Quick Strike |
| **Burst** | long (≥45 s) | high | large | Cataclysm, Execution |
| **Rider** | medium | medium | priced down for the effect | Shatter, Temple Strike |

Each Path should refill **each** role as it levels. Today every ability past level 20 drifts toward
burst, so the filler slot is never re-filled and stays occupied by a level 1 button for
forty-nine levels. Temper is the one Path that got this right, probably by accident: A Hundred Hands
(L46, 8 s cooldown, 12.0 damage/sec — **the highest sustained figure in the game**) is a real
late-game filler. Adept and Warden have nothing of the kind.

---

## 6. Implementation surface

Small, because the plumbing already exists.

| Change | File | Note |
|---|---|---|
| `UnscaledBaseDamage` → `BaseAtLevel(level)` | `Domain/Abilities/Effects/DamageEffect.cs` | the whole mechanism |
| `Middle(parameters)` → `Middle(parameters, level)` | same | static, shared with `Describe` |
| `Apply` reads `caster` | same | already passed, currently discarded |
| `IAbilityEffect.Describe(params, targeting)` → `+ level` | `Domain/Abilities/IAbilityEffect.cs` | **the only interface break** |
| Thread level through | `Domain/Abilities/AbilityDescriber.cs` | one method |
| Pass caster level | `Engine/Commands/AbilityCommands.cs:420` | sole engine call site; caster in hand |
| Same for `tickDamage` | `Effects/DamageOverTimeEffect.cs` | `CreateActiveEffect` already receives the caster |
| Decide about `baseHeal` | `Effects/HealEffect.cs` | see below |

`Describe` **must** take the level. This codebase has repeatedly found that a screen disagreeing with
the game is the failure mode that survives longest — `IAbilityEffect.Describe`'s own doc comment says
so. A level-scaled ability described by a level-free formula would be that bug on day one.

**Heals are a genuine open question.** Their denominator is player health, a fixed formula an author
can hold in their head — and they did, correctly, at 17×. Damage's denominator is mob health, which a
builder can change with a zone dial. Converting heals buys consistency and protects against drift;
leaving them absolute preserves the best-tuned curve in the file. Separate change either way.

---

## 7. What the level term fixed, and what it did not

`DamageEffect.BaseAtLevel` now grows the base by 4/10 of a point per level: 10 at level 1, unchanged,
and 30 at level 50. Every authored `scalingFactor` keeps its meaning, and no content was retuned.

**Measured, against the live export, in standard gear:**

| `PerLevelNumerator` | unplayable cells (of 44) | cells the epic rescues |
|---|---|---|
| 0 — the old flat base | **13** | 6 |
| 2 | 9 | 4 |
| 3 | 7 | 2 |
| **4 — shipped** | **7** | 3 |
| 5 | 5 | 3 |
| 7 | 3 | 1 |

**It was deliberately not sized to clear the board.** Seven tenths clears almost every cell, and it
does it by ending fights before the resource bar can bind — abilities reach 92–98% of all damage
dealt at level 50 and the weapon becomes ornamental. That is not a fix, it is a louder symptom. Four
tenths does the job the term exists for: it puts the direct-damage line back in the same band as the
wound line (Sundering Blow 3.8% → 14.7% of a level-appropriate target) and leaves the remaining
problems visible, which is where they belong.

### The three things still stopping a solo player

**1. The Adept and the Hallow have half the Warden's bar, in casts.** Measured, from the resource
table: a Warden holds 8–10 casts' worth at every level; an Adept holds 3.3–5.5 and a Hallow similar.
The residual failures are all theirs, and they all look the same — the median fight gets through
82–100% of the target's health and then runs dry.

**Regeneration is *not* the lever, and I checked twice.** Multiplying in-combat recovery by eight
moves 7 unplayable cells to 6. Ticking it more often at the same rate moves nothing. The bar's *size*
against what a cast costs is the constraint, not how fast it refills — which points at
`VitalCalculator.FocusMax`/`StaminaMax` (`starting + 3·(level−1)`) or at the authored `costValue`
curve, not at `RegenCalculator`.

**2. The epic line gates content it drops in.** Three cells are unwinnable in standard gear and
winnable with the Path's epic. An epic that rescues a fight the realm's own shop gear cannot win has
stopped being a bonus.

**3. The Hallow has exactly one damage ability in the entire game.** `hallow.wither`, a wound,
unlocked at level 5. Everything else the Path learns is a heal, a buff, or a debuff — so a solo
Hallow is a weapon-swinger with a bleed, and the level term does nothing for it because it touches
direct damage only. This is a content gap, not a curve.

### In order

1. **Size the Adept and Hallow resource pools against what their abilities cost.** Every remaining
   stuck cell is here, and regeneration has been ruled out as the fix.
2. **Give the Hallow a damage ability past level 5.** One wound for forty-five levels is why it is
   the worst-off Path in every table.
3. **Re-tune the cooldowns** (§4a). The level term does not touch this and it is half of why the
   lines are not lines.
4. **Content pass**: the Warden needs damage abilities past level 16 larger than Crushing Blow, and
   Adept and Warden both need late-game fillers.
5. **Separately**, decide whether `baseHeal` and `tickDamage` become factors (§6). The wound line is
   currently the best-tuned thing in the file and does not need rescuing — this is about drift.

## 8. The harness

`tools/DikuWeb.Balance` — a console tool, in the shape of `tools/DikuWeb.Playtest`.

```
dotnet run --project tools/DikuWeb.Balance
dotnet run --project tools/DikuWeb.Balance -- --content build/live.json
dotnet run --project tools/DikuWeb.Balance -- --paths Temper --levels 40,50 --runs 200
dotnet run --project tools/DikuWeb.Balance -- --csv build/balance.csv
```

Four tables: the encounters and what they hit for; where each Path's damage comes from; what the
ability bar is worth with the same fights run twice; and every damage ability rendered as damage
rather than as a multiplier over a hidden constant.

Every number comes from the Domain — `DamageCalculator` rolls the swings, `EquipmentResolver` reads
the gear, the real `IAbilityEffect` executors apply the effects, `DamageMultipliers` composes the
buffs. The loop decides only *when* things happen, never what they are worth.

**Its assumptions, all of which are printed with the results:** a character wears their realm's best
for their Path, preferring the authored epic; the target is the median-health combat mob of a zone
whose authored band contains the level; the rotation heals below 40%, keeps a damage buff up, and
otherwise presses the largest non-redundant thing it can afford. It models solo play with no
consumables, no fleeing, and no group.
