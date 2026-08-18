// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { useState } from 'react'
import { OptionalSecondsInput, toPulses, toSeconds } from './SecondsInput'

afterEach(cleanup)

/**
 * A duration that may be blank, typed one character at a time.
 *
 * The field this replaced was fully controlled off the stored pulse count, so every keystroke was
 * round-tripped through `toPulses` and back. Typing "1." parsed as 1, stored 4 pulses, and rendered
 * "1" — **the decimal point was erased as you typed it**, so a weapon speed of 1.5 seconds could not
 * be entered at all. Any value not already a whole number of seconds was unreachable through the UI.
 */
function Harness({ initial = null as number | null }) {
  const [pulses, setPulses] = useState<number | null>(initial)
  return (
    <>
      <OptionalSecondsInput pulses={pulses} onChange={setPulses} minPulses={4} aria-label="delay" />
      <output>{pulses === null ? 'null' : String(pulses)}</output>
    </>
  )
}

const field = (r: { container: HTMLElement }) => r.container.querySelector('input') as HTMLInputElement
const stored = (r: { container: HTMLElement }) => (r.container.querySelector('output') as HTMLElement).textContent

function type(el: { container: HTMLElement }, text: string) {
  // One character at a time, because the bug only appears between keystrokes.
  let so_far = ''
  for (const ch of text) {
    so_far += ch
    fireEvent.change(field(el), { target: { value: so_far } })
    so_far = field(el).value
  }
}

describe('OptionalSecondsInput', () => {
  it('lets a decimal be typed one character at a time', () => {
    const el = render(<Harness />)

    type(el, '1.5')

    expect(field(el).value).toBe('1.5')
    expect(stored(el)).toBe('6')
  })

  it('keeps the decimal point while it is still being typed', () => {
    // The exact keystroke that used to be swallowed.
    const el = render(<Harness />)

    type(el, '1.')

    expect(field(el).value).toBe('1.')
  })

  it('reaches every quarter-second the engine can represent', () => {
    for (const [text, pulses] of [
      ['1', 4],
      ['1.25', 5],
      ['1.5', 6],
      ['1.75', 7],
      ['2', 8],
      ['2.5', 10],
    ] as const) {
      const el = render(<Harness />)
      type(el, text)
      expect(stored(el)).toBe(String(pulses))
      cleanup()
    }
  })

  it('treats blank as no speed at all rather than as zero', () => {
    // Blank is a real value here: a weapon with no delay swings at the default in a main hand and
    // never strikes from an off hand. NumberInput cannot express this - it settles blank to a
    // number on blur - which is why this is its own component.
    const el = render(<Harness initial={6} />)

    fireEvent.change(field(el), { target: { value: '' } })

    expect(stored(el)).toBe('null')
  })

  it('settles a below-minimum entry up to the floor on blur, not while typing', () => {
    // Clamping per keystroke is what stops "0.5" ever being typed on the way to "0.75".
    const el = render(<Harness />)

    type(el, '0.5')
    expect(field(el).value).toBe('0.5')

    fireEvent.blur(field(el))
    expect(stored(el)).toBe('4')
    expect(field(el).value).toBe('1')
  })

  it('shows a loaded value in seconds', () => {
    const el = render(<Harness initial={10} />)

    expect(field(el).value).toBe('2.5')
  })
})

describe('conversion', () => {
  it('round-trips the quarter-second grid', () => {
    for (const pulses of [4, 5, 6, 7, 8, 10, 12]) {
      expect(toPulses(toSeconds(pulses))).toBe(pulses)
    }
  })
})
