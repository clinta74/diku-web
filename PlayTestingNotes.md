# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

_Nothing outstanding._ The queue lives in [BUGS.md](BUGS.md).

The zombie that walked out of its own fight was two bugs compounding, and both are fixed. Nothing
wanders out of a fight now; and when the other party does leave — by fleeing, by dying, by any
route — the fight lets go of everyone properly and says so. The plan library reads clean, with a
rat wandering *in* mid-fight to prove the guard stopped the right mob rather than freezing the zone.

A group fight now ends for everyone. You were right, and it was worse than "some players may stay
in combat": in a group it **never** ended. The rule counted heads rather than sides — two or more
combatants meant a fight — which is correct for exactly one shape, one player against one mob,
where the mob dying leaves one combatant. Two players on one mob is three; the mob dies, two
remain, the count is still two. Everyone left was stuck `Fighting`, refused every later `kill` and
unable to walk out of the room. It scaled with the party, and it was invisible solo, which is how
it lasted this long.

A fight is now over when nobody left in it still has somebody to hit, which is the same question
the loop already asks before swinging. A duel stays live, a taunted player who never chose a target
is still let go at the end, and one of two mobs dying does not end anything.

Earlier: abilities now open a fight and close one. `kick rat` out of combat starts the fight
properly — the Warden keeps swinging afterwards instead of standing there — and a mob killed by an
ability dies the same way one killed by a swing does: experience, gold, loot, corpse, and a fight
that ends. Two things that sweep found which were not in the note:

- **A bleed applied out of combat never ticked.** Engagement was keyed on damage dealt, and Ambush
  deals nothing on application — the whole ability is the wound. So the Shade's opener engaged
  nothing, and wounds only tick inside a fight. Engagement now keys on the effect being hostile at
  all, which also covers a stun or a snare used to open.
- **A player killed by a spell outside a fight** was never noticed either: only mobs were engaged,
  so in a `pvp` room a Bolt could take a character to zero health with nothing watching for the
  death. A hostile ability now opens the duel the way `kill` does.


- the mob attacks section has the attack also section split a pary as you add more attack types. This section should
be combined with the attack section as well and layed out to make it more intuitive on how to use it.