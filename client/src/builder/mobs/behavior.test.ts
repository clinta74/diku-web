import { describe, expect, it } from 'vitest'
import {
  DEFAULT_EMOTE_MAX_SECONDS,
  DEFAULT_EMOTE_MIN_SECONDS,
  readBehavior,
  shopPrice,
  writeBehavior,
} from './behavior'

/**
 * The behavior bag is schemaless and shared, so the risk in editing it is not a wrong value but
 * a lost one: a save that rewrites the bag from the form's own fields deletes whatever the form
 * does not render. These cover the read/write pair as inverses, and pin the preservation rule.
 */
describe('readBehavior', () => {
  it('defaults an empty bag to a passive non-shopkeeper', () => {
    const draft = readBehavior(undefined)

    expect(draft).toEqual({
      disposition: 'passive',
      emotes: [],
      shopkeeper: false,
      sells: [],
      markup: 0,
      wanders: false,
      roams: false,
    })
  })

  it('reads a missing wanders key as staying put', () => {
    // The safe direction, and the one the engine takes. Most authored mobs are shopkeepers and
    // quest givers, so a missing key that meant "wanders" would send exactly the mobs a player
    // needs to find walking off.
    expect(readBehavior({}).wanders).toBe(false)
    expect(readBehavior({ wanders: true }).wanders).toBe(true)
  })

  it('reads a missing roams key as confined to its zone', () => {
    // The safe direction, and the one the engine takes: a mob with nothing said about it stays
    // in the zone whose multipliers gave it its numbers.
    expect(readBehavior({}).roams).toBe(false)
    expect(readBehavior({ roams: true }).roams).toBe(true)
  })

  it('reads the three dispositions', () => {
    expect(readBehavior({ type: 'aggressive' }).disposition).toBe('aggressive')
    expect(readBehavior({ type: 'npc' }).disposition).toBe('npc')
    expect(readBehavior({ type: 'passive' }).disposition).toBe('passive')
  })

  it('falls back to passive for a word it does not recognise', () => {
    // A typo must not leave the editor showing a disposition the engine will not honour.
    expect(readBehavior({ type: 'aggresive' }).disposition).toBe('passive')
  })

  it('matches a disposition regardless of case', () => {
    expect(readBehavior({ type: 'NPC' }).disposition).toBe('npc')
  })

  it('reads a stocked shop', () => {
    const draft = readBehavior({ shopkeeper: true, sells: ['bread', 'torch'] })

    expect(draft.shopkeeper).toBe(true)
    expect(draft.sells).toEqual(['bread', 'torch'])
  })

  it('drops blank entries so a half-filled row is not stock', () => {
    expect(readBehavior({ sells: ['bread', '', '  '] }).sells).toEqual(['bread'])
  })

  it('reads a bare string where a list was expected as one entry', () => {
    expect(readBehavior({ emotes: 'snarls' }).emotes).toEqual([
      { text: 'snarls', minSeconds: DEFAULT_EMOTE_MIN_SECONDS, maxSeconds: DEFAULT_EMOTE_MAX_SECONDS },
    ])
  })

  it('gives a bare emote the default cadence', () => {
    // Every emote written before timing existed is a bare string. Refusing them would silence
    // all of them.
    expect(readBehavior({ emotes: ['squeaks'] }).emotes).toEqual([
      { text: 'squeaks', minSeconds: DEFAULT_EMOTE_MIN_SECONDS, maxSeconds: DEFAULT_EMOTE_MAX_SECONDS },
    ])
  })

  it('reads an emote row that carries its own cadence', () => {
    const draft = readBehavior({
      emotes: [{ text: 'has the best fish in town', minSeconds: 120, maxSeconds: 300 }],
    })

    expect(draft.emotes).toEqual([
      { text: 'has the best fish in town', minSeconds: 120, maxSeconds: 300 },
    ])
  })

  it('reads the two shapes mixed in one list', () => {
    // Which is what a template looks like the moment one line's timing is edited.
    const draft = readBehavior({ emotes: ['squeaks', { text: 'gnaws', minSeconds: 5, maxSeconds: 9 }] })

    expect(draft.emotes.map((e) => e.text)).toEqual(['squeaks', 'gnaws'])
    expect(draft.emotes[0].minSeconds).toBe(DEFAULT_EMOTE_MIN_SECONDS)
    expect(draft.emotes[1].minSeconds).toBe(5)
  })

  it('drops an emote row with no text', () => {
    expect(readBehavior({ emotes: [{ minSeconds: 5 }, 'squeaks'] }).emotes).toHaveLength(1)
  })

  it('reads a range that ends before it starts as exactly the lower number', () => {
    // Matching the engine's clamp, rather than showing a range the game will not honour.
    expect(readBehavior({ emotes: [{ text: 'squeaks', minSeconds: 30, maxSeconds: 10 }] }).emotes)
      .toEqual([{ text: 'squeaks', minSeconds: 30, maxSeconds: 30 }])
  })
})

