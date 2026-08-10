// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { GameScreen } from './GameScreen'
import type { ContentEntry, GameEvent } from '../net/protocol'

const sent: string[] = []

vi.mock('../net/api', () => ({
  api: {
    command: (_id: string, input: string) => {
      sent.push(input)
      return Promise.resolve()
    },
  },
}))

// Holds the stream's own callback so a test can push frames in, which is the only way the room's
// contents - and therefore the completion candidates - ever reach the component.
const stream = vi.hoisted(() => ({ emit: null as ((event: GameEvent) => void) | null }))

vi.mock('../net/stream', () => ({
  connectStream: (_id: string, handlers: { onEvent: (event: GameEvent) => void }) => {
    stream.emit = handlers.onEvent
    return () => {
      stream.emit = null
    }
  },
}))

// jsdom has no layout, so it implements no scrolling: scrollHeight and clientHeight are always 0
// and scrollTop never moves. Every case below that cares about position says so explicitly.
Element.prototype.scrollIntoView = () => {}

beforeEach(() => {
  sent.length = 0
  localStorage.clear()
})

afterEach(cleanup)

function play({ active = true, characterId = 'c1' } = {}) {
  render(
    <GameScreen
      characterId={characterId}
      characterName="Kael"
      onLeave={() => {}}
      active={active}
    />,
  )

  return screen.getByLabelText('Command input') as HTMLInputElement
}

function emit(event: GameEvent) {
  act(() => stream.emit?.(event))
}

function inTheRoom(occupants: ContentEntry[], items: ContentEntry[] = []) {
  emit({ type: 'contents', data: { occupants, items } })
}

function entry(label: string): ContentEntry {
  return { icon: '@', label, keyword: label.toLowerCase().replaceAll(' ', '-') }
}

describe('typing anywhere', () => {
  function loseFocus(input: HTMLElement) {
    input.blur()
    expect(document.activeElement).not.toBe(input)
  }

  function type(key: string, target: EventTarget = document.body) {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }))
  }

  /**
   * jsdom performs no default text insertion, so what is asserted is the part this code is
   * responsible for - focus lands on the input before the character is dispatched. The insertion
   * that follows is the browser's, which is why the handler does not preventDefault and re-type
   * the character itself.
   */
  it('pulls a keystroke aimed at nothing into the command input', () => {
    const input = play()
    loseFocus(input)

    type('n')

    expect(document.activeElement).toBe(input)
  })

  it('leaves the keyboard alone while the builder is up', () => {
    // The game is hidden rather than unmounted, so this listener outlives its own visibility.
    // Every keystroke typed into a builder form would otherwise land here instead.
    const input = play({ active: false })
    loseFocus(input)

    type('n')

    expect(document.activeElement).not.toBe(input)
  })

  it('stops listening once the session is gone', () => {
    const input = play()
    loseFocus(input)
    cleanup()

    type('n')

    expect(document.activeElement).not.toBe(input)
  })

  it('puts the caret back when the tab is returned to', () => {
    const input = play()
    loseFocus(input)

    window.dispatchEvent(new Event('focus'))

    expect(document.activeElement).toBe(input)
  })

  it('does not steal a keystroke already typed into it', () => {
    // Guarding on activeElement keeps the handler from refocusing on every character, which would
    // collapse any selection the player made inside their own half-typed command.
    const input = play()
    input.focus()
    fireEvent.change(input, { target: { value: 'say hello' } })
    input.setSelectionRange(4, 9)

    type('x', input)

    expect(input.selectionStart).toBe(4)
    expect(input.selectionEnd).toBe(9)
  })
})

