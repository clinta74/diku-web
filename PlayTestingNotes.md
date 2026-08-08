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

- mobs and items need alias name list so that you don't have to type the exact name.
- command line autocomplete would be helpful for item and mob names
  - for spawn it should search the correct keys

- ~~need a destroy item command~~ **fixed** — `destroy <item>` removes something you're carrying
  from the world and from storage. The verb must be typed in full (no abbreviation), the way
  `quit` is, since nothing comes back. It refuses two things rather than warning about them:
  anything you're wearing or wielding (`remove` it first), and quest items, which PLAN.md §4.9
  already declares can be neither sold nor destroyed.

the abilities for character paths that make more sense for the path type. warrior with kick bash come up with some options to flush out ablities and spells so that level progression has more meaning.