describe('writeBehavior', () => {
  it('round-trips a draft through read', () => {
    const stored = { type: 'npc', emotes: ['bows'], shopkeeper: true, sells: ['bread'] }

    expect(readBehavior(writeBehavior(stored, readBehavior(stored)))).toEqual(readBehavior(stored))
  })

  it('preserves keys the form does not render', () => {
    // The bag is the extensibility point. A save must not delete a key a newer build wrote.
    const stored = { type: 'passive', patrolRoute: ['a', 'b'], mood: 7 }

    const next = writeBehavior(stored, readBehavior(stored))

    expect(next.patrolRoute).toEqual(['a', 'b'])
    expect(next.mood).toBe(7)
  })

  it('clears the stock when a mob stops keeping a shop', () => {
    // Otherwise the mob keeps a stale stock list that nothing in the UI shows any more.
    const stored = { shopkeeper: true, sells: ['bread'] }

    const next = writeBehavior(stored, { ...readBehavior(stored), shopkeeper: false })

    expect(next.shopkeeper).toBeUndefined()
    expect(next.sells).toBeUndefined()
  })

  it('removes the emote key entirely when the last emote is deleted', () => {
    const stored = { emotes: ['snarls'] }

    const next = writeBehavior(stored, { ...readBehavior(stored), emotes: [] })

    expect(next.emotes).toBeUndefined()
  })

  it('writes a default-cadence emote back as a bare string', () => {
    // The simplest shape that carries the meaning, so turning the dial is visible in the stored
    // bag and content written before timing existed round-trips unchanged.
    const stored = { emotes: ['snarls'] }

    expect(writeBehavior(stored, readBehavior(stored)).emotes).toEqual(['snarls'])
  })

  it('writes a row once the cadence is not the default', () => {
    const next = writeBehavior(
      {},
      {
        ...readBehavior({}),
        emotes: [{ text: 'announces the catch', minSeconds: 120, maxSeconds: 300 }],
      },
    )

    expect(next.emotes).toEqual([
      { text: 'announces the catch', minSeconds: 120, maxSeconds: 300 },
    ])
  })

  it('drops a blank emote row rather than saving it', () => {
    // A half-filled row would reach the engine as an emote with no text.
    const next = writeBehavior(
      {},
      {
        ...readBehavior({}),
        emotes: [
          { text: '  ', minSeconds: 5, maxSeconds: 9 },
          { text: 'squeaks', minSeconds: DEFAULT_EMOTE_MIN_SECONDS, maxSeconds: DEFAULT_EMOTE_MAX_SECONDS },
        ],
      },
    )

    expect(next.emotes).toEqual(['squeaks'])
  })

  it('round-trips a timed emote through read', () => {
    const stored = { emotes: [{ text: 'gnaws', minSeconds: 5, maxSeconds: 9 }] }

    expect(readBehavior(writeBehavior(stored, readBehavior(stored))).emotes)
      .toEqual(readBehavior(stored).emotes)
  })

  it('always writes a disposition so the engine never has to guess', () => {
    expect(writeBehavior({}, readBehavior({})).type).toBe('passive')
  })

  it('writes the stock as an array of item keys', () => {
    const next = writeBehavior(
      {},
      {
        disposition: 'npc',
        emotes: [],
        shopkeeper: true,
        sells: ['bread', 'torch'],
        markup: 0,
        wanders: false,
        roams: false,
      },
    )

    expect(next.sells).toEqual(['bread', 'torch'])
    expect(next.shopkeeper).toBe(true)
  })

  it('writes wanders only when it is on', () => {
    // Absent is the engine's default, so storing `wanders: false` would record the default as
    // though a builder had chosen it — and a later change of default would silently not apply.
    const off = writeBehavior({}, readBehavior({}))
    expect(off.wanders).toBeUndefined()

    const on = writeBehavior({}, { ...readBehavior({}), wanders: true })
    expect(on.wanders).toBe(true)
  })

  it('writes roams only when it is on, and only for a mob that wanders', () => {
    const off = writeBehavior({}, readBehavior({}))
    expect(off.roams).toBeUndefined()

    const on = writeBehavior({}, { ...readBehavior({}), wanders: true, roams: true })
    expect(on.roams).toBe(true)
  })

  it('does not write roams for a mob that does not wander', () => {
    // Roaming says *where* a mob may wander, so it means nothing about one that never moves.
    // Left set, it would come back into force the day somebody ticks "wanders" — a mob crossing
    // zone borders on the strength of a tick nobody remembers making.
    const next = writeBehavior({}, { ...readBehavior({}), wanders: false, roams: true })

    expect(next.roams).toBeUndefined()
  })

  it('clears roams when a mob is told to stay home again', () => {
    const stored = { wanders: true, roams: true }
    const next = writeBehavior(stored, { ...readBehavior(stored), roams: false })

    expect(next.roams).toBeUndefined()
    expect(next.wanders).toBe(true)
  })

  it('clears roams when a wandering mob is told to stand still', () => {
    const stored = { wanders: true, roams: true }
    const next = writeBehavior(stored, { ...readBehavior(stored), wanders: false })

    expect(next.wanders).toBeUndefined()
    expect(next.roams).toBeUndefined()
  })

  it('writes a markup only when it is a shop, and only when it says something', () => {
    // Same rule as roams: a stored zero would record the default as though it were a decision,
    // and a markup on a mob that keeps no shop is a key nothing will ever read.
    expect(writeBehavior({}, { ...readBehavior({}), shopkeeper: true, markup: 0 }).markup)
      .toBeUndefined()

    expect(writeBehavior({}, { ...readBehavior({}), shopkeeper: true, markup: 0.25 }).markup)
      .toBe(0.25)

    expect(writeBehavior({}, { ...readBehavior({}), shopkeeper: false, markup: 0.25 }).markup)
      .toBeUndefined()
  })

  it('clears the markup when a mob stops keeping a shop', () => {
    const stored = { shopkeeper: true, sells: ['bread'], markup: 0.5 }

    const next = writeBehavior(stored, { ...readBehavior(stored), shopkeeper: false })

    expect(next.markup).toBeUndefined()
  })
})