describe('command history', () => {
  it('is there after a reload', () => {
    const input = play()
    fireEvent.change(input, { target: { value: 'kill rat' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    // A reload is a fresh mount reading the same storage.
    cleanup()
    const reloaded = play()
    fireEvent.keyDown(reloaded, { key: 'ArrowUp' })

    expect(reloaded.value).toBe('kill rat')
  })

  it('does not carry across to another character', () => {
    const input = play({ characterId: 'c1' })
    fireEvent.change(input, { target: { value: 'kill rat' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    cleanup()
    const other = play({ characterId: 'c2' })
    fireEvent.keyDown(other, { key: 'ArrowUp' })

    expect(other.value).toBe('')
  })
})

describe('tab completion', () => {
  it('grows a name from what is in the room', () => {
    const input = play()
    inTheRoom([entry('a bar maiden')])

    fireEvent.change(input, { target: { value: 'talk maid' } })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('talk a bar maiden')
  })

  it('cycles on a second press, and back on shift', () => {
    const input = play()
    inTheRoom([entry('a rat')], [entry('a rat trap')])

    fireEvent.change(input, { target: { value: 'get a rat t' } })
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(input.value).toBe('get a rat trap')

    // Only one candidate has "a rat t" as a prefix, so the cycle is one long and stays put.
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(input.value).toBe('get a rat trap')

    fireEvent.keyDown(input, { key: 'Tab', shiftKey: true })
    expect(input.value).toBe('get a rat trap')
  })

  it('restarts the cycle once the player types again', () => {
    // Without this the second Tab would advance a stale cycle and overwrite the new fragment.
    const input = play()
    inTheRoom([entry('a bar maiden'), entry('an old man')])

    fireEvent.change(input, { target: { value: 'talk maid' } })
    fireEvent.keyDown(input, { key: 'Tab' })
    expect(input.value).toBe('talk a bar maiden')

    fireEvent.change(input, { target: { value: 'talk old' } })
    fireEvent.keyDown(input, { key: 'a' })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('talk an old man')
  })

  it('never offers the player themself', () => {
    // The viewer arrives labelled "you", which is not a name anything answers to.
    const input = play()
    inTheRoom([{ icon: '@', label: 'you', keyword: 'kael' }])

    fireEvent.change(input, { target: { value: 'give beer y' } })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('give beer y')
  })

  it('targets a link-dead player by name rather than by their status', () => {
    const input = play()
    inTheRoom([{ icon: '@', label: 'Mira (link-dead)', keyword: 'mira' }])

    fireEvent.change(input, { target: { value: 'give beer Mi' } })
    fireEvent.keyDown(input, { key: 'Tab' })

    expect(input.value).toBe('give beer Mira')
  })

  it('lets Tab move focus when there is nothing to complete', () => {
    // Swallowing Tab unconditionally would leave the keyboard no way off this control, and
    // combined with the type-anywhere handler that makes the page unnavigable without a mouse.
    const input = play()
    inTheRoom([entry('a bar maiden')])

    fireEvent.change(input, { target: { value: 'kill zzz' } })
    const notPrevented = fireEvent.keyDown(input, { key: 'Tab' })

    expect(notPrevented).toBe(true)
    expect(input.value).toBe('kill zzz')
  })
})

describe('following the newest line', () => {
  /** jsdom reports every box as zero-sized, so the scroll position has to be stated outright. */
  function scrollTo(box: Element, { top, height, visible = 200 }: Record<string, number>) {
    Object.defineProperty(box, 'scrollHeight', { value: height, configurable: true })
    Object.defineProperty(box, 'clientHeight', { value: visible, configurable: true })
    Object.defineProperty(box, 'scrollTop', { value: top, writable: true, configurable: true })
    fireEvent.scroll(box)
  }

  function scrollback() {
    const box = document.querySelector('.scrollback')
    expect(box).not.toBeNull()
    return box as HTMLElement
  }

  it('offers no way back while already at the bottom', () => {
    play()

    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })

  it('offers one once the player scrolls up to read', () => {
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })

    expect(screen.getByRole('button', { name: /jump to newest/i })).toBeTruthy()
  })

  it('stops dragging the player back down while they are reading', () => {
    // The whole complaint: a fight still going pulled you to the bottom four times a second.
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })

    emit({ type: 'text', data: { spans: [{ t: 'A rat bites you.' }] } })

    expect(scrollback().scrollTop).toBe(0)
    expect(screen.getByText('A rat bites you.')).toBeTruthy()
  })

  it('goes back to following when the button is used', () => {
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })

    fireEvent.click(screen.getByRole('button', { name: /jump to newest/i }))

    expect(scrollback().scrollTop).toBe(2000)
    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })

  it('resumes following on its own when the player scrolls back down', () => {
    play()
    scrollTo(scrollback(), { top: 0, height: 2000 })
    scrollTo(scrollback(), { top: 1800, height: 2000 })

    expect(screen.queryByRole('button', { name: /jump to newest/i })).toBeNull()
  })
})
