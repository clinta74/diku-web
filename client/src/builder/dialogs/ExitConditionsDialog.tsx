import { useEffect, useState } from 'react'
import { builderApi, type RoomDetail, type RoomExit } from '../../net/builderApi'
import { Button } from '../../ui/Button'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'

interface ExitConditionsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  roomKey: string
  /** The exit being gated. Null while the dialog is closed. */
  exit: RoomExit | null
  onChanged: (room: RoomDetail) => void
}

/**
 * What it takes to walk this way (PLAN.md §4.15). A flag, an item, or both - the same mechanism
 * behind a cellar door and a gate between Reaches.
 *
 * Clearing a field removes that requirement, because the save states the whole exit rather than
 * patching it. That is the only way a lock can ever come off, and it is why this is a form with
 * a Save rather than a row of toggles.
 */
export function ExitConditionsDialog({
  open,
  onOpenChange,
  roomKey,
  exit,
  onChanged,
}: ExitConditionsDialogProps) {
  const [flagKey, setFlagKey] = useState('')
  const [itemKey, setItemKey] = useState('')
  const [refusal, setRefusal] = useState('')
  const [mirror, setMirror] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open && exit) {
      setFlagKey(exit.requiredFlagKey ?? '')
      setItemKey(exit.requiredItemKey ?? '')
      setRefusal(exit.refusalMessage ?? '')
      setMirror(false)
      setError(null)
      setBusy(false)
    }
  }, [open, exit])

  async function save() {
    if (!exit) return

    setBusy(true)
    setError(null)
    try {
      const blank = (value: string) => (value.trim() === '' ? null : value.trim())

      onChanged(
        await builderApi.setExit(roomKey, exit.direction, exit.to, mirror, {
          requiredFlagKey: blank(flagKey),
          requiredItemKey: blank(itemKey),
          refusalMessage: blank(refusal),
          reciprocalConditions: mirror,
        }),
      )
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
      title={exit ? `Gate the ${exit.direction} exit` : 'Gate exit'}
      description="Leave a field empty for no requirement. Both must hold when both are set."
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <Button variant="primary" onClick={() => void save()} disabled={busy}>
            Save
          </Button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}

      <Field
        label="Required character flag"
        hint="Earned and never lost — what attunement to a realm is. Granted by a quest reward."
      >
        <input
          value={flagKey}
          placeholder="attuned.grask"
          spellCheck={false}
          onChange={(e) => setFlagKey(e.target.value)}
        />
      </Field>

      <Field
        label="Required item"
        hint="Carried or worn, and never consumed — what a key is. Can be dropped or stolen."
      >
        <input
          value={itemKey}
          placeholder="brass-key"
          spellCheck={false}
          onChange={(e) => setItemKey(e.target.value)}
        />
      </Field>

      <Field label="Refusal message" hint="What someone turned away is told. Empty for a generic line.">
        <input
          value={refusal}
          placeholder="The gate does not know you."
          onChange={(e) => setRefusal(e.target.value)}
        />
      </Field>

      <label className="checkbox-row">
        <input type="checkbox" checked={mirror} onChange={(e) => setMirror(e.target.checked)} />
        Gate the return exit the same way
      </label>
      <p className="dim">
        Off by default: you can always leave a vault. Turning it on creates the return exit if it
        does not exist.
      </p>
    </Modal>
  )
}