describe('the markup', () => {
  it('reads a missing, unreadable, or negative markup as base price', () => {
    // §4.13 keeps discounting out, so a negative is a typo rather than a cheaper shop.
    expect(readBehavior({}).markup).toBe(0)
    expect(readBehavior({ markup: 'dear' }).markup).toBe(0)
    expect(readBehavior({ markup: -0.5 }).markup).toBe(0)
  })

  it('reads a markup that arrived as a JSON string', () => {
    // The bag round-trips through jsonb, which is where every reader in this file has been bitten.
    expect(readBehavior({ markup: '0.25' }).markup).toBe(0.25)
  })

  it('prices at base value when there is no markup', () => {
    expect(shopPrice(5, 0)).toBe(5)
    expect(shopPrice(5, -1)).toBe(5)
  })

  it('rounds up to the next whole gold and never adds less than one', () => {
    // The case from play: at 1.1x a 1 gold loaf costs 2, not 1.
    expect(shopPrice(1, 0.1)).toBe(2)
    expect(shopPrice(5, 0.1)).toBe(6)
    expect(shopPrice(10, 0.1)).toBe(11)
    expect(shopPrice(100, 0.1)).toBe(110)
    expect(shopPrice(0, 0.1)).toBe(1)
  })

  it('does not charge a gold of floating-point error', () => {
    // 100 x 1.1 is 110.00000000000001 in binary floating point, and a bare ceiling makes that
    // 111 — a preview a gold over what the server, which holds the markup as a decimal, charges.
    expect(shopPrice(100, 0.1)).toBe(110)
    expect(shopPrice(20, 0.15)).toBe(23)
    expect(shopPrice(70, 0.7)).toBe(119)
  })
})
