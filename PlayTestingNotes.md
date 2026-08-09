# Play testing notes

Add anything noticed while playing here. Cleared as items are done.

_Nothing outstanding._

Abilities are now verbs: `kick rat`, not `cast kick rat`. `cast` is for spells only and refuses a
skill, naming the verb form instead. The moderation commands moved to `kickplayer`, `banplayer`,
`muteplayer`, `unbanplayer`, `unmuteplayer` so they cannot shadow an ability.

Two things the sweep found that were not in the note: **eight multi-word abilities were
unreachable by name entirely** — `cast shield bash rat` parsed as "cast Shield at bash", so Shield
Bash, Crushing Blow, Last Stand, Arcane Shield, Battle Fury, Death Mark, Quick Strike, and Shield
Wall could only be used by typing the hyphenated key. And `abilities` printed raw keys like
`warden.shield-bash`, teaching an implementation detail as though it were the thing to type; it
now lists spells and skills separately, by name, with costs.
