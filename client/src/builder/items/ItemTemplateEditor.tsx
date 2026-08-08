import { useEffect, useRef, useState } from 'react'
import { builderApi, type ItemTemplate } from '../../net/builderApi'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { Select } from '../../ui/Select'
import { NumberInput } from '../../ui/NumberInput'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'

interface Props {
  templateKey: string
  onChanged: (template: ItemTemplate) => void
  onDeleted: (key: string) => void
}

const ITEM_SLOTS = ['Head', 'Chest', 'Hands', 'Legs', 'Feet', 'MainHand', 'OffHand', 'Trinket']
const STAT_MULTIPLIERS = [
  'damageMultiplier',
  'armorMultiplier',
  'healthMultiplier',
  'focusMultiplier',
  'staminaMultiplier',
]

export function ItemTemplateEditor({ templateKey, onChanged, onDeleted }: Props) {
  const toast = useToast()
  const [template, setTemplate] = useState<ItemTemplate | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [icon, setIcon] = useState('i')
  const [slot, setSlot] = useState<string | null>(null)
  const [weight, setWeight] = useState(0)
  const [baseValue, setBaseValue] = useState(0)
  const [baseStats, setBaseStats] = useState<Record<string, number>>({})
  // Weapon speed and verb are columns, not base stats: the coercion below would turn a verb
  // into 0, and a delay below the floor has to be refusable by the server.
  const [attackDelayPulses, setAttackDelayPulses] = useState<number | null>(null)
  const [attackVerb, setAttackVerb] = useState('')
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
        setBaseStats(
          loaded.baseStats && typeof loaded.baseStats === 'object'
            ? Object.fromEntries(
                Object.entries(loaded.baseStats).map(([k, v]) => [k, Number(v) || 0]),
              )
            : {},
        )
        setAttackDelayPulses(loaded.attackDelayPulses ?? null)
        setAttackVerb(loaded.attackVerb ?? '')
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
        <legend>As a weapon</legend>
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

      <fieldset className="multiplier-set">
        <legend>Stat multipliers (when worn/wielded)</legend>
        <p className="dim detail">1.0 = no change, 1.1 = +10%, 0.9 = -10%. Blank removes it.</p>
        {STAT_MULTIPLIERS.map((stat) => (
          <Field key={stat} label={stat}>
            <MultiplierInput
              value={baseStats[stat]}
              onChange={(next) => {
                setBaseStats((prev) => {
                  const copy = { ...prev }
                  if (next === undefined) delete copy[stat]
                  else copy[stat] = next
                  return copy
                })
                touch()
              }}
            />
          </Field>
        ))}
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

/**
 * A decimal field where blank means "no multiplier" (the key is removed), distinct from 0 which
 * would zero the stat. Keeps a text buffer so partial entries like "1." are typable, and filters
 * to digits and a single dot - no spinner, free text entry, same as the other numeric fields.
 */
function MultiplierInput({
  value,
  onChange,
}: {
  value: number | undefined
  onChange: (value: number | undefined) => void
}) {
  const [text, setText] = useState(() => (value === undefined ? '' : String(value)))
  const focused = useRef(false)

  useEffect(() => {
    if (!focused.current) setText(value === undefined ? '' : String(value))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  function handle(raw: string) {
    let out = ''
    let dot = false
    for (const c of raw) {
      if (c >= '0' && c <= '9') out += c
      else if (c === '.' && !dot) {
        out += c
        dot = true
      }
    }
    setText(out)
    if (out === '' || out === '.') {
      onChange(undefined)
      return
    }
    const parsed = Number(out)
    if (Number.isFinite(parsed)) onChange(parsed)
  }

  return (
    <input
      type="text"
      inputMode="decimal"
      className="number-input"
      value={text}
      placeholder="—"
      spellCheck={false}
      onFocus={() => {
        focused.current = true
      }}
      onBlur={() => {
        focused.current = false
        setText(value === undefined ? '' : String(value))
      }}
      onChange={(e) => handle(e.target.value)}
    />
  )
}
