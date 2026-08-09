import { useEffect, useState } from 'react'
import {
  builderApi,
  type ItemTemplate,
  type MobTemplate,
  type RoomDetail,
  type Spawner,
} from '../../net/builderApi'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { NumberInput } from '../../ui/NumberInput'

type Kind = 'Mob' | 'Item'

interface SpawnerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  zoneKey: string
  /** The room this dialog was opened from. A new spawner starts covering just this one. */
  roomKey: string
  mobTemplates: MobTemplate[]
  itemTemplates: ItemTemplate[]
  /** Every room in the zone, so a spawner can cover more than the one it was opened from. */
  zoneRooms: RoomDetail[]
  /** The spawner being edited, or null to add a new one. */
  editing: Spawner | null
  onSaved: () => void
}

/**
 * One dialog for both adding and editing a spawner.
 *
 * It used to hardcode `templateKind: 'Mob'` and was only ever handed `mobTemplates`, so **item
 * spawners had no authoring path at all** — half the spawning system was unreachable from the
 * browser. Create also hardcoded `roomKeys: [roomKey]` and update omitted `roomKeys` entirely,
 * so a spawner's room set could never be changed after it was made.
 */
export function SpawnerDialog({
  open,
  onOpenChange,
  zoneKey,
  roomKey,
  mobTemplates,
  itemTemplates,
  zoneRooms,
  editing,
  onSaved,
}: SpawnerDialogProps) {
  const [kind, setKind] = useState<Kind>('Mob')
  const [templateKey, setTemplateKey] = useState('')
  const [roomKeys, setRoomKeys] = useState<string[]>([])
  const [targetCount, setTargetCount] = useState(1)
  const [respawnSeconds, setRespawnSeconds] = useState(60)
  const [sentinel, setSentinel] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    setKind(editing?.templateKind ?? 'Mob')
    setTemplateKey(editing?.templateKey ?? '')
    setRoomKeys(editing?.roomKeys ?? [roomKey])
    setTargetCount(editing?.targetCount ?? 1)
    setRespawnSeconds(editing?.respawnSeconds ?? 60)
    setSentinel(editing?.sentinel ?? false)
    setError(null)
    setBusy(false)
  }, [open, editing, roomKey])

  const templates: Array<{ key: string; name: string }> =
    kind === 'Mob'
      ? mobTemplates.map((t) => ({ key: t.key, name: t.name }))
      : itemTemplates.map((t) => ({ key: t.key, name: t.name }))

  function toggleRoom(key: string) {
    setRoomKeys((current) =>
      current.includes(key) ? current.filter((k) => k !== key) : [...current, key],
    )
  }

  async function submit() {
    if (!templateKey) {
      setError(`Choose ${kind === 'Mob' ? 'a mob' : 'an item'} template.`)
      return
    }

    if (roomKeys.length === 0) {
      // A spawner covering no rooms would sweep forever and populate nothing.
      setError('Pick at least one room for this spawner to fill.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      if (editing) {
        await builderApi.updateSpawner(editing.id, {
          templateKey,
          templateKind: kind,
          roomKeys,
          targetCount,
          respawnSeconds,
          sentinel,
        })
      } else {
        await builderApi.createSpawner({
          zoneKey,
          templateKey,
          templateKind: kind,
          roomKeys,
          targetCount,
          respawnSeconds,
          sentinel,
        })
      }
      onSaved()
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the spawner.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={editing ? 'Edit spawner' : 'Add spawner'}
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <button type="button" className="primary" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saving…' : editing ? 'Save' : 'Add'}
          </button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}

      <Field label="Spawns">
        <Select
          value={kind}
          onChange={(v) => {
            // The template lists do not overlap, so a key from the other kind would dangle.
            setKind(v as Kind)
            setTemplateKey('')
          }}
        >
          <option value="Mob">Mobs</option>
          <option value="Item">Items on the ground</option>
        </Select>
      </Field>

      <Field label={kind === 'Mob' ? 'Mob template' : 'Item template'}>
        <Select value={templateKey} onChange={setTemplateKey}>
          <option value="">— select {kind === 'Mob' ? 'mob' : 'item'} —</option>
          {templates.map((template) => (
            <option key={template.key} value={template.key}>
              {template.name || template.key}
            </option>
          ))}
        </Select>
      </Field>

      <Field
        label="Rooms"
        hint="The target count is shared across every room ticked, not applied to each."
      >
        <ul className="room-picker">
          {zoneRooms.map((room) => (
            <li key={room.key}>
              <label className="field-check">
                <input
                  type="checkbox"
                  checked={roomKeys.includes(room.key)}
                  onChange={() => toggleRoom(room.key)}
                />
                {room.title || room.key}
              </label>
            </li>
          ))}
        </ul>
      </Field>

      <Field label="Target count">
        <NumberInput min={1} value={targetCount} onChange={setTargetCount} />
      </Field>

      <Field label="Respawn seconds">
        <NumberInput min={0} value={respawnSeconds} onChange={setRespawnSeconds} />
      </Field>

      {kind === 'Mob' && (
        <label className="field-check">
          <input
            type="checkbox"
            checked={sentinel}
            onChange={(e) => setSentinel(e.target.checked)}
          />
          Sentinel (mobs don’t wander) — set this for shopkeepers and quest givers
        </label>
      )}
    </Modal>
  )
}
