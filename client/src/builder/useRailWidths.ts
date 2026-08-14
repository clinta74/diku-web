import { useCallback, useEffect, useState } from 'react'

/** Where the widths are kept, so they survive a reload rather than only a navigation. */
const STORAGE_KEY = 'dikuweb.builder.rails'

/** The widths the builder shipped with, and what a reset returns to. */
export const DEFAULT_RAILS = { left: 240, right: 272 } as const

/**
 * A rail narrower than this cannot show a room key, and one wider than this leaves no room to
 * edit in. Clamped rather than left free, because a drag past the edge of the window is easy and
 * a rail dragged to zero looks like the tree disappearing.
 */
const MIN = 160
const MAX = 560

export interface RailWidths {
  left: number
  right: number
}

function clamp(value: number): number {
  return Math.min(MAX, Math.max(MIN, Math.round(value)))
}

function read(): RailWidths {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return { ...DEFAULT_RAILS }

    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null) return { ...DEFAULT_RAILS }

    const { left, right } = parsed as Partial<RailWidths>

    return {
      left: typeof left === 'number' && Number.isFinite(left) ? clamp(left) : DEFAULT_RAILS.left,
      right: typeof right === 'number' && Number.isFinite(right) ? clamp(right) : DEFAULT_RAILS.right,
    }
  } catch {
    // Whatever is under the key is treated as hostile, the same way the command history is: a
    // throw while reading it would take the whole builder down rather than one preference.
    return { ...DEFAULT_RAILS }
  }
}

/**
 * The builder's two side rails, draggable and remembered.
 *
 * <b>Fixed widths were the one axis the UX review named that the client did not address at all</b>
 * (UX.md finding 2). Room keys are `world.zone.room`, so
 * `aldenmoor.millbrook.tavern-common` is 33 characters in a 15rem tree — and the longest prose in
 * the product, a room description, is written in the middle column that the rails squeeze.
 *
 * Persisted because a width you have to set again on every reload is not really adjustable.
 */
export function useRailWidths() {
  const [widths, setWidths] = useState<RailWidths>(read)

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(widths))
    } catch {
      // A full or blocked store costs the preference, not the session.
    }
  }, [widths])

  const setLeft = useCallback((px: number) => {
    setWidths((current) => ({ ...current, left: clamp(px) }))
  }, [])

  const setRight = useCallback((px: number) => {
    setWidths((current) => ({ ...current, right: clamp(px) }))
  }, [])

  const reset = useCallback(() => setWidths({ ...DEFAULT_RAILS }), [])

  return { widths, setLeft, setRight, reset }
}
