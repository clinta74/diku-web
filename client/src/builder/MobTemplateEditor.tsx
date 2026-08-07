import { useEffect, useState } from 'react'
import { builderApi, type MobTemplate } from '../net/builderApi'

interface Props {
  templateKey: string
  onChanged: (template: MobTemplate) => void
  onDeleted: (key: string) => void
}

export function MobTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
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
  const [success, setSuccess] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    let cancelled = false

    setTemplate(null)
    setError(null)

    void builderApi
      .mobTemplate(templateKey)
      .then((loaded) => {
        if (cancelled) return
        apply(loaded)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load that template.')
      })

    return () => {
      cancelled = true
    }
  }, [templateKey])

  function apply(loaded: MobTemplate) {
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
    setSuccess(null)
  }

  async function save() {
    setBusy(true)
    setError(null)
    setSuccess(null)

    try {
      const updated = await builderApi.updateMobTemplate(templateKey, {
        name,
        description,
        icon,
        level,
        wanderIntervalPulses,
        baseStats: { health: baseHealth },
        baseXp,
        baseGold,
      })
      apply(updated)
      onChanged(updated)
      setSuccess('Saved!')
      setTimeout(() => setSuccess(null), 3000)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  async function deleteTemplate() {
    if (!window.confirm(`Delete mob template "${templateKey}"? This cannot be undone.`)) return

    setBusy(true)
    setError(null)

    try {
      await builderApi.deleteMobTemplate(templateKey)
      onDeleted(templateKey)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  if (error && !template) return <p className="bad">{error}</p>
  if (!template) return <p className="dim">Loading…</p>

  return (
    <div className="mob-editor">
      <header>
        <h2>{name || templateKey}</h2>
        <code className="mob-key">{templateKey}</code>
      </header>

      {error && <p className="bad">{error}</p>}
      {success && <p className="good">{success}</p>}

      <label>
        Name
        <input
          value={name}
          onChange={(e) => {
            setName(e.target.value)
            setDirty(true)
          }}
          disabled={busy}
        />
      </label>

      <label>
        Description
        <textarea
          value={description}
          onChange={(e) => {
            setDescription(e.target.value)
            setDirty(true)
          }}
          disabled={busy}
          rows={3}
        />
      </label>

      <div className="field-row">
        <label>
          Icon (single character)
          <input
            value={icon}
            onChange={(e) => {
              setIcon(e.target.value.slice(0, 1) || 'm')
              setDirty(true)
            }}
            disabled={busy}
            maxLength={1}
          />
        </label>

        <label>
          Level
          <input
            type="number"
            value={level}
            onChange={(e) => {
              setLevel(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="1"
          />
        </label>

        <label>
          Wander Speed (pulses)
          <input
            type="number"
            value={wanderIntervalPulses}
            onChange={(e) => {
              setWanderIntervalPulses(parseInt(e.target.value) || 24)
              setDirty(true)
            }}
            disabled={busy}
            min="1"
            title="Lower = faster movement. 24 pulses = 6 seconds (default)"
          />
        </label>

        <label>
          Base Health
          <input
            type="number"
            value={baseHealth}
            onChange={(e) => {
              setBaseHealth(parseInt(e.target.value) || 40)
              setDirty(true)
            }}
            disabled={busy}
            min="1"
          />
        </label>
      </div>

      <div className="field-row">
        <label>
          Base XP
          <input
            type="number"
            value={baseXp}
            onChange={(e) => {
              setBaseXp(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="0"
          />
        </label>

        <label>
          Base Gold
          <input
            type="number"
            value={baseGold}
            onChange={(e) => {
              setBaseGold(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="0"
          />
        </label>
      </div>

      <div className="actions">
        <button onClick={() => void save()} disabled={!dirty || busy} className="good">
          Save
        </button>
        <button onClick={() => void deleteTemplate()} disabled={busy} className="bad">
          Delete
        </button>
      </div>
    </div>
  )
}
