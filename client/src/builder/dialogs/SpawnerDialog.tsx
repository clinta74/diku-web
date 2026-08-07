import { useEffect, useState } from 'react'
import { builderApi, type MobTemplate, type Spawner } from '../../net/builderApi'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'
import { NumberInput } from '../../ui/NumberInput'

interface SpawnerDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  zoneKey: string
  roomKey: string
  mobTemplates: MobTemplate[]
  /** The spawner being edited, or null to add a new one. */
  editing: Spawner | null
  onSaved: () => void
}

/**
 * One dialog for both adding and editing a spawner, replacing the old pair of near-identical
 * inline forms that shared a single set of state as a manual reset. Prefilled from `editing`
 * when present.
 */
export function SpawnerDialog({
  open,
  onOpenChange,
  zoneKey,
  roomKey,
  mobTemplates,
  editing,
  onSaved,
}: SpawnerDialogProps) {
  const [templateKey, setTemplateKey] = useState('')
  const [targetCount, setTargetCount] = useState(1)
  const [respawnSeconds, setRespawnSeconds] = useState(60)
  const [sentinel, setSentinel] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!open) return
    setTemplateKey(editing?.templateKey ?? '')
    setTargetCount(editing?.targetCount ?? 1)
    setRespawnSeconds(editing?.respawnSeconds ?? 60)
    setSentinel(editing?.sentinel ?? false)
    setError(null)
    setBusy(false)
  }, [open, editing])

  async function submit() {
    if (!templateKey) {
      setError('Choose a mob template.')
      return
    }

    setBusy(true)
    setError(null)
    try {
      if (editing) {
        await builderApi.updateSpawner(editing.id, {
          templateKey,
          templateKind: 'Mob',
          targetCount,
          respawnSeconds,
          sentinel,
        })
      } else {
        await builderApi.createSpawner({
          zoneKey,
          templateKey,
          templateKind: 'Mob',
          roomKeys: [roomKey],
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

      <Field label="Mob template">
        <Select value={templateKey} onChange={setTemplateKey}>
          <option value="">— select mob —</option>
          {mobTemplates.map((template) => (
            <option key={template.key} value={template.key}>
              {template.name || template.key}
            </option>
          ))}
        </Select>
      </Field>

      <Field label="Target count">
        <NumberInput min={1} value={targetCount} onChange={setTargetCount} />
      </Field>

      <Field label="Respawn seconds">
        <NumberInput min={0} value={respawnSeconds} onChange={setRespawnSeconds} />
      </Field>

      <label className="checkbox-row">
        <input type="checkbox" checked={sentinel} onChange={(e) => setSentinel(e.target.checked)} />
        Sentinel (mobs don't wander)
      </label>
    </Modal>
  )
}
