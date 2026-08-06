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
  const [level, setLevel] = useState(1)
  const [experience, setExperience] = useState(0)
  const [health, setHealth] = useState(10)
  const [mana, setMana] = useState(0)
  const [stamina, setStamina] = useState(10)
  const [error, setError] = useState<string | null>(null)
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
    setLevel(loaded.level)
    setExperience(loaded.experience)
    setHealth(loaded.health)
    setMana(loaded.mana)
    setStamina(loaded.stamina)
    setDirty(false)
  }

  async function save() {
    setBusy(true)
    setError(null)

    try {
      const updated = await builderApi.updateMobTemplate(templateKey, {
        name,
        description,
        level,
        experience,
        health,
        mana,
        stamina,
      })
      apply(updated)
      onChanged(updated)
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
          Experience
          <input
            type="number"
            value={experience}
            onChange={(e) => {
              setExperience(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="0"
          />
        </label>
      </div>

      <div className="field-row">
        <label>
          Health
          <input
            type="number"
            value={health}
            onChange={(e) => {
              setHealth(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="1"
          />
        </label>

        <label>
          Mana
          <input
            type="number"
            value={mana}
            onChange={(e) => {
              setMana(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="0"
          />
        </label>

        <label>
          Stamina
          <input
            type="number"
            value={stamina}
            onChange={(e) => {
              setStamina(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="1"
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
