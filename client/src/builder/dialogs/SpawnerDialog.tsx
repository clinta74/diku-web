import { useEffect, useState } from 'react'
import {
  builderApi,
  type ItemTemplate,
  type MobTemplate,
  type RoomDetail,
  type Spawner,
  type WanderMode,
} from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { TemplatePicker } from '../templates/TemplatePicker'
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
  const [wander, setWander] = useState<WanderMode>('template')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    setKind(editing?.templateKind ?? 'Mob')
    setTemplateKey(editing?.templateKey ?? '')
    setRoomKeys(editing?.roomKeys ?? [roomKey])
    setTargetCount(editing?.targetCount ?? 1)
    setRespawnSeconds(editing?.respawnSeconds ?? 60)
    setWander(editing?.wander ?? 'template')
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
          wander,
        })
      } else {
        await builderApi.createSpawner({
          zoneKey,
          templateKey,
          templateKind: kind,
          roomKeys,
          targetCount,
          respawnSeconds,
          wander,
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
          <Button variant="primary" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Saving…' : editing ? 'Save' : 'Add'}
          </Button>
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

      <Field
        label={kind === 'Mob' ? 'Mob template' : 'Item template'}
        hint="Type to filter. The list shows the key and the name."
      >
        <TemplatePicker value={templateKey} options={templates} onChange={setTemplateKey} />
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

      <Field label="Respawn (seconds)">
        <NumberInput min={0} value={respawnSeconds} onChange={setRespawnSeconds} />
      </Field>

      {kind === 'Mob' && (
        <Field
          label="Wandering"
          hint={
            wander === 'template'
              ? 'The mob template decides. Set it there unless this placement is the exception.'
              : 'Overrides the template for mobs from this spawner only.'
          }
        >
          <Select value={wander} onChange={(v) => setWander(v as WanderMode)}>
            <option value="template">Follow the mob template</option>
            <option value="never">Never — stays where it is put</option>
            <option value="always">Always — wanders between rooms</option>
          </Select>
        </Field>
      )}
    </Modal>
  )
}
