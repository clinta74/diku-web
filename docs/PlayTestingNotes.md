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

- **done**: all three. `XpRelevance` (§4.7) is one window used twice — `min(level / 2, 30)` sets
  both the level below which a mob teaches you nothing and the level below which a party member
  stops sharing, so there is one number to tune rather than two that drift. Between the floor and
  your own level it tapers on a line rather than cutting off, and a zero always says which of the
  two rules produced it.
  Applied *after* zone multipliers, so a generous zone can scale a reward and never resurrect a
  worthless one. A mob's level is floored at its zone's `MinLevel`, because a rat template in a
  level 40 zone with heavy multipliers is a level 40 fight wearing a level 1 label — **the first
  thing that has ever read `Zone.MinLevel`.**
  `attack` is the verb now, with `kill` kept working and out of `help`; deleting it would have
  taken `k` with it. The mobile client's Attack button sent `attack <target>` to a server that had
  no such verb, so that button has been broken since it was written and now works.

- **The world has nothing left to fight at level 6.** Not a bug in the above, but its consequence:
  the only mobs authored are levels 1 and 2, and both zones declare `min_level` 1, so from level 6
  the floor is 3 and every mob in the game pays nothing. Needs content, or bands that say what the
  zones are actually for — `aldenmoor.sunken-crypt` is currently 1–50.