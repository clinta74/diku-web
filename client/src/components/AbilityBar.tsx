import { useEffect, useState } from "react";
import { PULSE_MS, type AbilityEntry } from "../net/protocol";

interface Props {
  abilities: AbilityEntry[];
  /** When each cooling ability becomes usable again, as a wall-clock timestamp. */
  cooldownUntil: Record<string, number>;
  /** Puts the ability's verb in the input rather than firing it. */
  onPick: (verb: string) => void;
}

/**
 * How often the countdown redraws. A quarter second is one pulse, which is as fine as the server
 * can distinguish anyway - a faster timer would render numbers the game does not have.
 */
const TICK_MS = PULSE_MS;

/**
 * What the character can do, and what is still cooling down.
 *
 * <b>The countdown runs here, not on the wire.</b> The server sends one event when an ability
 * fires and nothing after; this derives the number on screen from a stored instant and the clock.
 * The alternative - a frame per pulse per cooling ability - is four a second per player, which is
 * exactly the traffic PLAN.md §11's pulse budget is careful about.
 *
 * <b>The timer only exists while something is cooling.</b> An interval that runs forever on an
 * idle screen is a wakeup every quarter second for a row of numbers that are not changing, and on
 * a phone that is battery.
 */
export function AbilityBar({ abilities, cooldownUntil, onPick }: Props) {
  const [now, setNow] = useState(() => Date.now());

  const cooling = Object.values(cooldownUntil).some((until) => until > now);

  useEffect(() => {
    if (!cooling) return;
    const timer = setInterval(() => setNow(Date.now()), TICK_MS);
    return () => clearInterval(timer);
  }, [cooling]);

  if (abilities.length === 0) {
    return null;
  }

  // A real list of real buttons. An earlier version put role="listitem" on the buttons
  // themselves, which overrides the button role - the control stops announcing as something you
  // can press, which is the one thing about it worth announcing.
  return (
    <ul className="ability-bar" aria-label="Abilities">
      {abilities.map((ability) => {
        const until = cooldownUntil[ability.key] ?? 0;
        const remainingMs = Math.max(0, until - now);
        const ready = remainingMs === 0;

        // Rounded up, so a cooldown reads "1" right until it is actually usable. Rounding to
        // nearest shows "0" on something still refused, which reads as the game being wrong.
        const seconds = Math.ceil(remainingMs / 1000);

        return (
          <li key={ability.key}>
            <button
              type="button"
              className={ready ? "ability-chip" : "ability-chip cooling"}
              // Fills the input rather than casting. A click that fired an ability would be a way
              // to spend a resource by brushing a trackpad, and the game is typed - the point is to
              // teach the verb, not to replace it.
              onClick={() => onPick(ability.verb)}
              title={
                ready
                  ? `${ability.verb} — ${ability.costValue} ${ability.costType.toLowerCase()}`
                  : `${ability.name} — ${seconds}s`
              }
            >
              <span className="ability-chip-name">{ability.name}</span>
              {!ready && <span className="ability-chip-timer">{seconds}</span>}
              {ready && (
                <span className="ability-chip-cost">{ability.costValue}</span>
              )}
            </button>
          </li>
        );
      })}
    </ul>
  );
}
