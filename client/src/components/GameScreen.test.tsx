// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { GameScreen } from './GameScreen'

vi.mock('../net/api', () => ({ api: { command: () => Promise.resolve() } }))
vi.mock('../net/stream', () => ({ connectStream: () => () => {} }))

// jsdom has no layout, so it implements no scrolling. The scrollback pins itself to the bottom on
// every new line, which would throw here before a single assertion ran.
Element.prototype.scrollIntoView = () => {}

afterEach(cleanup)

function play(active = true) {
  render(
    <GameScreen
      characterId="c1"
      characterName="Kael"
      onLeave={() => {}}
      active={active}
    />,
  )

  const input = screen.getByLabelText('Command input')

  // autoFocus is the starting state; every case here is about losing it and getting it back.
  input.blur()
  expect(document.activeElement).not.toBe(input)

  return input
}

function type(key: string, target: EventTarget = document.body) {
  target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
}

/**
 * The behaviour itself. jsdom performs no default text insertion, so what is asserted is the part
 * this code is responsible for - focus lands on the input before the character is dispatched. The
 * insertion that follows is the browser's, which is exactly why the handler does not preventDefault
 * and re-type the character itself.
 */
it('pulls a keystroke aimed at nothing into the command input', () => {
  const input = play()

  type('n')

  expect(document.activeElement).toBe(input)
})

it('leaves the keyboard alone while the builder is up', () => {
  // The game is hidden rather than unmounted, so this listener outlives its own visibility. Every
  // keystroke typed into a builder form would otherwise land here instead.
  const input = play(false)

  type('n')

  expect(document.activeElement).not.toBe(input)
})

it('stops listening once the session is gone', () => {
  const input = play()
  cleanup()

  type('n')

  expect(document.activeElement).not.toBe(input)
})

it('puts the caret back when the tab is returned to', () => {
  const input = play()

  window.dispatchEvent(new Event('focus'))

  expect(document.activeElement).toBe(input)
})

it('does not steal a keystroke already typed into it', () => {
  // Guarding on activeElement keeps the handler from refocusing the input on every character,
  // which would collapse any selection the player made inside their own half-typed command.
  const input = play() as HTMLInputElement
  input.focus()
  input.value = 'say hello'
  input.setSelectionRange(4, 9)

  type('x', input)

  expect(input.selectionStart).toBe(4)
  expect(input.selectionEnd).toBe(9)
})
