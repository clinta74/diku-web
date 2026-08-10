import { expect, it } from 'vitest'
import { isAtBottom } from './scrollFollow'

it('counts the exact bottom', () => {
  expect(isAtBottom({ scrollTop: 800, scrollHeight: 1000, clientHeight: 200 })).toBe(true)
})

it('allows a little slack, for fractional pixels and for a line that just arrived', () => {
  // Without it a player who never touched the scrollbar would silently stop being followed, and
  // the jump-to-bottom button would appear over a transcript they were already at the end of.
  expect(isAtBottom({ scrollTop: 780, scrollHeight: 1000, clientHeight: 200 })).toBe(true)
})

it('does not count most of the way down', () => {
  expect(isAtBottom({ scrollTop: 600, scrollHeight: 1000, clientHeight: 200 })).toBe(false)
})

it('counts a transcript shorter than its box, which never scrolls at all', () => {
  expect(isAtBottom({ scrollTop: 0, scrollHeight: 100, clientHeight: 200 })).toBe(true)
})
