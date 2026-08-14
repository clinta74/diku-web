import type { MobAttack } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { NumberInput } from '../../ui/NumberInput'
import { Select } from '../../ui/Select'
import { ATTACK_EFFECTS, effectOption, pruneParams } from '../effects'

interface Props {
  attacks: MobAttack[]
  onChange: (next: MobAttack[]) => void
}

/**
 * What a mob swings, how often, and what each swing carries.
 *
 * **One card per attack.** This was two lists over the same array - every attack's timing in the
 * first, every attack's effect in the second - which meant that adding a third attack pushed its
 * effect three blocks away from its timing, and the only thing tying the two halves together was
 * a label quoting the verb. Two attacks sharing a verb made even that ambiguous, and a fresh
 * attack defaults to "hit", so clicking Add twice produced exactly that.
 *
 * An attack is one thing, so it is edited as one thing. The effect fields nest under the effect
 * that owns them for the same reason: they are meaningless without it, and one of the four
 * effects reads five of them.
 */
export function AttackEditor({ attacks, onChange }: Props) {
  const edit = (index: number, patch: Partial<MobAttack>) =>
    onChange(attacks.map((a, i) => (i === index ? { ...a, ...patch } : a)))

  /**
   * Changing the effect discards the previous one's parameters. The executors skip what they do
   * not recognise, so a stale `tickDamage` left on a stun would be invisible rather than wrong -
   * until someone reads the row and cannot tell what it was meant to do.
   */
  const editEffect = (index: number, effectKey: string) =>
    edit(index, {
      effectKey: effectKey || null,
      effectParams: pruneParams(effectKey, attacks[index].effectParams),
    })

  const editParam = (index: number, key: string, value: string) => {
    const next = { ...(attacks[index].effectParams ?? {}), [key]: value }
    edit(index, { effectParams: pruneParams(attacks[index].effectKey, next) })
  }

  return (
    <fieldset className="attack-list">
      <legend>Attacks</legend>
      <p className="dim detail">
        Each attack keeps its own timer, so two attacks are two independent swings rather than one
        chosen from two. Leave the list empty and this mob hits once every 8 pulses for its own
        damage.
      </p>

      {attacks.length === 0 && <p className="dim">No attacks of its own.</p>}

      {attacks.map((attack, index) => {
        const option = effectOption(attack.effectKey)

        return (
          <div className="attack-card" key={index}>
            <div className="attack-card-head">
              <span className="field-label">
                Attack {index + 1}
                {attack.verb && <span className="dim"> · “{attack.verb}”</span>}
              </span>
              <button
                type="button"
                className="danger-button"
                onClick={() => onChange(attacks.filter((_, i) => i !== index))}
              >
                Remove
              </button>
            </div>

            <div className="field-row">
              <Field label="Message" hint="Base form: bite, claw, gore.">
                <input value={attack.verb} onChange={(e) => edit(index, { verb: e.target.value })} />
              </Field>
              <Field label="Delay (pulses)" hint="Minimum 4 ≈ 1s.">
                <NumberInput
                  min={4}
                  value={attack.delayPulses}
                  onChange={(v) => edit(index, { delayPulses: v })}
                />
              </Field>
              <Field label="Damage ×" hint="Blank = the mob’s own damage.">
                <input
                  type="text"
                  inputMode="decimal"
                  value={attack.damageMultiplier ?? ''}
                  onChange={(e) => {
                    const raw = e.target.value.trim()
                    edit(index, {
                      damageMultiplier: raw === '' || Number.isNaN(Number(raw)) ? null : Number(raw),
                    })
                  }}
                />
              </Field>
            </div>

            <Field
              /* "On a hit" rather than "also", because when it applies is the part that is not
                 obvious: the effect rides the swing's damage, so it inherits the miss chance and
                 the parry, and lands only on someone the blow left standing. */
              label="On a hit, also…"
              hint={option?.summary ?? 'Nothing beyond its damage.'}
            >
              <Select value={attack.effectKey ?? ''} onChange={(v) => editEffect(index, v)}>
                <option value="">— nothing —</option>
                {ATTACK_EFFECTS.map((effect) => (
                  <option key={effect.key} value={effect.key}>
                    {effect.label}
                  </option>
                ))}
              </Select>
            </Field>

            {option && (
              <div className="attack-effect">
                <div className="stat-grid">
                  {option.params.map((param) => (
                    <Field key={param.key} label={param.label} hint={param.hint}>
                      <input
                        value={attack.effectParams?.[param.key] ?? ''}
                        placeholder={param.fallback}
                        inputMode={param.integer ? 'numeric' : 'text'}
                        onChange={(e) => editParam(index, param.key, e.target.value)}
                      />
                    </Field>
                  ))}
                </div>
                <p className="dim">
                  Blank means the default shown in each box. Only harmful effects are offered — a
                  rider lands on whoever the attack hit.
                </p>
              </div>
            )}
          </div>
        )
      })}

      <button
        type="button"
        onClick={() =>
          onChange([
            ...attacks,
            { verb: 'hit', delayPulses: 8, damageMultiplier: null, effectKey: null, effectParams: null },
          ])
        }
      >
        Add attack
      </button>
    </fieldset>
  )
}
