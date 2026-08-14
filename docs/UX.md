# UX evaluation

A review of the browser client — the game screen and the builder — against the six things the
playtest note asked for: best practice, style consistency, letter casing, spacing, layout spacing,
resizability, and whether it is intuitive.

Written 2026-08-13 against 2,457 lines of CSS across three files and 58 components.

**The summary, before the detail.** The client is in better shape than a codebase of this age
usually is, and the two things most reviews of a hobby project spend their length on — inconsistent
capitalisation and unlabelled controls — are already right here. What it lacks is a *system*: there
are seventeen colour tokens and none for spacing, type, or radius, so every component reaches for a
number and the numbers have drifted apart. One finding is a live defect rather than a matter of
taste, and it is first.

Findings are ordered by what they cost a user, not by effort.

---

## 1. Four destructive buttons have no destructive styling — FIXED

**Severity: defect.** This is not a style opinion; the class does not exist.

Four buttons ask for `className="danger"`:

| File | What it removes |
|---|---|
| `builder/mobs/AttackEditor.tsx:68` | An attack |
| `builder/mobs/LootEditor.tsx:67` | A loot entry |
| `builder/mobs/MobBehaviorEditor.tsx:99` | An idle emote |
| `builder/mobs/MobBehaviorEditor.tsx:229` | A stocked shop item |

