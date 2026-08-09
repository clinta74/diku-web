import { useEffect, useState } from 'react'
import { builderApi, type RoomDetail } from '../../net/builderApi'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'

interface RenameRoomDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  roomKey: string
  onRenamed: (room: RoomDetail) => void
}

/**
 * Renames a room. The server rewrites every exit that pointed at it and every spawner that
 * filled it, and carries the mobs and floor items across. Replaces the inline rename row.
 */
export function RenameRoomDialog({ open, onOpenChange, roomKey, onRenamed }: RenameRoomDialogProps) {
  const [value, setValue] = useState(roomKey)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (open) {
      setValue(roomKey)
      setError(null)
      setBusy(false)
    }
  }, [open, roomKey])

  async function submit() {
    const next = value.trim()
    if (!next || next === roomKey) {
      onOpenChange(false)
      return
    }

    setBusy(true)
    setError(null)
    try {
      onRenamed(await builderApi.renameRoom(roomKey, next))
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Rename failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Rename room"
      description="Rewrites every exit and spawner that pointed here."
      footer={
        <>
          <button type="button" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </button>
          <button type="button" className="primary" onClick={() => void submit()} disabled={busy}>
            {busy ? 'Renaming…' : 'Rename'}
          </button>
        </>
      }
    >
      {error && <p className="bad">{error}</p>}
      <Field label="New room key">
        <input
          value={value}
          spellCheck={false}
          autoFocus
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') void submit()
          }}
        />
      </Field>
    </Modal>
  )
}
