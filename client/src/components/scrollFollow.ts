/**
 * Whether the scrollback is close enough to the bottom to count as following it.
 *
 * Following is the normal state, and it is the state a player leaves by scrolling up to read
 * something. The scrollback used to jump to the newest line unconditionally, which meant reading
 * back through a fight that was still going yanked you away four times a second.
 *
 * The slack is what makes the rule usable. Exact equality fails on fractional device pixels, and
 * on the moment a line is appended between the scroll event and this reading, so a player who
 * never touched the scrollbar would silently stop being followed and be shown a jump-to-bottom
 * control over a transcript they were already at the end of.
 */
export function isAtBottom(
  box: { scrollTop: number; scrollHeight: number; clientHeight: number },
  slack = 24,
): boolean {
  return box.scrollHeight - box.scrollTop - box.clientHeight <= slack
}
