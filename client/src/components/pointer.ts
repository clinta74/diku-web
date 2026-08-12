import { useEffect, useState } from 'react'

/**
 * Whether the primary input is a finger rather than a mouse.
 *
 * Deliberately about the *pointer*, not the screen width. A narrow desktop window is still driven
 * by a keyboard and a mouse and should keep the keyboard features; a tablet at 1024px is not. The
 * two questions get confused constantly because they usually agree, and the places they disagree
 * are exactly the ones that feel broken.
 *
 * `(hover: none)` is included because `pointer: coarse` alone is true of some touch-capable
 * laptops, which do have a mouse and should keep behaving like desktops. Requiring both means
 * "a finger, and no mouse to fall back on".
 */
const QUERY = '(pointer: coarse) and (hover: none)'

/**
 * Read once, outside React, for the cases that need an answer before first paint.
 *
 * Returns false when `matchMedia` is missing rather than throwing — that is jsdom under Vitest,
 * where "is this a phone" is not a meaningful question and the desktop answer is the right one.
 */
export function isCoarsePointer(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia(QUERY).matches
}

/**
 * The same answer as state, updated if it changes.
 *
 * It does change: plugging a mouse into a tablet flips it, and so does a browser's device
 * emulation, which is how this gets tested. Subscribing is a few lines and saves a class of bug
 * where the UI is stuck in whichever mode the page happened to load in.
 */
export function useCoarsePointer(): boolean {
  const [coarse, setCoarse] = useState(isCoarsePointer)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return

    const query = window.matchMedia(QUERY)
    const sync = (event: MediaQueryListEvent) => setCoarse(event.matches)

    // Re-read on mount as well as subscribing: the initial state was computed during the first
    // render, and under StrictMode's double render that is not necessarily the same moment.
    setCoarse(query.matches)
    query.addEventListener('change', sync)
    return () => query.removeEventListener('change', sync)
  }, [])

  return coarse
}
