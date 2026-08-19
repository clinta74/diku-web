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
  /**
   * What this mob says when a player uses `talk` on it. A list, picked from in turn, so a mob
   * spoken to twice does not answer the same way — the same reason `emotes` is a list.
   *
   * Plain strings rather than the timed rows emotes accept: a greeting has no cadence, because it
   * happens when somebody speaks rather than on a schedule.
   */
  greeting: string[]
  shopkeeper: boolean
  sells: string[]
  /** How far over base value this shop prices its stock: 0.1 is 1.1x. Zero is base price. */
  markup: number
  /** Whether this mob moves between rooms at all. A spawner can override it per placement. */
  wanders: boolean
  /** Whether it may cross out of the zone it spawned in. Only meaningful when it wanders. */
  roams: boolean
}

/**
 * The asking price for an item at a shop set to this markup — mirrors `ShopPricing.Price`
 * (PLAN.md §4.13).
 *
 * Duplicated in the client on purpose, and only for the preview beside the stock list. The server
 * is the authority on what a player pays; this exists so a builder turning the dial sees what it
 * does before saving, which is the same argument §7.5 makes for the multiplier preview.
 */
export function shopPrice(baseValue: number, markup: number): number {
  const base = Math.max(0, Math.trunc(baseValue))
  if (!Number.isFinite(markup) || markup <= 0) return base

  // Rounded to six places before the ceiling, which the server does not need to do: it holds the
  // markup as a `decimal`, where 100 x 1.1 is 110. In binary floating point it is
  // 110.00000000000001, and a ceiling turns that into 111 — a preview a gold over what the shop
  // actually charges, which is worse than no preview at all. Six places is far below any markup
  // a builder would type and far above the error being cancelled.
  const raised = Number((base * (1 + markup)).toFixed(6))

  return Math.max(Math.ceil(raised), base + 1)
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

/** Reads the markup, which may have arrived as a JSON string. Anything else is base price. */
function asMarkup(raw: unknown): number {
  const value = typeof raw === 'number' ? raw : Number(raw)
  return Number.isFinite(value) && value > 0 ? value : 0
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
    greeting: asStrings(behavior?.greeting),
    shopkeeper: behavior?.shopkeeper === true || behavior?.shopkeeper === 'true',
    sells: asStrings(behavior?.sells),
    // Absent, unreadable, or negative all mean base price, matching the engine: §4.13 keeps
    // discounting out, so a negative is a typo rather than a cheaper shop.
    markup: asMarkup(behavior?.markup),
    // Absent means it stays put, matching the engine: most authored mobs are shopkeepers, quest
    // givers, and guards that belong somewhere, and a quest giver that wandered off because a key
    // was missing is worse than a rat that failed to.
    wanders: behavior?.wanders === true || behavior?.wanders === 'true',
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

  // Same rule as emotes: blank rows are dropped, and an empty list removes the key rather than
  // storing one that says nothing.
  const greeting = draft.greeting.map((line) => line.trim()).filter((line) => line.length > 0)

  if (greeting.length > 0) next.greeting = greeting
  else delete next.greeting

  if (draft.shopkeeper) {
    next.shopkeeper = true
    next.sells = draft.sells

    // Written only when it says something, like `roams`. Persisting a zero would store the
    // default as though a builder had decided on it.
    if (draft.markup > 0) next.markup = draft.markup
    else delete next.markup
  } else {
    delete next.shopkeeper
    delete next.sells
    delete next.markup
  }

  // Written only when true, for the same reason as `roams`: the engine reads a missing key as
  // "stays put", so persisting `wanders: false` would store the default as though it were a
  // decision somebody made.
  if (draft.wanders) next.wanders = true
  else delete next.wanders

  // Written only when true. The engine reads a missing key as "stays home", so persisting
  // `roams: false` would store the default as though it were a decision.
  //
  // Cleared along with `wanders`, because roaming is a statement about *where* a mob may wander
  // and means nothing about one that does not. Leaving it set would make a mob that is given
  // wandering back later silently cross zone borders on the strength of a tick nobody remembers.
  if (draft.wanders && draft.roams) next.roams = true
  else delete next.roams

  return next
}
