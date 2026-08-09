import { useEffect, useState } from 'react'
import { builderApi, type MobAttack, type MobTemplate } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { NumberInput } from '../../ui/NumberInput'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'
import { useBuilderData } from '../BuilderData'
import { MobBehaviorEditor } from './MobBehaviorEditor'
import { readBehavior, writeBehavior, type BehaviorDraft } from './behavior'
import { MobStatsEditor } from './MobStatsEditor'
import { LootEditor } from './LootEditor'
import { readLoot, writeLoot, type LootRow } from './mobStats'
import { Select } from '../../ui/Select'
import { ATTACK_EFFECTS, effectOption, pruneParams } from './attackEffects'

interface Props {
  templateKey: string
  onChanged: (template: MobTemplate) => void
  onDeleted: (key: string) => void
}

export function MobTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const { itemTemplates } = useBuilderData()
  const [template, setTemplate] = useState<MobTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('m')
  const [level, setLevel] = useState(1)
  const [wanderIntervalPulses, setWanderIntervalPulses] = useState(24)
  const [baseStats, setBaseStats] = useState<Record<string, unknown>>({})
  const [loot, setLoot] = useState<LootRow[]>([])
  const [baseXp, setBaseXp] = useState(0)
  const [baseGold, setBaseGold] = useState(0)
  const [attacks, setAttacks] = useState<MobAttack[]>([])
  const [behavior, setBehavior] = useState<BehaviorDraft>(readBehavior(undefined))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    let cancelled = false
    setTemplate(null)
    setError(null)
    void builderApi
      .mobTemplate(templateKey)
      .then((loaded) => {
        if (cancelled) return
        setTemplate(loaded)
        setName(loaded.name)
        setDescription(loaded.description)
        setIcon(loaded.icon)
        setLevel(loaded.level)
        setWanderIntervalPulses(loaded.wanderIntervalPulses ?? 24)
        // Kept verbatim. Only the keys the form owns are written, in place - the item editor
        // learned this the hard way when a blanket Number() coercion zeroed every dice string.
        setBaseStats(
          loaded.baseStats && typeof loaded.baseStats === 'object' ? { ...loaded.baseStats } : {},
        )
        setLoot(readLoot(loaded.loot))
        setBaseXp(loaded.baseXp)
        setBaseGold(loaded.baseGold)
        setAttacks(loaded.attacks ?? [])
        setBehavior(readBehavior(loaded.behavior))
        setDirty(false)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load that template.')
      })
    return () => {
      cancelled = true
    }
  }, [templateKey])

  async function save() {
    if (!template) return
    setBusy(true)
    setError(null)
    try {
      const updated = await builderApi.updateMobTemplate(templateKey, {
        name,
        description,
        icon,
        level,
        wanderIntervalPulses,
        baseStats,
        baseXp,
        baseGold,
        loot: writeLoot(loot),
        // Folded into the stored bag rather than replacing it, so keys this form does not
        // render survive the save.
        behavior: writeBehavior(template.behavior, behavior),
        attacks,
      })
      setTemplate(updated)
      setDirty(false)
      onChanged(updated)
      toast.notify('Template saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  async function confirmDelete() {
    setBusy(true)
    try {
      await builderApi.deleteMobTemplate(templateKey)
      toast.notify('Template deleted')
      setDeleting(false)
      onDeleted(templateKey)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  const change = <T,>(setter: (v: T) => void) => (v: T) => {
    setter(v)
    setDirty(true)
  }

  function editAttack(index: number, patch: Partial<MobAttack>) {
    setAttacks((current) => current.map((a, i) => (i === index ? { ...a, ...patch } : a)))
    setDirty(true)
  }

  /**
   * Changing the effect discards the previous one's parameters. The executors skip what they do
   * not recognise, so a stale `tickDamage` left on a stun would be invisible rather than wrong -
   * until someone reads the row and cannot tell what it was meant to do.
   */
  function editAttackEffect(index: number, effectKey: string) {
    setAttacks((current) =>
      current.map((a, i) =>
        i === index
          ? { ...a, effectKey: effectKey || null, effectParams: pruneParams(effectKey, a.effectParams) }
          : a,
      ),
    )
    setDirty(true)
  }

  function editAttackParam(index: number, key: string, value: string) {
    setAttacks((current) =>
      current.map((a, i) => {
        if (i !== index) return a
        const next = { ...(a.effectParams ?? {}), [key]: value }
        return { ...a, effectParams: pruneParams(a.effectKey, next) }
      }),
    )
    setDirty(true)
  }

  function addAttack() {
    setAttacks((current) => [
      ...current,
      { verb: 'hit', delayPulses: 8, damageMultiplier: null, effectKey: null, effectParams: null },
    ])
    setDirty(true)
  }

  function removeAttack(index: number) {
    setAttacks((current) => current.filter((_, i) => i !== index))
    setDirty(true)
  }

  if (error && !template) return <p className="bad">{error}</p>
  if (!template) return <p className="dim">Loading…</p>

  return (
    <div className="template-editor">
      <header className="room-editor-head">
        <div>
          <h2>{name || templateKey}</h2>
          <code className="room-key">{templateKey}</code>
        </div>
        <OverflowMenu
          actions={[{ label: 'Delete template…', onSelect: () => setDeleting(true), destructive: true }]}
        />
      </header>

      {error && <p className="bad">{error}</p>}

      <Field label="Name">
        <input value={name} onChange={(e) => change(setName)(e.target.value)} />
      </Field>

      <Field label="Description">
        <Textarea rows={3} value={description} onChange={change(setDescription)} />
      </Field>

      <div className="field-row">
        <Field label="Icon">
          <input
            value={icon}
            maxLength={1}
            onChange={(e) => change(setIcon)(e.target.value.slice(0, 1) || 'm')}
          />
        </Field>
        <Field label="Level">
          <NumberInput min={1} value={level} onChange={change(setLevel)} />
        </Field>
        <Field label="Wander (pulses)" hint="Lower = faster. 24 ≈ 6s.">
          <NumberInput min={1} value={wanderIntervalPulses} onChange={change(setWanderIntervalPulses)} />
        </Field>
      </div>

      <div className="field-row">
        <Field label="Base XP">
          <NumberInput min={0} value={baseXp} onChange={change(setBaseXp)} />
        </Field>
        <Field label="Base gold">
          <NumberInput min={0} value={baseGold} onChange={change(setBaseGold)} />
        </Field>
      </div>

      <MobStatsEditor
        stats={baseStats}
        onChange={(next) => {
          setBaseStats(next)
          setDirty(true)
        }}
      />

      <LootEditor
        rows={loot}
        itemTemplates={itemTemplates}
        onChange={(next) => {
          setLoot(next)
          setDirty(true)
        }}
      />

      <MobBehaviorEditor
        draft={behavior}
        itemTemplates={itemTemplates}
        onChange={(next) => {
          setBehavior(next)
          setDirty(true)
        }}
      />

      <fieldset className="attack-list">
        <legend>Attacks</legend>
        <p className="dim">
          Each attack runs on its own timer, so two attacks means two independent swings. Leave
          the list empty and this mob hits once every 8 pulses.
        </p>

        {attacks.map((attack, index) => (
          <div className="field-row" key={index}>
            <Field label="Message" hint="Base form: bite, claw, gore.">
              <input
                value={attack.verb}
                onChange={(e) => editAttack(index, { verb: e.target.value })}
              />
            </Field>
            <Field label="Delay (pulses)" hint="Minimum 4 ≈ 1s.">
              <NumberInput
                min={4}
                value={attack.delayPulses}
                onChange={(v) => editAttack(index, { delayPulses: v })}
              />
            </Field>
            <Field label="Damage ×" hint="Blank = same as the mob's damage.">
              <input
                type="text"
                inputMode="decimal"
                value={attack.damageMultiplier ?? ''}
                onChange={(e) => {
                  const raw = e.target.value.trim()
                  editAttack(index, {
                    damageMultiplier: raw === '' || Number.isNaN(Number(raw)) ? null : Number(raw),
                  })
                }}
              />
            </Field>
            <button type="button" className="danger" onClick={() => removeAttack(index)}>
              Remove
            </button>
          </div>
        ))}

        {attacks.map((attack, index) => {
          const option = effectOption(attack.effectKey)
          return (
            <div className="attack-effect" key={`effect-${index}`}>
              <Field
                label={`“${attack.verb || 'hit'}” also…`}
                hint={option?.summary ?? 'Nothing beyond its damage.'}
              >
                <Select
                  value={attack.effectKey ?? ''}
                  onChange={(v) => editAttackEffect(index, v)}
                >
                  <option value="">— nothing —</option>
                  {ATTACK_EFFECTS.map((effect) => (
                    <option key={effect.key} value={effect.key}>
                      {effect.label}
                    </option>
                  ))}
                </Select>
              </Field>

              {option && (
                <div className="stat-grid">
                  {option.params.map((param) => (
                    <Field key={param.key} label={param.label} hint={param.hint}>
                      <input
                        value={attack.effectParams?.[param.key] ?? ''}
                        placeholder={param.fallback}
                        inputMode={param.integer ? 'numeric' : 'text'}
                        onChange={(e) => editAttackParam(index, param.key, e.target.value)}
                      />
                    </Field>
                  ))}
                </div>
              )}
            </div>
          )
        })}

        <button type="button" onClick={addAttack}>
          Add attack
        </button>
      </fieldset>

      <div className="row">
        <button type="button" className="primary" disabled={!dirty || busy} onClick={() => void save()}>
          {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
        </button>
      </div>

      <ConfirmDialog
        open={deleting}
        onOpenChange={setDeleting}
        title={`Delete ${templateKey}?`}
        description="This cannot be undone. Spawners referencing it will stop working."
        destructive
        busy={busy}
        confirmLabel="Delete template"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}
