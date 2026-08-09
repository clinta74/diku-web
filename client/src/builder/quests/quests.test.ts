import { describe, expect, it } from 'vitest'
import { chainOrder, DIALOGUE_FIELDS, formatKeyList, newQuest, parseKeyList } from './quests'
import { DIALOGUE_KEYS } from '../../net/builderApi'
import type { StorylineNode } from '../../net/builderApi'

const node = (key: string, external = false): StorylineNode => ({
  key,
  name: key,
  zoneKey: external ? 'other.zone' : 'test.zone',
  external,
})

describe('dialogue fields', () => {
  /**
   * The engine reads these four keys by name and silently falls back when one is missing, so a
   * plausible-but-wrong key here would produce a field a builder fills in that never appears in
   * play. Transcribed from `QuestCommands`; this is the guard that they still agree.
   */
  it('covers exactly the keys the engine reads', () => {
    expect(DIALOGUE_FIELDS.map((f) => f.key).sort()).toEqual([...DIALOGUE_KEYS].sort())
  })

  it('states what the engine says when a line is left blank', () => {
    // An empty field is a real choice, not a hole - but only if the builder can see what it means.
    for (const field of DIALOGUE_FIELDS) {
      expect(field.fallback.length).toBeGreaterThan(0)
    }
  })
})

describe('prerequisite lists', () => {
  it('splits on commas and newlines and trims', () => {
    expect(parseKeyList(' a , b\nc ')).toEqual(['a', 'b', 'c'])
  })

  it('drops blanks rather than storing empty keys', () => {
    // A trailing comma is the normal way to type a list, and an empty prerequisite would be a
    // quest that can never be offered.
    expect(parseKeyList('a,,b,')).toEqual(['a', 'b'])
    expect(parseKeyList('   ')).toEqual([])
  })

  it('de-duplicates', () => {
    expect(parseKeyList('a, a, b')).toEqual(['a', 'b'])
  })

  it('round-trips through the text field', () => {
    const keys = ['zone.first', 'zone.second']
    expect(parseKeyList(formatKeyList(keys))).toEqual(keys)
  })
})

describe('newQuest', () => {
  /**
   * A create that sent `{}` would leave the giver and turn-in empty, which is exactly the
   * dormant state §5.2d describes - the quest exists, is offered by nobody, and reads correctly
   * in the journal while being impossible to start.
   */
  it('names every field the server would otherwise default', () => {
    const draft = newQuest('zone.errand', 'An Errand', 'test.zone')

    expect(draft.zoneKey).toBe('test.zone')
    expect(draft.name).toBe('An Errand')
    expect(draft.requiredCount).toBe(1)
    expect(draft.rewardItemCount).toBe(1)
    expect(draft.prerequisiteQuestKeys).toEqual([])
    expect(draft.isRepeatable).toBe(false)
  })

  it('falls back to the key when no name is given', () => {
    expect(newQuest('zone.errand', '', 'test.zone').name).toBe('zone.errand')
  })
})

describe('chainOrder', () => {
  it('puts a prerequisite before what it unlocks', () => {
    const ordered = chainOrder(
      [node('second'), node('first')],
      [{ from: 'first', to: 'second' }],
    )

    expect(ordered.map((o) => o.node.key)).toEqual(['first', 'second'])
    expect(ordered.map((o) => o.depth)).toEqual([0, 1])
  })

  it('measures depth from the longest path, not the first one found', () => {
    // a -> b -> c and a -> c. c is depth 2, because it cannot be reached until b is done.
    const ordered = chainOrder(
      [node('a'), node('b'), node('c')],
      [
        { from: 'a', to: 'b' },
        { from: 'b', to: 'c' },
        { from: 'a', to: 'c' },
      ],
    )

    expect(ordered.find((o) => o.node.key === 'c')?.depth).toBe(2)
  })

  it('still lists the members of a cycle', () => {
    // A cycle has no valid order, and every quest in it is unstartable. Dropping them would hide
    // exactly what the reader needs to see.
    const ordered = chainOrder(
      [node('a'), node('b')],
      [
        { from: 'a', to: 'b' },
        { from: 'b', to: 'a' },
      ],
    )

    expect(ordered.map((o) => o.node.key).sort()).toEqual(['a', 'b'])
  })

  it('does not loop forever on a self-referencing quest', () => {
    const ordered = chainOrder([node('a')], [{ from: 'a', to: 'a' }])
    expect(ordered).toHaveLength(1)
  })

  it('keeps external prerequisites in the ordering', () => {
    // A cross-zone chain must read as a chain rather than as a quest with no way in.
    const ordered = chainOrder(
      [node('local'), node('elsewhere', true)],
      [{ from: 'elsewhere', to: 'local' }],
    )

    expect(ordered.map((o) => o.node.key)).toEqual(['elsewhere', 'local'])
  })

  it('handles an empty zone', () => {
    expect(chainOrder([], [])).toEqual([])
  })
})
