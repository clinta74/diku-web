import { useEffect, useState } from "react";
import { PULSE_MS, type AbilityEntry } from "../net/protocol";

interface Props {
  abilities: AbilityEntry[];
  /** When each cooling ability becomes usable again, as a wall-clock timestamp. */
  cooldownUntil: Record<string, number>;
}

/**
 * How often the countdown redraws. A quarter second is one pulse, which is as fine as the server
 * can distinguish anyway - a faster timer would render a bar the game does not have.
 */
const TICK_MS = PULSE_MS;

/** Cells in a cooldown bar. Ten, because that is what a vitals meter uses. */
const CELLS = 10;

/**
 * What is still cooling down, and nothing else.
 *
 * <b>Only cooling abilities appear.</b> The bar used to list every ability the character had, which
 * at level 45 is a row wider than the screen with a horizontal scrollbar under it - so the one
 * thing worth seeing at a glance, what is not ready yet, was the thing you had to scroll to find.
 * A short row that empties itself says more than a long one that never changes. What the character
 * <em>can</em> do is answered by the `abilities` command, and by the level-up message that names
 * each new one as it is earned.
 *
 * The cost of that is the chips no longer fill the input, since there is nothing to click while an
 * ability is ready. Deliberate for now: the verb is the interface.
 *
 * <b>The countdown runs here, not on the wire.</b> The server sends one event when an ability
 * fires and nothing after; this derives the bar on screen from a stored instant and the clock. The
 * alternative - a frame per pulse per cooling ability - is four a second per player, which is
 * exactly the traffic PLAN.md §11's pulse budget is careful about.
 *
 * <b>The timer only exists while something is cooling.</b> An interval that runs forever on an idle
 * screen is a wakeup every quarter second for a row that is not there, and on a phone that is
 * battery.
 */
export function AbilityBar({ abilities, cooldownUntil }: Props) {
  const [now, setNow] = useState(() => Date.now());

  const cooling = abilities.filter(
    (ability) => (cooldownUntil[ability.key] ?? 0) > now,
  );
  const idle = cooling.length === 0;

  useEffect(() => {
    if (idle) return;
    const timer = setInterval(() => setNow(Date.now()), TICK_MS);
    return () => clearInterval(timer);
  }, [idle]);

  if (idle) {
    return null;
  }

  return (
    <ul className="ability-bar" aria-label="Cooling down">
      {cooling.map((ability) => {
        const remainingMs = Math.max(0, (cooldownUntil[ability.key] ?? 0) - now);

        // The denominator is the roster's figure rather than the fired event's, because a
        // reconnect resyncs from the roster and only ever learns what is *left*. The two agree by
        // construction today - AbilitySystem sends the ability's own CooldownPulses - and the
        // clamp below is what keeps a builder retuning one mid-cooldown from drawing past full.
        const totalMs = ability.cooldownPulses * PULSE_MS;
        const fraction = totalMs > 0 ? Math.min(1, remainingMs / totalMs) : 1;

        // Rounded up, so the bar keeps a cell right until the ability is actually usable. Rounding
        // to nearest empties it early, on something the server would still refuse.
        const filled = Math.max(1, Math.ceil(fraction * CELLS));
        const seconds = Math.ceil(remainingMs / 1000);

        return (
          <li
            key={ability.key}
            className="ability-chip"
            // The bar is decoration to a screen reader; the seconds are the content. Announced
            // from the label rather than from the bar, because "block block block light shade"
            // is what reading the fill out loud actually sounds like.
            aria-label={`${ability.name}, ${seconds} seconds`}
            title={`${ability.name} — ${seconds}s`}
          >
            <span className="ability-chip-name">{ability.name}</span>
            <span className="ability-chip-bar" aria-hidden="true">
              {"█".repeat(filled)}
              <span className="dim">{"░".repeat(CELLS - filled)}</span>
            </span>
          </li>
        );
      })}
    </ul>
  );
}
