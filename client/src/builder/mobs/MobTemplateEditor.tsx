import { useEffect, useState } from 'react'
import { builderApi, type MobTemplate } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { NumberInput } from '../../ui/NumberInput'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'

interface Props {
  templateKey: string
  onChanged: (template: MobTemplate) => void
  onDeleted: (key: string) => void
}

export function MobTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const [template, setTemplate] = useState<MobTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('m')
  const [level, setLevel] = useState(1)
  const [wanderIntervalPulses, setWanderIntervalPulses] = useState(24)
  const [baseHealth, setBaseHealth] = useState(40)
  const [baseXp, setBaseXp] = useState(0)
  const [baseGold, setBaseGold] = useState(0)
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
        setBaseHealth(loaded.baseStats?.health ? Number(loaded.baseStats.health) : 40)
        setBaseXp(loaded.baseXp)
        setBaseGold(loaded.baseGold)
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
        // Spread the loaded stats so editing health does not drop the other stats, and carry
        // loot and behavior through untouched - the old editor silently wiped both (PLAN §9.2).
        baseStats: { ...template.baseStats, health: baseHealth },
        baseXp,
        baseGold,
        loot: template.loot,
        behavior: template.behavior,
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
        <Field label="Base health">
          <NumberInput min={1} value={baseHealth} onChange={change(setBaseHealth)} />
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
