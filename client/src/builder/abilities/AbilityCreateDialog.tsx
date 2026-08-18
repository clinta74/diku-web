import { useEffect, useState } from 'react'
import { builderApi, type CharacterPath } from '../../net/builderApi'
import { ABILITY_EFFECTS } from '../effects'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { Modal } from '../../ui/Modal'
import { NumberInput } from '../../ui/NumberInput'
import { Select } from '../../ui/Select'

interface Props {
  open: boolean
  onOpenChange: (open: boolean) => void
  onCreated: (key: string) => void
}

const PATHS: CharacterPath[] = ['Warden', 'Adept', 'Shade', 'Hallow']

/**
 * Creating an ability needs more than a key and a name, which is why it has its own dialog rather
 * than using the shared one the template trees share.
 *
 * The Path is the reason. An ability key is `<path>.<name>` and the server refuses one whose
 * prefix does not match, because `AbilityLookup` resolves the full key — a misfiled key is a name
 * that reaches another Path's ability. So the Path is picked here and the prefix is composed
 * rather than typed, which makes the rule impossible to break instead of merely enforced.
 */
export function AbilityCreateDialog({ open, onOpenChange, onCreated }: Props) {
  const [path, setPath] = useState<CharacterPath>('Warden')
  const [slug, setSlug] = useState('')
  const [name, setName] = useState('')
  const [unlockLevel, setUnlockLevel] = useState(1)
  const [effectKey, setEffectKey] = useState(ABILITY_EFFECTS[0].key)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    setPath('Warden')
    setSlug('')
    setName('')
    setUnlockLevel(1)
    setEffectKey(ABILITY_EFFECTS[0].key)
    setError(null)
    setBusy(false)
  }, [open])

  const key = `${path.toLowerCase()}.${slug}`

  async function submit() {
    if (!/^[a-z0-9-]+$/.test(slug)) {
      setError('The second half of the key is lowercase letters, digits, or hyphens.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      await builderApi.createAbility(key, {
        path,
        unlockLevel,
        name: name.trim() || slug,
        description: '',
        costType: 'Stamina',
        costValue: 10,
        cooldownPulses: 24,
        // No shared timer. A new ability shares nothing until somebody says otherwise.
        cooldownGroup: null,
        castTimePulses: null,
        targetingType: 'SingleTarget',
        // One effect to begin with; more are added in the editor. Seeded with each parameter's
        // own fallback, so a freshly created ability is valid rather than being refused for a
        // blank the builder has not reached yet.
        effects: [
          {
            key: effectKey,
            params: Object.fromEntries(
              (ABILITY_EFFECTS.find((e) => e.key === effectKey)?.params ?? []).map((p) => [
                p.key,
                p.fallback,
              ]),
            ),
          },
        ],
      })
      onCreated(key)
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the ability.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="New ability"
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <Button variant="primary" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Creating…' : 'Create'}
          </Button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}

      <Field label="Path" hint="Decides who learns it, and the first half of the key.">
        <Select value={path} onChange={(v) => setPath(v as CharacterPath)}>
          {PATHS.map((p) => (
            <option key={p} value={p}>
              {p}
            </option>
          ))}
        </Select>
      </Field>

      <Field label="Key" hint={`Stored as ${key || `${path.toLowerCase()}.…`}`}>
        <div className="prefixed-input">
          <code>{path.toLowerCase()}.</code>
          <input value={slug} placeholder="shield-bash" onChange={(e) => setSlug(e.target.value)} />
        </div>
      </Field>

      <Field label="Name" hint="What a player types. Defaults to the key.">
        <input value={name} placeholder="Shield Bash" onChange={(e) => setName(e.target.value)} />
      </Field>

      <Field label="Unlocks at">
        <NumberInput min={1} max={50} value={unlockLevel} onChange={setUnlockLevel} />
      </Field>

      <Field label="Does" hint="Changeable afterwards; picking here seeds sensible parameters.">
        <Select value={effectKey} onChange={setEffectKey}>
          {ABILITY_EFFECTS.map((e) => (
            <option key={e.key} value={e.key}>
              {e.label}
            </option>
          ))}
        </Select>
      </Field>
    </Modal>
  )
}
