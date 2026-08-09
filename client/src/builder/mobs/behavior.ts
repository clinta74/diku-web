/**
 * Reading and writing the parts of a mob's behavior bag that the engine acts on.
 *
 * The bag is deliberately schemaless (PLAN.md §4.8), which is why these are a read/write pair
 * rather than a plain object mapping: the risk in editing it is not a wrong value but a lost
 * one, since a save that rebuilt the bag from the form's own fields would delete every key the
 * form does not render.
 */

export type Disposition = 'passive' | 'aggressive' | 'npc'

export interface BehaviorDraft {
  disposition: Disposition
  emotes: string[]
  shopkeeper: boolean
  sells: string[]
  roams: boolean
}

export const DISPOSITIONS: Array<{ value: Disposition; label: string; hint: string }> = [
  { value: 'passive', label: 'Passive', hint: 'Minds its own business, but fights back.' },
  { value: 'aggressive', label: 'Aggressive', hint: 'Attacks anyone who walks in.' },
  { value: 'npc', label: 'NPC', hint: 'Cannot fight and cannot be killed.' },
]

/**
 * Reads a value that may have arrived as JSON rather than as the type it was written as, and
 * may have been hand-authored as a bare string where a list was meant.
 */
function asStrings(raw: unknown): string[] {
  if (Array.isArray(raw)) return raw.map((v) => String(v).trim()).filter((v) => v.length > 0)
  if (typeof raw === 'string' && raw.trim().length > 0) return [raw.trim()]
  return []
}

/** Pulls the keys the engine reads out of a stored behavior bag. */
export function readBehavior(behavior: Record<string, unknown> | undefined): BehaviorDraft {
  const type = String(behavior?.type ?? 'passive').toLowerCase()
  return {
    disposition: type === 'aggressive' || type === 'npc' ? type : 'passive',
    emotes: asStrings(behavior?.emotes),
    shopkeeper: behavior?.shopkeeper === true || behavior?.shopkeeper === 'true',
    sells: asStrings(behavior?.sells),
    // Absent means confined, matching the engine: a mob stays in the zone it spawned in unless
    // something says otherwise, so forgetting the key is the safe direction.
    roams: behavior?.roams === true || behavior?.roams === 'true',
  }
}

/**
 * Folds the draft back into the stored bag.
 *
 * Unknown keys are preserved, so a save cannot quietly delete a newer build's content. Keys the
 * form *does* own are removed when they carry no meaning, so a mob that stopped being a
 * shopkeeper does not keep a stale stock list nothing displays.
 */
export function writeBehavior(
  existing: Record<string, unknown> | undefined,
  draft: BehaviorDraft,
): Record<string, unknown> {
  const next: Record<string, unknown> = { ...(existing ?? {}) }

  next.type = draft.disposition

  if (draft.emotes.length > 0) next.emotes = draft.emotes
  else delete next.emotes

  if (draft.shopkeeper) {
    next.shopkeeper = true
    next.sells = draft.sells
  } else {
    delete next.shopkeeper
    delete next.sells
  }

  // Written only when true. The engine reads a missing key as "stays home", so persisting
  // `roams: false` would store the default as though it were a decision.
  if (draft.roams) next.roams = true
  else delete next.roams

  return next
}
