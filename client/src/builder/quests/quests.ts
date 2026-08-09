import type { Quest, StorylineNode } from '../../net/builderApi'

/**
 * The four dialogue strings, with what the engine says when one is left blank.
 *
 * The fallbacks are transcribed from `QuestCommands`, which is what makes an empty field
 * honest rather than a hole: a builder can see what the NPC will say and decide the generated
 * line is fine. They were not visible anywhere before, so the only way to know was to read the
 * engine.
 */
export interface DialogueField {
  key: string
  label: string
  hint: string
  /** What the engine narrates when this key is absent. `{...}` marks interpolated quest data. */
  fallback: string
}

export const DIALOGUE_FIELDS: DialogueField[] = [
  {
    key: 'giverOffer',
    label: 'Offer',
    hint: 'When the giver hands the quest over. Also used to re-offer a repeatable one.',
    fallback: 'I have a job for you: {summary}',
  },
  {
    key: 'giverInProgress',
    label: 'In progress',
    hint: 'Talking to the giver again while the quest is still open.',
    fallback: 'Still working on {name}?',
  },
  {
    key: 'giverComplete',
    label: 'Already done',
    hint: 'Talking to the giver after finishing it. Never shown for a repeatable quest.',
    fallback: "You've already completed {name}.",
  },
  {
    key: 'turninReady',
    label: 'Turn-in',
    hint: 'When the turn-in NPC accepts the item and pays out.',
    fallback: "Excellent work! You've completed {name}.",
  },
]

/**
 * Splits a comma- or newline-separated list of quest keys.
 *
 * Free text rather than a picker, because a prerequisite can name a quest that does not exist
 * yet - authoring a chain backwards is normal, and §7.4 says content is wired before its targets
 * exist. The storyline graph is what reports a key that never turns up.
 */
export function parseKeyList(raw: string): string[] {
  return [...new Set(
    raw
      .split(/[,\n]/)
      .map((part) => part.trim())
      .filter((part) => part.length > 0),
  )]
}

export function formatKeyList(keys: readonly string[]): string {
  return keys.join(', ')
}

/**
 * A blank quest, for the create dialog.
 *
 * Deliberately not `{}`. The server fills omitted fields from the existing row on a PATCH and
 * from defaults on a POST, but a create that sent nothing would leave the giver and turn-in
 * empty - which is exactly the dormant state (§5.2d) a quest should not be born in.
 */
export function newQuest(key: string, name: string, zoneKey: string): Partial<Quest> {
  return {
    zoneKey,
    name: name || key,
    summary: '',
    description: '',
    giverMobKey: '',
    turninMobKey: '',
    requiredItemKey: null,
    requiredCount: 1,
    rewardXp: 0,
    rewardGold: 0,
    rewardItemKey: null,
    rewardItemCount: 1,
    prerequisiteQuestKeys: [],
    isRepeatable: false,
    dialogue: {},
    sortOrder: 0,
  }
}

/**
 * Orders storyline nodes so prerequisites come before what they unlock, and reports the depth
 * of each - which is what lets the graph be drawn as indented text rather than as a canvas.
 *
 * A cycle has no valid order by definition. Rather than looping or dropping its members, whatever
 * is left when no more progress can be made is appended at depth zero: the members of a cycle are
 * reported separately anyway, and silently omitting them would hide the thing the reader most
 * needs to see.
 */
export function chainOrder(
  nodes: readonly StorylineNode[],
  edges: readonly { from: string; to: string }[],
): Array<{ node: StorylineNode; depth: number }> {
  const prerequisites = new Map<string, string[]>()
  for (const node of nodes) {
    prerequisites.set(node.key, [])
  }
  for (const edge of edges) {
    prerequisites.get(edge.to)?.push(edge.from)
  }

  const depth = new Map<string, number>()
  const ordered: Array<{ node: StorylineNode; depth: number }> = []
  const remaining = [...nodes]

  let progressed = true
  while (progressed && remaining.length > 0) {
    progressed = false

    for (let i = remaining.length - 1; i >= 0; i--) {
      const node = remaining[i]
      const needs = prerequisites.get(node.key) ?? []

      if (!needs.every((k) => depth.has(k))) {
        continue
      }

      const own = needs.length === 0 ? 0 : Math.max(...needs.map((k) => depth.get(k)! + 1))
      depth.set(node.key, own)
      ordered.push({ node, depth: own })
      remaining.splice(i, 1)
      progressed = true
    }
  }

  for (const node of remaining) {
    ordered.push({ node, depth: 0 })
  }

  return ordered.sort((a, b) => a.depth - b.depth || a.node.key.localeCompare(b.node.key))
}
