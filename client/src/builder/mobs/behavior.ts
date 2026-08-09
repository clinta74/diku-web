/**
 * Reading and writing the parts of a mob's behavior bag that the engine acts on.
 *
 * The bag is deliberately schemaless (PLAN.md §4.8), which is why these are a read/write pair
 * rather than a plain object mapping: the risk in editing it is not a wrong value but a lost
 * one, since a save that rebuilt the bag from the form's own fields would delete every key the
 * form does not render.
 */

export type Disposition = 'passive' | 'aggressive' | 'npc'

/**
 * One line of idle prose and how often it should be heard.
 *
 * The cadence is per line, not per mob: a shopkeeper calling the catch of the day every few
 * minutes is atmosphere, and the same line every few seconds is a reason to leave the room.
 */
export interface EmoteDraft {
  text: string
  minSeconds: number
  maxSeconds: number
}

/** Mirrors `MobEmote.DefaultMinSeconds` / `DefaultMaxSeconds` in the engine. */
export const DEFAULT_EMOTE_MIN_SECONDS = 20
export const DEFAULT_EMOTE_MAX_SECONDS = 60

export interface BehaviorDraft {
  disposition: Disposition
  emotes: EmoteDraft[]
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

/** Reads a number that may have arrived as a JSON string, falling back when it did not. */
function asSeconds(raw: unknown, fallback: number): number {
  const value = typeof raw === 'number' ? raw : Number(raw)
  return Number.isFinite(value) && value > 0 ? Math.round(value) : fallback
}

/**
 * Reads the emote list, which has two authored shapes.
 *
 * A bare string is a line with no opinion about timing and takes the defaults; a row carries its
 * own. Both stay valid because the bag is schemaless and every emote written before timing
 * existed is a bare string — accepting only rows would silence all of them.
 */
function asEmotes(raw: unknown): EmoteDraft[] {
  if (!Array.isArray(raw)) return asStrings(raw).map(toDefaultEmote)

  const emotes: EmoteDraft[] = []

  for (const entry of raw) {
    if (entry !== null && typeof entry === 'object') {
      const row = entry as Record<string, unknown>
      const text = String(row.text ?? '').trim()
      if (text.length === 0) continue

      const minSeconds = asSeconds(row.minSeconds, DEFAULT_EMOTE_MIN_SECONDS)
      emotes.push({
        text,
        minSeconds,
        // A max below the min reads as "exactly the min", matching the engine's clamp rather
        // than showing the builder a range the game will not honour.
        maxSeconds: Math.max(minSeconds, asSeconds(row.maxSeconds, DEFAULT_EMOTE_MAX_SECONDS)),
      })
      continue
    }

    const text = String(entry ?? '').trim()
    if (text.length > 0) emotes.push(toDefaultEmote(text))
  }

  return emotes
}

function toDefaultEmote(text: string): EmoteDraft {
  return {
    text,
    minSeconds: DEFAULT_EMOTE_MIN_SECONDS,
    maxSeconds: DEFAULT_EMOTE_MAX_SECONDS,
  }
}

/** Pulls the keys the engine reads out of a stored behavior bag. */
export function readBehavior(behavior: Record<string, unknown> | undefined): BehaviorDraft {
  const type = String(behavior?.type ?? 'passive').toLowerCase()
  return {
    disposition: type === 'aggressive' || type === 'npc' ? type : 'passive',
    emotes: asEmotes(behavior?.emotes),
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

  // Blank rows are dropped rather than saved: a half-filled row would reach the engine as an
  // emote with no text, and "a rat ." is worse than silence.
  const emotes = draft.emotes.filter((e) => e.text.trim().length > 0)

  if (emotes.length > 0) {
    // Written in the simplest shape that carries the meaning. A line left at the default cadence
    // stays a bare string, so turning the dial is visible in the stored bag and content authored
    // before timing existed round-trips unchanged.
    next.emotes = emotes.map((emote) =>
      emote.minSeconds === DEFAULT_EMOTE_MIN_SECONDS &&
      emote.maxSeconds === DEFAULT_EMOTE_MAX_SECONDS
        ? emote.text.trim()
        : {
            text: emote.text.trim(),
            minSeconds: emote.minSeconds,
            maxSeconds: Math.max(emote.minSeconds, emote.maxSeconds),
          },
    )
  } else {
    delete next.emotes
  }

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
