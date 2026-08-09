import { useEffect, useState } from 'react'
import { builderApi, type ItemTemplate } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { Select } from '../../ui/Select'
import { NumberInput } from '../../ui/NumberInput'
import { NumberField } from '../../ui/NumberField'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'
import { asNumber, OWNED_STAT_KEYS, STAT_GROUPS } from './stats'

interface Props {
  templateKey: string
  onChanged: (template: ItemTemplate) => void
  onDeleted: (key: string) => void
}

const ITEM_SLOTS = ['Head', 'Chest', 'Hands', 'Legs', 'Feet', 'MainHand', 'OffHand', 'Trinket']

export function ItemTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const [template, setTemplate] = useState<ItemTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('i')
  const [slot, setSlot] = useState<string | null>(null)
  const [weight, setWeight] = useState(0)
  const [baseValue, setBaseValue] = useState(0)
  // Deliberately `unknown`, not `number`. This bag is schemaless (PLAN.md §4.8) and carries
  // values this form does not render - `damage: "1d6"` most of all. It used to be loaded through
  // `Number(v) || 0`, which turned every one of them into 0 and saved that back, so editing an
  // item's *name* silently destroyed its damage dice.
  const [baseStats, setBaseStats] = useState<Record<string, unknown>>({})
  // Weapon speed and verb are columns, not base stats: the coercion below would turn a verb
  // into 0, and a delay below the floor has to be refusable by the server.
  const [attackDelayPulses, setAttackDelayPulses] = useState<number | null>(null)
  const [attackVerb, setAttackVerb] = useState('')
  const [isQuestItem, setIsQuestItem] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    let cancelled = false
    setTemplate(null)
    setError(null)
    void builderApi
      .itemTemplate(templateKey)
      .then((loaded) => {
        if (cancelled) return
        setTemplate(loaded)
        setName(loaded.name)
        setDescription(loaded.description)
        setIcon(loaded.icon)
        setSlot(loaded.slot)
        setWeight(loaded.weight)
        setBaseValue(loaded.baseValue)
        // Kept verbatim. Only the keys this form owns are ever written, in place.
        setBaseStats(
          loaded.baseStats && typeof loaded.baseStats === 'object' ? { ...loaded.baseStats } : {},
        )
        setAttackDelayPulses(loaded.attackDelayPulses ?? null)
        setAttackVerb(loaded.attackVerb ?? '')
        setIsQuestItem(loaded.isQuestItem)
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
    setBusy(true)
    setError(null)
    try {
      const updated = await builderApi.updateItemTemplate(templateKey, {
        name,
        description,
        icon,
        slot,
        weight,
        baseValue,
        baseStats,
        attackDelayPulses,
        attackVerb: attackVerb.trim() === '' ? null : attackVerb.trim(),
        isQuestItem,
      })
      setTemplate(updated)
      setSlot(updated.slot)
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
      await builderApi.deleteItemTemplate(templateKey)
      toast.notify('Template deleted')
      setDeleting(false)
      onDeleted(templateKey)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  const touch = () => setDirty(true)

  // Everything in the bag this form has no field for. Sorted so the list does not reshuffle
  // between renders.
  const carriedStats = Object.entries(baseStats)
    .filter(([key]) => !OWNED_STAT_KEYS.includes(key))
    .sort(([a], [b]) => a.localeCompare(b))

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
        <input
          value={name}
          onChange={(e) => {
            setName(e.target.value)
            touch()
          }}
        />
      </Field>

      <Field label="Description">
        <Textarea
          rows={3}
          value={description}
          onChange={(v) => {
            setDescription(v)
            touch()
          }}
        />
      </Field>

      <div className="field-row">
        <Field label="Icon">
          <input
            value={icon}
            maxLength={1}
            onChange={(e) => {
              setIcon(e.target.value.slice(0, 1) || 'i')
              touch()
            }}
          />
        </Field>

        <Field label="Slot">
          {/* slot ?? '' - Head is 0-valued on the server, so `slot || ''` used to hide it. */}
          <Select
            value={slot ?? ''}
            onChange={(v) => {
              setSlot(v || null)
              touch()
            }}
          >
            <option value="">— None (ground item) —</option>
            {ITEM_SLOTS.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
        </Field>
      </div>

      <div className="field-row">
        <Field label="Weight (grams)">
          <NumberInput
            min={0}
            value={weight}
            onChange={(v) => {
              setWeight(v)
              touch()
            }}
          />
        </Field>

        <Field label="Base value">
          <NumberInput
            min={0}
            value={baseValue}
            onChange={(v) => {
              setBaseValue(v)
              touch()
            }}
          />
        </Field>
      </div>

      <fieldset className="multiplier-set">
        <legend>Swing timing</legend>
        <p className="dim detail">
          Blank speed means this is not a weapon: in a main hand it swings at the default 8
          pulses, in an off hand it never strikes at all.
        </p>
        <div className="field-row">
          <Field label="Attack delay (pulses)" hint="Minimum 4 ≈ 1s. Lower = faster.">
            <input
              type="text"
              inputMode="numeric"
              value={attackDelayPulses ?? ''}
              onChange={(e) => {
                const raw = e.target.value.trim()
                setAttackDelayPulses(raw === '' || Number.isNaN(Number(raw)) ? null : Number(raw))
                touch()
              }}
            />
          </Field>
          <Field label="Attack verb" hint="Base form: slash, crush, stab.">
            <input
              value={attackVerb}
              maxLength={24}
              onChange={(e) => {
                setAttackVerb(e.target.value)
                touch()
              }}
            />
          </Field>
        </div>
      </fieldset>

      <label className="field-check">
        <input
          type="checkbox"
          checked={isQuestItem}
          onChange={(e) => {
            setIsQuestItem(e.target.checked)
            touch()
          }}
        />
        Quest item — cannot be sold or destroyed, but can still be dropped
      </label>
      {isQuestItem && (
        <p className="dim detail">
          Stamped onto each copy when it spawns, so items already in a pack keep the rule they
          were created under. Turning this off will not free copies that already exist.
        </p>
      )}

      {STAT_GROUPS.map((group) => (
        <fieldset className="multiplier-set" key={group.label}>
          <legend>{group.label}</legend>
          {group.hint && <p className="dim detail">{group.hint}</p>}

          <div className="stat-grid">
            {group.fields.map((field) => (
              <Field key={field.key} label={field.label} hint={field.hint}>
                <NumberField
                  value={asNumber(baseStats[field.key])}
                  integer={field.kind === 'int'}
                  onChange={(next) => {
                    setBaseStats((prev) => {
                      const copy = { ...prev }
                      // Blank removes the key rather than writing 0. Zero armour and *no*
                      // armour resolve the same today, but a stored 0 is a claim the item
                      // makes, and a later rule that treats them differently would be wrong.
                      if (next === undefined) delete copy[field.key]
                      else copy[field.key] = next
                      return copy
                    })
                    touch()
                  }}
                />
              </Field>
            ))}
          </div>
        </fieldset>
      ))}

      <fieldset className="multiplier-set">
        <legend>Other stats</legend>
        {carriedStats.length === 0 && (
          <p className="dim detail">Nothing beyond the fields above.</p>
        )}

        {carriedStats.length > 0 && (
          /* Shown, not editable. These survive a save either way, but a builder who cannot see
             them has no way to know an item has a damage die at all - and invisible content is
             how the coercion bug went unnoticed. */
          <p className="dim detail">
            Also on this item, carried through unchanged:{' '}
            {carriedStats.map(([key, value]) => `${key} = ${String(value)}`).join(', ')}
          </p>
        )}
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
        description="This cannot be undone."
        destructive
        busy={busy}
        confirmLabel="Delete template"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}
