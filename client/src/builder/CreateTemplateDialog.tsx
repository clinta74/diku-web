import { useState } from 'react'

interface Props {
  isOpen: boolean
  onClose: () => void
  onCreate: (key: string) => Promise<void>
}

export function CreateTemplateDialog({ isOpen, onClose, onCreate }: Props) {
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (!isOpen) return null

  function reset() {
    setKey('')
    setName('')
    setError(null)
  }

  async function handleCreate() {
    if (!key.trim()) {
      setError('Template key is required')
      return
    }

    setBusy(true)
    setError(null)

    try {
      await onCreate(key.trim())
      reset()
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create template.')
    } finally {
      setBusy(false)
    }
  }

  function handleClose() {
    reset()
    onClose()
  }

  return (
    <div className="dialog-overlay" onClick={handleClose}>
      <div className="dialog" onClick={(e) => e.stopPropagation()}>
        <h3>Create new mob template</h3>

        {error && <p className="bad">{error}</p>}

        <label>
          Template key (lowercase, e.g. "warden-mentor")
          <input
            value={key}
            onChange={(e) => setKey(e.target.value)}
            disabled={busy}
            autoFocus
            spellCheck={false}
          />
        </label>

        <label>
          Name (optional)
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={busy}
          />
        </label>

        <div className="dialog-actions">
          <button onClick={() => void handleCreate()} disabled={!key.trim() || busy} className="good">
            Create
          </button>
          <button onClick={handleClose} disabled={busy}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  )
}
