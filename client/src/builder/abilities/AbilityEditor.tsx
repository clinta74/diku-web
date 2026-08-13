import { useEffect, useState } from 'react'
import {
  builderApi,
  type Ability,
  type CostType,
  type TargetingType,
} from '../../net/builderApi'
import { ABILITY_EFFECTS, effectOption, pruneParams } from '../effects'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { Field } from '../../ui/Field'
import { NumberInput } from '../../ui/NumberInput'
import { Select } from '../../ui/Select'
import { Textarea } from '../../ui/Textarea'
import { useToast } from '../../ui/Toast'

interface Props {
  abilityKey: string
  onChanged: () => void
  onDeleted: () => void
}

const COST_TYPES: CostType[] = ['Stamina', 'Focus', 'Health']
const TARGETING: Array<{ value: TargetingType; label: string }> = [
  { value: 'SingleTarget', label: 'One target' },
  { value: 'Self', label: 'The caster' },
  { value: 'Aoe', label: 'Everyone in the room' },
]

/** One swing. Cooldowns that are not a multiple of this drift against the fight (PLAN.md §2.3). */
const PULSES_PER_BEAT = 8

export function AbilityEditor({ abilityKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const [ability, setAbility] = useState<Ability | null>(null)
  const [draft, setDraft] = useState<Ability | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirming, setConfirming] = useState(false)

  useEffect(() => {
    let cancelled = false
    void builderApi
      .ability(abilityKey)
      .then((loaded) => {
        if (cancelled) return
        setAbility(loaded)
        setDraft(loaded)
      })
      .catch(() => setError('Could not load this ability.'))
    return () => {
      cancelled = true
    }
  }, [abilityKey])

  if (!draft || !ability) {
    return <p className="dim">{error ?? 'Loading…'}</p>
  }

  const set = (patch: Partial<Ability>) => setDraft({ ...draft, ...patch })
  const option = effectOption(draft.effectKey)
  const dirty = JSON.stringify(draft) !== JSON.stringify(ability)

  const beats = draft.cooldownPulses / PULSES_PER_BEAT
  const onBeat = draft.cooldownPulses % PULSES_PER_BEAT === 0

  async function save() {
    if (!draft) return
    setBusy(true)
    setError(null)
    try {
      const saved = await builderApi.updateAbility(draft.key, {
        path: draft.path,
        unlockLevel: draft.unlockLevel,
        name: draft.name,
        description: draft.description,
        costType: draft.costType,
        costValue: draft.costValue,
        cooldownPulses: draft.cooldownPulses,
        castTimePulses: draft.castTimePulses,
        targetingType: draft.targetingType,
        effectKey: draft.effectKey,
        effectParams: pruneParams(draft.effectKey, draft.effectParams) ?? {},
      })
      setAbility(saved)
      setDraft(saved)
      toast.show(`Saved ${saved.name}.`)
      onChanged()
    } catch (e) {
      // The server refuses anything that would not work, and its message names the reason. That
      // is the whole value of the refusal, so it is shown verbatim rather than replaced with
      // "could not save".
      setError(e instanceof Error ? e.message : 'Could not save this ability.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="template-editor">
      <div className="room-editor-head">
        <h2>{ability.name}</h2>
        <code className="dim">{ability.key}</code>
      </div>

      {error && <p className="bad">{error}</p>}

      {ability.problems.length > 0 && (
        <ul className="ability-problems">
          {ability.problems.map((p, i) => (
            <li key={i} className={p.severity === 'Error' ? 'bad' : 'warn'}>
              {p.message}
            </li>
          ))}
        </ul>
      )}

      <div className="field-row">
        <Field label="Name">
          <input value={draft.name} onChange={(e) => set({ name: e.target.value })} />
        </Field>
        <Field
          label="Path"
          hint="Changing this needs the key to match — rename the ability instead."
        >
          <Select value={draft.path} onChange={() => undefined} disabled>
            <option value={draft.path}>{draft.path}</option>
          </Select>
        </Field>
        <Field label="Unlocks at" hint="Character level.">
          <NumberInput
            min={1}
            max={50}
            value={draft.unlockLevel}
            onChange={(v) => set({ unlockLevel: v })}
          />
        </Field>
      </div>

      <Field label="Description" hint="Read by the player on the abilities screen.">
        <Textarea
          value={draft.description}
          onChange={(v) => set({ description: v })}
          rows={3}
        />
      </Field>

      <div className="field-row">
        <Field
          label="Costs"
          hint="Focus for spells, Stamina for skills — the cost type is what makes it one or the other (§4.7)."
        >
          <Select value={draft.costType} onChange={(v) => set({ costType: v as CostType })}>
            {COST_TYPES.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </Select>
        </Field>
        <Field label="Amount">
          <NumberInput min={1} value={draft.costValue} onChange={(v) => set({ costValue: v })} />
        </Field>
        <Field label="Targets">
          <Select
            value={draft.targetingType}
            onChange={(v) => set({ targetingType: v as TargetingType })}
          >
            {TARGETING.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </Select>
        </Field>
      </div>

      <div className="field-row">
        <Field
          label="Cooldown (pulses)"
          hint={
            onBeat
              ? `${draft.cooldownPulses * 0.25}s — ${beats} swing${beats === 1 ? '' : 's'}.`
              : `${draft.cooldownPulses * 0.25}s. Not a whole number of 2s swings, so it drifts against the fight.`
          }
        >
          <NumberInput
            min={0}
            step={PULSES_PER_BEAT}
            value={draft.cooldownPulses}
            onChange={(v) => set({ cooldownPulses: v })}
          />
        </Field>
        <Field
          label="Cast time (pulses)"
          hint="Blank is instant. A cast can be interrupted."
        >
          <NumberInput
            min={0}
            value={draft.castTimePulses ?? 0}
            onChange={(v) => set({ castTimePulses: v === 0 ? null : v })}
          />
        </Field>
      </div>

      <fieldset className="behavior-editor">
        <legend>Effect</legend>

        <Field label="Does" hint={option?.summary}>
          <Select
            value={draft.effectKey}
            onChange={(v) =>
              // The old effect's parameters are dropped rather than carried across. Executors skip
              // keys they do not recognise, so a leftover tickDamage on a heal is invisible rather
              // than wrong — until somebody reads the row and cannot tell what it meant.
              set({ effectKey: v, effectParams: {} })
            }
          >
            {ABILITY_EFFECTS.map((e) => (
              <option key={e.key} value={e.key}>
                {e.label}
              </option>
            ))}
          </Select>
        </Field>

        {option && (
          <div className="stat-grid">
            {option.params.map((param) => (
              <Field key={param.key} label={param.label} hint={param.hint}>
                <input
                  value={draft.effectParams[param.key] ?? ''}
                  placeholder={param.fallback}
                  onChange={(e) =>
                    set({
                      effectParams: { ...draft.effectParams, [param.key]: e.target.value },
                    })
                  }
                />
              </Field>
            ))}
          </div>
        )}

        {!option && (
          <p className="bad">
            This ability names <code>{draft.effectKey}</code>, which no executor implements. It
            costs its resource and does nothing. Pick an effect above.
          </p>
        )}
      </fieldset>

      <div className="row">
        <button type="button" className="primary" disabled={!dirty || busy} onClick={() => void save()}>
          {busy ? 'Saving…' : 'Save'}
        </button>
        <button type="button" disabled={!dirty || busy} onClick={() => setDraft(ability)}>
          Revert
        </button>
        <button type="button" className="danger-button" onClick={() => setConfirming(true)}>
          Delete
        </button>
      </div>

      <ConfirmDialog
        open={confirming}
        onOpenChange={setConfirming}
        title={`Delete ${ability.name}?`}
        confirmLabel="Delete"
        destructive
        onConfirm={async () => {
          await builderApi.deleteAbility(ability.key)
          onDeleted()
        }}
      >
        <p>
          Anyone whose Path and level granted it stops knowing it at once. Characters keep their
          levels; there is simply nothing at this one until something replaces it.
        </p>
      </ConfirmDialog>
    </div>
  )
}
