import { useEffect, useState } from 'react'
import { builderApi, DIRECTIONS, type RoomDetail } from '../../net/builderApi'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'

interface AddExitDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  roomKey: string
  /** Directions already used, so we don't offer them again. */
  taken: string[]
  onChanged: (room: RoomDetail) => void
}

/**
 * Adds an exit from a room: pick a direction, then either link to an existing room key or dig
 * a fresh one that way. Replaces the inline select+input+two-buttons row in the old editor.
 */
export function AddExitDialog({ open, onOpenChange, roomKey, taken, onChanged }: AddExitDialogProps) {
  const available = DIRECTIONS.filter((d) => !taken.includes(d))
  const [direction, setDirection] = useState<string>(available[0] ?? DIRECTIONS[0])
  const [target, setTarget] = useState('')
  const [reciprocal, setReciprocal] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open) {
      setDirection(available[0] ?? DIRECTIONS[0])
      setTarget('')
      setReciprocal(true)
      setError(null)
      setBusy(false)
    }
    // available is derived from props; recomputing on open is intentional.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  async function run(work: Promise<RoomDetail>) {
    setBusy(true)
    setError(null)
    try {
      onChanged(await work)
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That did not work.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Add exit"
      description="Link to a room that already exists, or dig a new one in that direction."
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <button
            type="button"
            onClick={() => void run(builderApi.dig(roomKey, direction, { reciprocal }))}
            disabled={busy}
          >
            Dig new
          </button>
          <button
            type="button"
            className="primary"
            onClick={() => void run(builderApi.setExit(roomKey, direction, target.trim(), reciprocal))}
            disabled={busy || !target.trim()}
          >
            Link
          </button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}

      <Field label="Direction">
        <Select value={direction} onChange={setDirection}>
          {DIRECTIONS.map((d) => (
            <option key={d} value={d} disabled={taken.includes(d)}>
              {d}
              {taken.includes(d) ? ' (in use)' : ''}
            </option>
          ))}
        </Select>
      </Field>

      <Field label="Target room key" hint="Linking to a room that does not exist yet is allowed.">
        <input
          value={target}
          placeholder="world.zone.room"
          spellCheck={false}
          onChange={(e) => setTarget(e.target.value)}
        />
      </Field>

      <label className="checkbox-row">
        <input type="checkbox" checked={reciprocal} onChange={(e) => setReciprocal(e.target.checked)} />
        Also create the return exit
      </label>
    </Modal>
  )
}
