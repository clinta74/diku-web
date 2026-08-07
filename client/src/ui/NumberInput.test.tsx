// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { useState } from 'react'
import { NumberInput } from './NumberInput'

afterEach(cleanup)

function Harness({
  allowDecimal,
  allowNegative,
  min,
  onValue,
}: {
  allowDecimal?: boolean
  allowNegative?: boolean
  min?: number
  onValue?: (v: number) => void
}) {
  const [value, setValue] = useState(0)
  return (
    <NumberInput
      value={value}
      min={min}
      allowDecimal={allowDecimal}
      allowNegative={allowNegative}
      aria-label="n"
      onChange={(v) => {
        setValue(v)
        onValue?.(v)
      }}
    />
  )
}

const input = (el: HTMLElement) => el.querySelector('input') as HTMLInputElement

describe('NumberInput', () => {
  it('is a text field with no spinner', () => {
    const el = render(<Harness />)
    expect(input(el.container).getAttribute('type')).toBe('text')
  })

  it('rejects non-numeric characters as they are typed', () => {
    const el = render(<Harness />)
    const field = input(el.container)
    fireEvent.change(field, { target: { value: '1a2b3' } })
    expect(field.value).toBe('123')
  })

  it('drops a decimal point unless decimals are allowed', () => {
    const el = render(<Harness />)
    const field = input(el.container)
    fireEvent.change(field, { target: { value: '1.5' } })
    expect(field.value).toBe('15')
  })

  it('keeps one decimal point when decimals are allowed', () => {
    const el = render(<Harness allowDecimal />)
    const field = input(el.container)
    fireEvent.change(field, { target: { value: '1.5.2' } })
    expect(field.value).toBe('1.52')
  })

  it('emits the parsed number on change', () => {
    const onValue = vi.fn()
    const el = render(<Harness onValue={onValue} />)
    fireEvent.change(input(el.container), { target: { value: '42' } })
    expect(onValue).toHaveBeenLastCalledWith(42)
  })

  it('clamps to the minimum on blur', () => {
    const el = render(<Harness min={1} />)
    const field = input(el.container)
    fireEvent.change(field, { target: { value: '' } })
    fireEvent.blur(field)
    expect(field.value).toBe('1')
  })
})
