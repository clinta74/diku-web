import type { ContentEntry, MapEntity } from '../net/protocol'

/**
 * How a player is drawn to themselves.
 *
 * The server sends one room to everybody in it — the same map, the same contents, the same bytes —
 * because laying a room out once per occupant made refreshing it quadratic in how many people were
 * standing there, and at two hundred crowded sessions that was enough to stop the game loop keeping
 * its own schedule (PLAN.md §11). Whose screen this is, is the one thing the server cannot know
 * without personalising, and the one thing the client already knows.
 *
 * So the marking lives here. It is presentation, and it always was.
 */
export const SELF_ICON = '@'
export const SELF_LABEL = 'you'

/**
 * Marks the viewer on a room map.
 *
 * Matched on `type === 'player'` as well as the name, which the server could not do as cheaply:
 * a mob or an item that happened to share a character's name can no longer be mistaken for them.
 *
 * Returns the entities unchanged when the viewer is not among them — an unlit room draws nobody,
 * and a room the player has just left still arrives once.
 */
export function markSelfOnMap(entities: MapEntity[], characterName: string): MapEntity[] {
  if (!characterName) return entities

  const index = entities.findIndex(
    (entity) => entity.type === 'player' && entity.label === characterName,
  )

  if (index < 0) return entities

  const marked = entities.slice()
  marked[index] = { ...marked[index], icon: SELF_ICON, label: SELF_LABEL }
  return marked
}

/**
 * Marks the viewer in the room's contents list.
 *
 * **The keyword is deliberately untouched.** It is what a tapped verb sends, so relabelling the
 * entry must not change the name the game is asked about — `look you` is not a command, and the
 * server was careful about this for the same reason before the marking moved here.
 *
 * Matched case-insensitively against the keyword, which is a lowercased character name. There is
 * no `type` to narrow on in a contents entry, so a mob keyed exactly on the player's name would
 * still match — which is what the server did too, and character names are unique enough that it
 * has never come up.
 */
export function markSelfInContents(entries: ContentEntry[], characterName: string): ContentEntry[] {
  if (!characterName) return entries

  const wanted = characterName.toLowerCase()
  const index = entries.findIndex((entry) => entry.keyword.toLowerCase() === wanted)

  if (index < 0) return entries

  const marked = entries.slice()
  marked[index] = { ...marked[index], icon: SELF_ICON, label: SELF_LABEL }
  return marked
}
