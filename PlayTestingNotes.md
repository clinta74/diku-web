# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

_Nothing outstanding._

Abilities now open a fight and close one. `kick rat` out of combat starts the fight properly — the
Warden keeps swinging afterwards instead of standing there — and a mob killed by an ability dies
the same way one killed by a swing does: experience, gold, loot, corpse, and a fight that ends.

Two things the sweep found that were not in the note:

- **A bleed applied out of combat never ticked.** Engagement was keyed on damage dealt, and Ambush
  deals nothing on application — the whole ability is the wound. So the Shade's opener engaged
  nothing, and wounds only tick inside a fight. Engagement now keys on the effect being hostile at
  all, which also covers a stun or a snare used to open.
- **A player killed by a spell outside a fight** was never noticed either: only mobs were engaged,
  so in a `pvp` room a Bolt could take a character to zero health with nothing watching for the
  death. A hostile ability now opens the duel the way `kill` does.
