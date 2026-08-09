import { describe, expect, it } from 'vitest'
import {
  DEFAULT_EMOTE_MAX_SECONDS,
  DEFAULT_EMOTE_MIN_SECONDS,
  readBehavior,
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
      roams: false,
    })
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
      { disposition: 'npc', emotes: [], shopkeeper: true, sells: ['bread', 'torch'], roams: false },
    )

    expect(next.sells).toEqual(['bread', 'torch'])
    expect(next.shopkeeper).toBe(true)
  })

  it('writes roams only when it is on', () => {
    // Absent is the engine's default, so storing `roams: false` would record the default as
    // though a builder had chosen it — and a later change of default would silently not apply.
    const off = writeBehavior({}, readBehavior({}))
    expect(off.roams).toBeUndefined()

    const on = writeBehavior({}, { ...readBehavior({}), roams: true })
    expect(on.roams).toBe(true)
  })

  it('clears roams when a mob is told to stay home again', () => {
    const next = writeBehavior({ roams: true }, { ...readBehavior({ roams: true }), roams: false })

    expect(next.roams).toBeUndefined()
  })
})
