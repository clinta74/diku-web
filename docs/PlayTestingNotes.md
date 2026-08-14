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
