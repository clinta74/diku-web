import { describe, expect, it } from 'vitest'
import { markSelfInContents, markSelfOnMap } from './self'
import type { ContentEntry, MapEntity } from '../net/protocol'

const player = (name: string, x = 0): MapEntity => ({
  id: `p_${name}`,
  icon: 'W',
  x,
  y: 0,
  label: name,
  type: 'player',
})

const entry = (name: string, icon = 'W'): ContentEntry => ({
  icon,
  label: name,
  keyword: name.toLowerCase(),
})

describe('marking yourself on the room map', () => {
  it('draws the viewer as an at sign and labels them "you"', () => {
    const marked = markSelfOnMap([player('Kael'), player('Mira', 1)], 'Kael')

    expect(marked[0]).toMatchObject({ icon: '@', label: 'you' })
    expect(marked[1]).toMatchObject({ icon: 'W', label: 'Mira' })
  })

  it('leaves everybody else exactly as the server drew them', () => {
    // The point of the whole change: one room, drawn once, for everyone standing in it. Two
    // clients must agree about every entity except the one holding the screen.
    const shared = [player('Kael'), player('Mira', 1)]

    const forKael = markSelfOnMap(shared, 'Kael')
    const forMira = markSelfOnMap(shared, 'Mira')

    expect(forKael[1]).toEqual(forMira[1] && shared[1])
    expect(forKael[0]).toMatchObject({ icon: '@' })
    expect(forMira[1]).toMatchObject({ icon: '@' })
  })

  it('does not mutate the payload it was given', () => {
    // The same object is rendered again on every re-render and is shared across panels, so
    // marking has to be a copy. Mutating it would leave "you" burned into the cached state and
    // every subsequent viewer of that payload would see it.
    const shared = [player('Kael')]
    markSelfOnMap(shared, 'Kael')

    expect(shared[0]).toMatchObject({ icon: 'W', label: 'Kael' })
  })

  it('will not mistake a mob or an item that shares your name', () => {
    const mob: MapEntity = { id: 'm_1', icon: 'k', x: 2, y: 0, label: 'Kael', type: 'mob' }
    const marked = markSelfOnMap([mob], 'Kael')

    expect(marked[0]).toMatchObject({ icon: 'k', label: 'Kael' })
  })

  it('returns the entities untouched when the viewer is not among them', () => {
    // An unlit room draws nobody, and the room you have just left still arrives once.
    const entities = [player('Mira')]
    expect(markSelfOnMap(entities, 'Kael')).toBe(entities)
    expect(markSelfOnMap(entities, '')).toBe(entities)
  })
})

describe('marking yourself in the room contents', () => {
  it('shows the viewer as "you"', () => {
    const marked = markSelfInContents([entry('Kael'), entry('Mira')], 'Kael')

    expect(marked[0]).toMatchObject({ icon: '@', label: 'you' })
    expect(marked[1]).toMatchObject({ label: 'Mira' })
  })

  it('leaves the keyword alone, because that is what a tapped verb sends', () => {
    // "look you" is not a command. Relabelling must not change the name the game is asked about.
    const marked = markSelfInContents([entry('Kael')], 'Kael')

    expect(marked[0].keyword).toBe('kael')
  })

  it('matches the keyword however the name is cased', () => {
    const marked = markSelfInContents([entry('Kael')], 'KAEL')
    expect(marked[0].label).toBe('you')
  })

  it('does not mutate, and passes through an unfamiliar room', () => {
    const entries = [entry('Mira')]
    expect(markSelfInContents(entries, 'Kael')).toBe(entries)
    expect(entries[0]).toMatchObject({ label: 'Mira' })
  })
})
