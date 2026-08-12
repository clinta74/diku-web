// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { GameScreen } from './GameScreen'
import type { GameEvent } from '../net/protocol'

const sent: string[] = []
const entered: string[] = []

vi.mock('../net/api', () => ({
  api: {
    command: (_id: string, input: string) => {
      sent.push(input)
      return Promise.resolve()
    },
    enter: (id: string) => {
      entered.push(id)
      return Promise.resolve({})
    },
  },
}))

const stream = vi.hoisted(() => ({
  emit: null as ((event: GameEvent) => void) | null,
  fail: null as (() => void) | null,
  open: null as (() => void) | null,
}))

vi.mock('../net/stream', () => ({
  connectStream: (
    _id: string,
    handlers: {
      onEvent: (event: GameEvent) => void
      onError?: () => void
      onOpen?: () => void
    },
  ) => {
    stream.emit = handlers.onEvent
    stream.fail = () => handlers.onError?.()
    stream.open = () => handlers.onOpen?.()
    return () => {
      stream.emit = null
      stream.fail = null
      stream.open = null
    }
  },
}))

/**
 * jsdom implements no media queries at all, so the layout has to be told which one it is in. Every
 * query is answered from one map — a test that says "phone" gets a phone for both the width
 * question and the pointer question, which is what a phone actually is.
 */
function pretendToBe(kind: 'phone' | 'desktop') {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: kind === 'phone',
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

beforeEach(() => {
  sent.length = 0
  entered.length = 0
  localStorage.clear()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function play() {
  return render(
    <GameScreen characterId="c1" characterName="Kael" onLeave={() => {}} active />,
  )
}

function sheet(container: HTMLElement) {
  return container.querySelector('.room-sheet') as HTMLElement
}

/** The exits only reach the component over the stream, so a test that wants a pad has to arrive. */
function arriveIn(exits: string[]) {
  act(() =>
    stream.emit?.({
      type: 'room',
      data: { key: 'aldenmoor.millbrook.north-gate', title: 'The North Gate', description: '', exits },
    }),
  )
}

it('starts with the room sheet closed, so the transcript has the screen', () => {
  // The whole point of the layout: the map and the room description are reference material you
  // consult, not the thing you watch. Opening on top of the transcript would undo it.
  pretendToBe('phone')
  const { container } = play()

  expect(container.querySelector('.game')?.getAttribute('data-layout')).toBe('phone')
  expect(sheet(container).dataset.open).toBe('false')
  expect(sheet(container).getAttribute('aria-hidden')).toBe('true')
})

it('opens and closes the sheet from the header', () => {
  pretendToBe('phone')
  const { container } = play()

  fireEvent.click(screen.getByRole('button', { name: /room/i }))
  expect(sheet(container).dataset.open).toBe('true')
  expect(sheet(container).getAttribute('aria-hidden')).toBe('false')

  fireEvent.click(screen.getByRole('button', { name: /close/i }))
  expect(sheet(container).dataset.open).toBe('false')
})

it('closes the sheet on Escape', () => {
  pretendToBe('phone')
  const { container } = play()

  fireEvent.click(screen.getByRole('button', { name: /room/i }))
  fireEvent.keyDown(document, { key: 'Escape' })

  expect(sheet(container).dataset.open).toBe('false')
})

it('leaves the panels visible and unhidden on a desktop', () => {
  // The sheet wrapper exists in both layouts — one tree, two shapes — so the guard that matters is
  // that its ARIA state does not follow the phone's. Hiding panels that are plainly on screen from
  // assistive tech would be a lie the sighted user cannot see.
  pretendToBe('desktop')
  const { container } = play()

  expect(container.querySelector('.game')?.getAttribute('data-layout')).toBe('desktop')
  expect(sheet(container).getAttribute('aria-hidden')).toBe('false')
})

it('walks with a tap, and keeps the missing directions on the pad', () => {
  // The reason M2 exists: the main verb of a MUD is walking, and walking used to mean typing
  // `north` on a keyboard covering half the screen.
  pretendToBe('phone')
  play()

  arriveIn(['north', 'east'])

  fireEvent.click(screen.getByRole('button', { name: 'north' }))
  expect(sent).toEqual(['north'])

  // Present but disabled, rather than absent: a pad that only drew real exits would reflow on
  // every arrival, moving the key out from under the thumb.
  const west = screen.getByRole('button', { name: 'west' }) as HTMLButtonElement
  expect(west.disabled).toBe(true)

  fireEvent.click(west)
  expect(sent).toEqual(['north'])
})

it('has no exit pad on a desktop', () => {
  pretendToBe('desktop')
  play()
  arriveIn(['north'])

  expect(screen.queryByRole('group', { name: 'Exits' })).toBeNull()
})

it('re-runs a command from a chip without opening the keyboard', () => {
  // The phone's up arrow. Tapping runs it rather than loading it into the box, because sending it
  // from there would mean summoning a keyboard to press a return key.
  pretendToBe('phone')
  const input = play().container.querySelector('input') as HTMLInputElement

  fireEvent.change(input, { target: { value: 'attack wolf' } })
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(sent).toEqual(['attack wolf'])

  fireEvent.click(screen.getByRole('button', { name: 'attack wolf' }))
  expect(sent).toEqual(['attack wolf', 'attack wolf'])
})

it('offers a way back into the world once the stream is down', () => {
  // The stream retries on its own and usually wins. This is for the case it cannot fix: after the
  // link-dead window the character has been removed from the world, and reconnecting a stream has
  // nothing to attach to — only entering again does. Before this, the only route was leaving to
  // the character screen and picking the same character.
  vi.useFakeTimers()
  try {
    pretendToBe('phone')
    play()

    // Nothing yet: the stream has not opened on the first render, and every ordinary page load
    // would otherwise flash "Disconnected" before the connection had a chance to happen.
    act(() => stream.fail?.())
    expect(screen.queryByRole('button', { name: /rejoin/i })).toBeNull()

    act(() => vi.advanceTimersByTime(2500))

    fireEvent.click(screen.getByRole('button', { name: /rejoin/i }))
    expect(entered).toEqual(['c1'])
  } finally {
    vi.useRealTimers()
  }
})

it('takes the reconnect bar away as soon as the stream is back', () => {
  vi.useFakeTimers()
  try {
    pretendToBe('phone')
    play()

    act(() => stream.fail?.())
    act(() => vi.advanceTimersByTime(2500))
    expect(screen.queryByRole('button', { name: /rejoin/i })).not.toBeNull()

    act(() => stream.open?.())
    expect(screen.queryByRole('button', { name: /rejoin/i })).toBeNull()
  } finally {
    vi.useRealTimers()
  }
})

it('does not steal keystrokes aimed elsewhere on a touch device', () => {
  // Type-anywhere is a desktop convenience. On a phone, focusing the input summons the keyboard
  // over the game, so a stray keydown must not do it.
  pretendToBe('phone')
  play()

  const input = screen.getByLabelText('Command input')
  ;(document.body as HTMLElement).focus()
  fireEvent.keyDown(document, { key: 'k' })

  expect(document.activeElement).not.toBe(input)
})
