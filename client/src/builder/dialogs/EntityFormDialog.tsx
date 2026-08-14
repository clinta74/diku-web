import { useEffect, useState } from 'react'
import { Button } from '../../ui/Button'
import { Modal } from '../../ui/Modal'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'

/** Mirrors the server's IsKeySegment: lowercase, digits, internal hyphens, no leading/trailing. */
const KEY_SEGMENT = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

export interface EntityFormValues {
  key: string
  name: string
  description: string
}

interface EntityFormDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  keyLabel: string
  keyPlaceholder?: string
  /** Shown, dimmed, before the key input - e.g. the parent path a room slug hangs off. */
  keyPrefix?: string
  withName?: boolean
  withDescription?: boolean
  /** Resolves on success (dialog then closes) or rejects with a message to show inline. */
  onSubmit: (values: EntityFormValues) => Promise<void>
}

/**
 * The create form behind most "+ new" actions: a validated key segment plus optional name and
 * description. Replaces the three native `prompt()` calls and the old, mistitled template
 * dialog with one portalled, focus-trapped flow.
 */
export function EntityFormDialog({
  open,
  onOpenChange,
  title,
  keyLabel,
  keyPlaceholder,
  keyPrefix,
  withName = true,
  withDescription = false,
  onSubmit,
}: EntityFormDialogProps) {
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  // Reset every time it opens, so a cancelled attempt never bleeds into the next.
  useEffect(() => {
    if (open) {
      setKey('')
      setName('')
      setDescription('')
      setError(null)
      setBusy(false)
    }
  }, [open])

  async function submit() {
    const trimmed = key.trim().toLowerCase()
    if (!KEY_SEGMENT.test(trimmed)) {
      setError('Use lowercase letters, digits, and hyphens - e.g. "north-gate".')
      return
    }

    setBusy(true)
    setError(null)
    try {
      await onSubmit({ key: trimmed, name: name.trim(), description: description.trim() })
      onOpenChange(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={title}
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

      <Field label={keyLabel}>
        <div className="prefixed-input">
          {keyPrefix && <code className="dim">{keyPrefix}</code>}
          <input
            value={key}
            placeholder={keyPlaceholder}
            spellCheck={false}
            autoFocus
            onChange={(e) => setKey(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') void submit()
            }}
          />
        </div>
      </Field>

      {withName && (
        <Field label="Name (optional)">
          <input value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
      )}

      {withDescription && (
        <Field label="Description (optional)">
          <Textarea rows={3} value={description} onChange={setDescription} />
        </Field>
      )}
    </Modal>
  )
}
