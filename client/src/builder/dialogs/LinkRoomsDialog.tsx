import { useEffect, useState } from 'react'
import { builderApi, DIRECTIONS } from '../../net/builderApi'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Select } from '../../ui/Select'

interface LinkRoomsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  fromKey: string
  toKey: string
  /** The layout's best guess from the on-screen offset - a default, not a fact. */
  guessedDirection: string
  onLinked: () => void
}

/**
 * Confirms the direction when linking two rooms by shift-click. The old canvas inferred the
 * direction from screen position, but position is produced by direction and then perturbed by
 * collision nudging - so it could silently create the wrong exit. Asking makes it correct.
 */
export function LinkRoomsDialog({
  open,
  onOpenChange,
  fromKey,
  toKey,
  guessedDirection,
  onLinked,
}: LinkRoomsDialogProps) {
  const [direction, setDirection] = useState(guessedDirection)
  const [reciprocal, setReciprocal] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open) {
      setDirection(guessedDirection)
      setReciprocal(true)
      setError(null)
      setBusy(false)
    }
  }, [open, guessedDirection])

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await builderApi.setExit(fromKey, direction, toKey, reciprocal)
      onLinked()
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not link those rooms.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Link rooms"
      description={`${fromKey} → ${toKey}`}
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <button type="button" className="primary" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Linking…' : 'Link'}
          </button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}

      <Field label="Direction" hint="Prefilled from the layout — change it if the guess is wrong.">
        <Select value={direction} onChange={setDirection}>
          {DIRECTIONS.map((d) => (
            <option key={d} value={d}>
              {d}
            </option>
          ))}
        </Select>
      </Field>

      <label className="checkbox-row">
        <input type="checkbox" checked={reciprocal} onChange={(e) => setReciprocal(e.target.checked)} />
        Also create the return exit
      </label>
    </Modal>
  )
}
