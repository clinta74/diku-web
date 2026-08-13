# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

- Should track changes in a changelog or use the github release functionality?

- the ability cooldowns are retuned (§4.5) — every one is now a whole number of the 2s combat
  beat, and length follows impact. The bigger find was that 10 of 11 buffs/debuffs/DoTs lasted
  longer than their own cooldown, so they were permanently maintainable and the cooldown did
  nothing. Still open: **showing pending cooldowns**, which needs two new SSE events because the
  client has never been told what abilities a character has.

- the UX evaluation is written up in [UX.md](UX.md) — eight findings, one of them a live defect
  (four Remove buttons that ask for a CSS class nobody wrote). The Follow my character checkbox is
  gone. The rest is a fix list at the end of that document, not yet done.

