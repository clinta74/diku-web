- ~~attacking a mob and killing it on the first strike the mob can still hit back.~~ **fixed** —
  combat no longer runs a shared round; each combatant swings on its own clock and a death is
  resolved by the blow that caused it, so the rat is out of the fight before it can answer.
A rat appears.
> kill rat
You begin attacking a rat!
You miss a rat.
You miss a rat.
You hit a rat for 16 damage.
A rat hits you for 4 damage.
A rat falls.

- ~~mobs and items need alias name list so that you don't have to type the exact name.~~
  **fixed** — matching is now derived from the name instead of authored, so "old coin" answers to
  `coin`, `old`, `co`, `old coin`, and `old-coin` with no keyword list to maintain. Matches are
  *ranked*, so when several things answer to one word the closest wins: an exact name beats a
  word match, and the last word of a name (the noun) beats a word buried in another name — `dagger`
  finds the rusty dagger, not the dagger hilt. This also tightened mob targeting, which used
  `TemplateKey.EndsWith` and so accepted a bare `t` for "giant-rat", and never looked at the
  display name at all. An explicit per-template alias list is still worth adding for names
  derivation can't reach ("Fang" for a named wolf); the derived matching covers the common case
  without it.

- command line autocomplete would be helpful for item and mob names — **still open**
  - for spawn it should search the correct keys

- ~~need a destroy item command~~ **fixed** — `destroy <item>` removes something you're carrying
  from the world and from storage. The verb must be typed in full (no abbreviation), the way
  `quit` is, since nothing comes back. It refuses two things rather than warning about them:
  anything you're wearing or wielding (`remove` it first), and quest items, which PLAN.md §4.9
  already declares can be neither sold nor destroyed.

- ~~the abilities for character paths that make more sense for the path type. warrior with kick
  bash come up with some options to flush out ablities and spells so that level progression has
  more meaning.~~ **fixed** — and the reason it felt empty turned out to be two bugs rather than
  a thin design. Progression stopped at level 6 for every path, so every level after it granted
  nothing; the level-6 entry (`warden.parry`, `adept.amplify`, `shade.shadowstep`,
  `channeler.restore`) had **no ability row behind it**, so reaching 6 granted something that
  could not be cast; and three abilities that *were* seeded (`warden.battle-fury`, `adept.weaken`,
  `shade.fortify`) appeared in no progression at all, so the entire buff/debuff feature was
  unlearnable.

  There are now **8 abilities per path, unlocking to level 20**, never more than four levels
  apart. The seeder and the progression table both derive from one `AbilityCatalogue`, so they
  cannot drift apart again, and tests assert it: no dangling unlock, no unreachable ability, no
  effect parameter the executor does not read.

  Paths read differently in cost and cadence rather than in mechanics: **Warden** pays stamina
  for reliable hits and self-sustain (Bash → Battle Fury → Parry → Rally → Shield Wall → Crushing
  Blow → Last Stand); **Adept** pays a lot of focus for slow, heavy casts and weakening
  (Weaken → Amplify → Scorch → Enfeeble → Disjunction → Cataclysm); **Shade** pays little and
  strikes fast (Fortify → Shadowstep → Ambush → Vanish → Assassinate → Death Mark);
  **Channeler** mends more than it harms (Enervate → Restore → Blessing → Renewal → Sap →
  Intercession).

  Ability rows are now reconciled on **every** startup rather than seeded once, so an existing
  database picks up abilities added later. Without that, only a fresh database would ever have
  seen these.

  Worth knowing: only four effect executors exist (`damage.physical`, `heal.restore`,
  `buff.damage-up`, `debuff.weaken`), so a genuinely distinct *kick* — a stun, an interrupt, a
  snare — needs a new executor, not just a new row. That is the next real step for combat depth.

