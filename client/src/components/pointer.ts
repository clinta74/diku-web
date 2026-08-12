import { matchesMedia, useMediaQuery } from './media'

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
const COARSE = '(pointer: coarse) and (hover: none)'

/**
 * The width at which the game switches to the phone layout — the transcript taking the screen and
 * the room panel moving into a sheet.
 *
 * A width and not the pointer query above, because this one really is about how much room there
 * is: a narrow desktop window has the same problem a phone does, and the layout that solves it is
 * no worse there. The pointer query decides behaviour; this decides shape.
 *
 * `GameScreen` stamps the answer onto the game element as `data-layout`, so the stylesheet keys off
 * that attribute rather than repeating the number.
 */
const PHONE = '(max-width: 600px)'

export function isCoarsePointer(): boolean {
  return matchesMedia(COARSE)
}

export function useCoarsePointer(): boolean {
  return useMediaQuery(COARSE)
}

export function usePhoneLayout(): boolean {
  return useMediaQuery(PHONE)
}
