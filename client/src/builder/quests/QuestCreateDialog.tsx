import { useEffect, useState } from 'react'
import { builderApi, type ZoneSummary } from '../../net/builderApi'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { useBuilderData } from '../BuilderData'
import { TemplatePicker } from '../templates/TemplatePicker'
import { newQuest } from './quests'

/** Mirrors the server's IsKeySegment: lowercase, digits, internal hyphens. */
const KEY_SEGMENT = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

interface Props {
  open: boolean
  onOpenChange: (open: boolean) => void
  onCreated: (key: string) => void
}

/**
 * Quests need their own create form rather than the shared `EntityFormDialog`.
 *
 * The server refuses a create without a zone, a giver, and a turn-in — the three things a quest
 * cannot be dormant without — so a key-and-name dialog would produce a 400 every time. Asking
 * for them here is also the better shape: those three are the quest, and filling them in later
 * means a spell of time when the row exists and nothing offers it.
 */
export function QuestCreateDialog({ open, onOpenChange, onCreated }: Props) {
  const { mobTemplates } = useBuilderData()

  const [zones, setZones] = useState<ZoneSummary[]>([])
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [zoneKey, setZoneKey] = useState('')
  const [giverMobKey, setGiverMobKey] = useState('')
  const [turninMobKey, setTurninMobKey] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return

    setKey('')
    setName('')
    setGiverMobKey('')
    setTurninMobKey('')
    setError(null)
    setBusy(false)

    // Every zone, not just the selected world's: the Quests tab has no world selection, and a
    // builder writing a quest line should not have to visit the World tab first to make one
    // reachable here.
    void builderApi
      .zones()
      .then((loaded) => {
        setZones(loaded)
        setZoneKey((current) => current || loaded[0]?.key || '')
      })
      .catch(() => setZones([]))
  }, [open])

  async function submit() {
    const trimmed = key.trim()

    if (!KEY_SEGMENT.test(trimmed)) {
      setError('A quest key is lowercase letters, digits, and single hyphens.')
      return
    }

    if (!zoneKey) {
      setError('Pick the zone this quest belongs to.')
      return
    }

    // Refused here as well as on the server, so the message names the field rather than arriving
    // as a generic 400 after the dialog has already closed over what caused it.
    if (!giverMobKey.trim() || !turninMobKey.trim()) {
      setError('A quest needs a giver and a turn-in. Both should be NPCs.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      await builderApi.createQuest(trimmed, {
        ...newQuest(trimmed, name.trim(), zoneKey),
        giverMobKey: giverMobKey.trim(),
        turninMobKey: turninMobKey.trim(),
      })
      onCreated(trimmed)
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that quest.')
    } finally {
      setBusy(false)
    }
  }

  const mobOptions = mobTemplates.map((t) => ({ key: t.key, name: t.name || t.key }))

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="New quest"
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <button type="button" className="primary" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Creating…' : 'Create'}
          </button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}

      <Field label="Key" hint="Permanent. Lowercase letters, digits, and hyphens.">
        <input
          value={key}
          spellCheck={false}
          placeholder="errand-for-mira"
          onChange={(e) => setKey(e.target.value)}
        />
      </Field>

      <Field label="Name">
        <input value={name} placeholder="An Errand for Mira" onChange={(e) => setName(e.target.value)} />
      </Field>

      <Field label="Zone" hint="Decides which multipliers scale the rewards.">
        <Select value={zoneKey} onChange={setZoneKey}>
          {zones.length === 0 && <option value="">— no zones —</option>}
          {zones.map((zone) => (
            <option key={zone.key} value={zone.key}>
              {zone.name} ({zone.key})
            </option>
          ))}
        </Select>
      </Field>

      <Field label="Giver" hint="Talk to this mob to be offered the quest.">
        <TemplatePicker value={giverMobKey} options={mobOptions} onChange={setGiverMobKey} />
      </Field>

      <Field label="Turn-in" hint="Often the same mob as the giver.">
        <TemplatePicker value={turninMobKey} options={mobOptions} onChange={setTurninMobKey} />
      </Field>
    </Modal>
  )
}
