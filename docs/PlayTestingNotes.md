- listing in a shop there is no way to know if an item is better than the one you have without buying it.
- empty slots should be shown in inventory

You are wearing/wielding:
  [Head] helm of vision
  [Chest] a scaled mantle
  [Hands] a pair of measurer's gloves
  [Legs] a plated skirt
  [MainHand] a measured oathmaul (q)
  [OffHand] a keening blade

- stats command should have a friendlier equipped bonus
    Equipped Bonuses:
  [Head] helm of vision
    armor=10, defense=1
  [Chest] a scaled mantle
    armor=26
  [Hands] a pair of measurer's gloves
    armor=8
  [Legs] a plated skirt
    armor=16
  [MainHand] a measured oathmaul
    bonus=4, damageMax=13, damageMin=7
  [OffHand] a keening blade
    bonus=5, damageMax=8, damageMin=4

---

## Done: all three were the same bug

**The player was being shown the builder's view of an item, or no view at all.** `Describe` prints a
stat bag as `key=value` and belongs under `examine`'s builder block, where it is exactly right. It
had leaked onto the `stats` screen. The shop had the opposite failure and showed nothing but a
price.

`ItemStatLine` is the player's reading of the same six numbers, and it sits beside
`EquipmentResolver` on purpose — every key it names is a key that class acts on, so the wording has
to keep step with what the number does. **`bonus` is accuracy, not damage**, which is the one the
old screen hid worst: `bonus=4` beside a damage range reads as more damage and is not.

The vocabulary is closed — six keys, and `BundleValidator` errors on a seventh — so a test asserts
that every stat the engine reads produces a word, and that a movement in any of them shows up in a
comparison. A number that changed a fight and appeared on no screen is how `armor`, `bonus` and
`defense` stayed hidden once before (BUGS.md #11).

**Empty slots.** Your transcript shows six; there are eight. Nothing told you Feet and Trinket
existed — a whole equipment category discoverable only by happening to pick one up. Every slot now
prints, filled or not, and the pack listing carries each item's numbers too, since that is where
somebody deciding what to keep is looking.

The `items.Count == 0` early return used to build the block and then discard it, so **the one player
who most needs to be told there are eight slots — the one who has filled none of them — was the
only player never shown them.** It now appends instead.

**The shop compares.** Slot, what it does, and what changes against the piece it would replace:

```
/ a measured oathmaul: 420 gold
    main hand - Damage 7-13, +4 to hit
    Against your a keening blade: +4 damage, -1 to hit.
```

It stops short of saying *better*, because three more damage for one less accuracy is a trade and
calling that an upgrade would be guessing on your behalf. Naming what moves was the part that was
missing — the arithmetic, not the judgement.

Comparing the shelf's `BaseStats` against a worn item's `ResolvedStats` is honest only because
`ItemSpawner` copies one into the other untouched; only `Value` is multiplied. If item stats ever
start scaling at spawn, that comparison has to resolve the template first or it will quietly quote
the wrong numbers. Said out loud in the code, where it would be found.

**Not done: `examine` on shop stock.** It reaches your pack, the floor and the people in the room,
and stock is a template that was never spawned. The listing now carries everything actionable, so a
second examine path for templates would mostly duplicate `ExamineItem` and drift from it. Say if
the description is wanted on the shelf as well.
