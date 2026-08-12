// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { GameScreen } from './GameScreen'
import type { GameEvent } from '../net/protocol'

vi.mock('../net/api', () => ({
  api: { command: () => Promise.resolve() },
}))

const stream = vi.hoisted(() => ({ emit: null as ((event: GameEvent) => void) | null }))

vi.mock('../net/stream', () => ({
  connectStream: (_id: string, handlers: { onEvent: (event: GameEvent) => void }) => {
    stream.emit = handlers.onEvent
    return () => {
      stream.emit = null
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