- ~~warden slash and parry don't make a lot of sense. Slash could be replaced by a kick. A parry
  is more like an innate ability, warden and shade should have it, and it should passively parry
  an incoming attack.~~ **done** — both were right, and chasing the second one turned up a third
  bug.

  **Slash → Kick.** The level-1 opener assumed a blade: a Warden holding a mace, a staff, or
  nothing at all was still told they slashed. A kick does not care what you are carrying.

  **Parry is a passive now** (`PassiveKeys.Parry`, Warden 4 / Shade 8). It was a castable
  self-heal on a 32-pulse cooldown, which is not what parrying is — and as an ability it had to
  be spent *before* the blow it was meant to stop. It rolls in `CombatSystem` after the attack
  roll but before narration, so it only ever spends itself on a blow that was going to land, and
  the exchange narrates once as a parry rather than as a hit that is quietly undone. Warden 20%,
  Shade 12% — a shield and a braced stance against footwork. Adepts and Channelers never parry.
  Neither do mobs: the chance comes from Path and level, and a mob has neither.

  Warden's level 7 slot became **Sunder** (the target takes 30% more damage), which also gives
  the Path its own use of the debuff effect.

  **The third bug:** `debuff.weaken` sets `IncomingDamageMultiplier`, which scales the damage the
  target *takes*. Every "weaken" I wrote used values *below* 1.0 — so Weaken, Enfeeble, Enervate,
  and Sap all made their target **25–45% harder to kill**. Nothing failed: the spell landed, the
  effect showed on the status screen, and the fight just went worse. `DebuffEffect` now reads
  `outgoingMultiplier` as well (it was hardcoded to 1.0, so the effect could not express "deals
  less damage" at all), the four weakens use it, and a test asserts every debuff moves a
  multiplier the harmful way.

- ~~some commands like examine and stats should have more data for a builder so. It can also have
  a link to send it into the builder screen for a item, mob, or npc.~~ **fixed** — a `TextSpan`
  can now carry a builder path, and the client renders such a span as a link that routes
  internally, so following one keeps the session and the SSE stream alive rather than reloading
  the page.

  **`examine`** also works on mobs now — it only ever looked at items, so examining the NPC in
  front of you reported that it was not there. It reports condition in prose ("bleeding", "badly
  hurt") rather than hit points, says when something is a non-combatant or a shopkeeper, and says
  when an item is quest-bound. For a builder it adds the template key, the resolved numbers, and
  **Open in builder**.

  **`stats`** gains a Builder block: the room key with a link straight to the room editor, the
  zone's difficulty multipliers (so it is clear why a rat here hits harder than the same rat next
  door), and a link per equipped item.

  All of it is builder-only, decided on the server — a player never receives a template key or a
  path, so the builder's existence stays off the wire for anyone who cannot use it. Tests cover
  the gating in both directions, including that a *moderator* does not count as a builder.

- ~~the game map section is a bit spares with only ascii. if extended ascii is used we can get
  borders characters and more symbols like stairs blocks, tables. the default maps should be
  bigger maybe x2 update the existing maps to improve the detail.~~ **fixed** — grids went from
  11×9 to **21×9**, drawn with box-drawing borders instead of hashes, and all twelve starter rooms
  were redrawn. Six of them had no art at all and were rendering as a blank rectangle. The extra
  room is what makes furniture legible: the tavern now has four tables and a bar along the wall,
  the smithy a forge and an anvil, the chapel six rows of benches facing the altar.

  Two supporting changes: the map CSS dropped its letter-spacing, without which box-drawing
  glyphs do not tile and a wall reads as a dotted line; and `NonPlaceableTiles` grew from
  {wall, water} to cover furniture, so nothing is drawn standing on the altar or inside the forge.

  **Builder palette** — the grid painter offered eight ASCII glyphs. It now offers ~55, grouped:
  Ground, Walls (`─│┌┐└┘├┤┬┴┼`), Heavy walls (`═║╔╗╚╝╠╣╦╩╬╪╫`), Solid (`█▓▀▌▐`), Ways through
  (`╥╨╞╡+⌐`), and Furniture (`▬▲▄■◎†♠♣`). Glyphs a room already uses that the palette does not
  offer appear in an "In this room" group, so imported art stays paintable. A new grid starts at
  21×9 to match. The palette's default tile name only seeds the legend on first use — the legend
  editor still renames per room, so `═` can be a wall in one room and a bar in another.

  One constraint worth keeping: **every glyph must be a single BMP character**. The painter writes
  one into a row by index and `RoomLayoutService` reads terrain back per `char`, so an emoji would
  be split across two cells and neither half would draw. Tests assert it on both the palette and
  the seeded art.

  Note: the seeder only runs on a **fresh** database, so an existing world keeps its old art —
  deliberately, since overwriting a builder's rooms would be worse than leaving them plain.

the game map section is a bit spares with only ascii. if extended ascii is used we can get borders characters and more symbols like stairs blocks, tables. the default maps should be bigger maybe x2 update the existing maps to improve the detail. 