The stylesheets define `.editor-section.danger` (a section's top border), `.danger-button`, and
`.menu-item-danger`. There is **no `.danger` rule that applies to a button**, and
`.editor-section.danger` matches the section element rather than any descendant. So all four render
as ordinary buttons, visually identical to *Add emote* sitting beside them.

The elsewhere-correct spelling is `.danger-button`, used in four other places including
`ConfirmDialog`. So the product has two names for one variant and one of them is dead.

**Fixed** by renaming the four to `danger-button`. `.danger` was not added as an alias: two names
for one variant is how this happened, and finding 3 is the real answer.

---

## 2. Resizability: nothing is resizable except one textarea

**Severity: high — it is the one axis the note named that the client does not address at all.**

- The builder is `grid-template-columns: 15rem 1fr 17rem`, fixed. There are no drag handles
  anywhere in the product.
- The whole client contains exactly one `resize:` declaration — `resize: vertical` on
  `ui/Textarea.tsx`.

Two places where this bites, both routine:

**Room keys do not fit the tree.** Keys are `world.zone.room`, so
`aldenmoor.millbrook.tavern-common` is 33 characters in a 15rem rail. The follow readout already
concedes this with `max-width: 16rem; text-overflow: ellipsis` — the fix was applied to the symptom
in one place rather than to the cause.

**Room descriptions are the longest prose in the product** and are written in a textarea in the
middle column, which cannot be widened. A four-sentence description is what §7.6 says a builder
should not be typing on a command line; the form it was given instead is a fixed box.

**Fix, cheapest first:** make the two builder rails drag-resizable and persist the widths to
`localStorage`. A pointer-driven splitter is roughly the same work as the zone canvas pan already
shipped, and M4a is about to touch Pointer Events anyway. Failing that, `resize: horizontal` on the
rails costs two lines and gets most of it.

---

## 3. There is no shared Button, and no tokens for anything but colour — FIXED

**Severity: high — it is the cause of findings 1, 4, and 5.**

`ui/` has `Field`, `Modal`, `Select`, `Textarea`, `NumberInput`, `Tabs`, `Toast`, `ConfirmDialog`,
`OverflowMenu` — a real primitive set, and it is why the forms are as consistent as they are.
**Button is the omission**, and it is the most-used control in the product: 102 raw `<button>`
elements, styled by whichever of `primary`, `danger`, `danger-button`, or nothing the author
reached for.

The token situation is the same shape. Seventeen custom properties are defined, all colour:
`--accent`, `--bad`, `--bg`, `--border`, `--chat`, `--dim`, `--emote`, `--focus`, `--good`,
`--health`, `--movement`, `--panel`, `--party`, `--speech`, `--stamina`, `--tell`, `--text`.
Colour is therefore the one dimension that is consistent everywhere. There are no tokens for
spacing, type scale, or radius — and those are exactly the three dimensions that have drifted.

**Fixed.** `ui/Button.tsx` takes `variant="quiet" | "primary" | "danger" | "link"`, and all 41
variant call sites went through it. Neutral buttons stay bare `<button>` deliberately: the element
rule already styles them, they have no variant to get wrong, and routing a hundred of them through
a wrapper is churn with a regression surface and no defect behind it. What the component removes is
the case where the intent was "destructive" and the output was "ordinary".

`--space-*`, `--text-*` and `--radius-*` now sit beside the colours.

---

## 4. Layout spacing: 24 distinct values, no scale — FIXED

Every padding, margin, and gap in the client, by frequency:

```
0.5rem ×34   0.6rem ×27   1rem ×21    0.4rem ×19   0.75rem ×14
0.35rem ×13  0.3rem ×10   0.9rem ×9   0.2rem ×9    0.45rem ×8
0.25rem ×8   0.8rem ×7    0.15rem ×6  0.1rem ×5    0.7rem ×4
1.5rem ×3    1.25rem ×3   1.1rem ×3   2rem ×2      0.55rem ×2
0.85rem ×1   3rem ×1      + 0px, 1px, 2px
```

The run from `0.35` to `0.9` in steps of `0.05` is the tell. The difference between `0.45rem` and
`0.5rem` is under a pixel at default type size — invisible on screen, and permanent in the CSS.
Nobody chose these against each other; each was chosen against whatever was on screen at the time.

Type is the same story: **19 distinct font sizes**, and they mix units for the same value —
`0.8rem` (9 uses) and `0.8em` (4 uses) both exist, as do `0.85rem` and `0.85em`. A `rem`/`em` split
is meaningful when it is deliberate and a bug when it is not; here `.preview-table` is `0.82rem`
with `thead th` at `0.72rem`, while `.menu-item` is `0.9em`, so the same visual weight is reached
two different ways in two components.

Radius: `3px` ×9, `4px` ×8, `6px` ×7, `8px` ×2, `999px` ×2, `5px` ×1. Three values doing one job.

**Fixed:** the six-step scale went in and 214 declarations across the stylesheets moved onto it,
covering 21 of the distinct values. Two literals remain on purpose — a `3rem` hero padding above
the top step, and a `0.45rem` inside a `calc()` with `env(safe-area-inset-top)`, where a token
would have to be resolved before the addition.

---

## 5. Letter casing is already right — the unit drift is FIXED

**Casing needs no work.** Every one of ~80 `Field` labels is sentence case, buttons and tab labels
agree, and the two-letter vitals (`HP`, `FO`, `ST`) and initialisms (`XP`) are correctly
capitalised against that. This is the axis the note worried about and the one that is finished.

**Units are where the drift is.** Three conventions in one form system:

| Convention | Where |
|---|---|
| Parenthetical | `Attack delay (pulses)`, `Delay (pulses)`, `Wander (pulses)`, `Weight (grams)` |
| Appended word | `Respawn seconds` |
| Hint only | `Every` / `to` with `hint="seconds, at least"` |

**Fixed:** parenthetical everywhere — `Respawn (seconds)`, `Every (seconds)`, `to (seconds)`. Note
this settles the *convention*, not finding 6: three fields still ask a builder for pulses.

---

## 6. Pulses leak into the builder, and a pulse is not a unit anyone thinks in

**Severity: medium, and the clearest "intuitive" finding.**

A pulse is 250 ms (§2.3) — an engine implementation detail. It reaches the builder in three fields,
while two adjacent fields use seconds:

- `Attack delay (pulses)`, hinted *"Minimum 4 ≈ 1s"*
- `Delay (pulses)`, hinted *"Minimum 4 ≈ 1s"*
- `Wander (pulses)`, unhinted
- `Respawn seconds`
- Emote `Every` / `to`, in seconds

So a builder authoring one mob converts between two time units inside one editor, and the hint that
makes the conversion possible is present on two of the three pulse fields and missing from the
third. The emote fields already prove the better answer: they take seconds and the engine converts.

**Fix:** take seconds everywhere and convert at the boundary, as `MobEmote.FromSeconds` already
does. Where a pulse floor matters (minimum 4), the hint becomes *"at least 1 second"*. The stored
shape need not change.

---

## 7. Keyboard focus on selects and textareas is a 1px border tint — FIXED

**Severity: medium — accessibility, and it fails quietly.**

`ui/ui.css` contains **zero** `:focus-visible` rules and five `outline: none`. Two of them matter:

```css
.textarea:focus { outline: none; border-color: var(--accent); }
.select:focus   { outline: none; border-color: var(--accent); }
```

The substitute for the removed outline is a one-pixel border colour change, on the two most common
controls in the builder. It is also `:focus` rather than `:focus-visible`, so it fires on mouse
click too — which is the reason it was made subtle, and the reason it is now too subtle to serve as
a keyboard indicator. A keyboard-only builder tabbing through the mob editor cannot reliably tell
which control is focused.

The rest is fine: `.dlg:focus { outline: none }` is correct for a dialog container, `.menu-item`
substitutes a background under Radix's roving focus, and the checkbox rules do use `:focus-visible`
with a real 2px outline. That last one is the pattern the other two should copy.

**Fixed** exactly that way: `:focus-visible` gives the keyboard a real 2px ring, and the quiet
`:focus` border stays for the mouse — which is why it was made subtle in the first place, so the two
uses no longer have to share one treatment.

---

## 8. Done: the *Follow my character* checkbox is gone

Removed, as the note asked. It was a setting with one correct position: the follow effect already
moved you only on an actual move, already stood off a form with unsaved edits, and already left you
alone on a room you had clicked. With all three true there was nothing for the off switch to
protect, so the topbar now shows where the character is standing rather than asking whether you
want to know.

`BuilderOutletContext` loses `follow` and `setFollow`, `App` loses a `useState`, and the smoke test
that covered "does not snap back when follow is on" now covers the same property as an unconditional
one — which is the assertion that mattered either way.

---

## What is already good, and should not be traded away

Worth recording so a later pass does not "fix" it:

- **Colour is fully tokenised**, including semantic channel colours for speech, tells, party, and
  chat. This is why the transcript reads well and why theming would be cheap.
- **The `ui/` primitives are real** and `Field` in particular has already collapsed ~40
  hand-written label blocks into one shape. The forms are consistent *because* of it.
- **Sentence case is universal.** See finding 5.
- **`prefers-reduced-motion` is honoured** in both stylesheets.
- **The compact breakpoints are considered**, not incidental: below 768px the builder's first
  column becomes a drawer with a scrim, and the zone canvas is summoned rather than resident — with
  the reasoning written down beside it.
- **Empty and loading states are written as prose**, not spinners — *"Pick a room, or dig one from
  the room you are standing in."* is a better empty state than most shipped products manage.

---

## Suggested order

1. ~~**Finding 1** — four dead `danger` classes.~~ Done: renamed to `danger-button`.
2. ~~**Finding 7** — focus rings on select and textarea.~~ Done: `:focus-visible` gives the
   keyboard a real 2px ring while a mouse click keeps the quiet border.
3. ~~**Finding 5** — unit convention in labels.~~ Done: parenthetical everywhere.
4. **Finding 3, tokens half** — add the spacing, type, and radius tokens without adopting them.
5. **Finding 2** — resizable builder rails. The largest user-visible win, and it wants Pointer
   Events, so it belongs beside Phase 7's M4a rather than before it.
6. **Finding 6** — seconds instead of pulses. Touches the API contract's field names, so it is the
   one worth a moment's thought rather than a quick pass.
7. ~~**Findings 3 and 4, adoption**~~ Done in the same pass: 214 spacing declarations moved onto
   the scale.
