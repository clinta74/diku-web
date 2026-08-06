import { useEffect, useState } from 'react'
import { builderApi, type ItemTemplate } from '../net/builderApi'

interface Props {
  templateKey: string
  onChanged: (template: ItemTemplate) => void
  onDeleted: (key: string) => void
}

const ITEM_SLOTS = ['Head', 'Chest', 'Hands', 'Legs', 'Feet', 'MainHand', 'OffHand', 'Trinket']

export function ItemTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const [template, setTemplate] = useState<ItemTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('i')
  const [slot, setSlot] = useState<string | null>(null)
  const [weight, setWeight] = useState(0)
  const [baseValue, setBaseValue] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    let cancelled = false

    setTemplate(null)
    setError(null)

    void builderApi
      .itemTemplate(templateKey)
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

  function apply(loaded: ItemTemplate) {
    setTemplate(loaded)
    setName(loaded.name)
    setDescription(loaded.description)
    setIcon(loaded.icon)
    setSlot(loaded.slot)
    setWeight(loaded.weight)
    setBaseValue(loaded.baseValue)
    setDirty(false)
    setSuccess(null)
  }

  async function save() {
    setBusy(true)
    setError(null)
    setSuccess(null)

    try {
      const updated = await builderApi.updateItemTemplate(templateKey, {
        name,
        description,
        icon,
        slot,
        weight,
        baseValue,
        baseStats: {},
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
    if (!window.confirm(`Delete item template "${templateKey}"? This cannot be undone.`)) return

    setBusy(true)
    setError(null)

    try {
      await builderApi.deleteItemTemplate(templateKey)
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
    <div className="item-editor">
      <header>
        <h2>{name || templateKey}</h2>
        <code className="item-key">{templateKey}</code>
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
              setIcon(e.target.value.slice(0, 1) || 'i')
              setDirty(true)
            }}
            disabled={busy}
            maxLength={1}
          />
        </label>

        <label>
          Slot
          <select
            value={slot || ''}
            onChange={(e) => {
              setSlot(e.target.value || null)
              setDirty(true)
            }}
            disabled={busy}
          >
            <option value="">— None (ground item) —</option>
            {ITEM_SLOTS.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </label>
      </div>

      <div className="field-row">
        <label>
          Weight (grams)
          <input
            type="number"
            value={weight}
            onChange={(e) => {
              setWeight(parseInt(e.target.value) || 0)
              setDirty(true)
            }}
            disabled={busy}
            min="0"
          />
        </label>

        <label>
          Base Value
          <input
            type="number"
            value={baseValue}
            onChange={(e) => {
              setBaseValue(parseInt(e.target.value) || 0)
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
