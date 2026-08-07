import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'

interface Props {
  title: string
  description: string
  dirty: boolean
  busy: boolean
  error: string | null
  onTitle: (value: string) => void
  onDescription: (value: string) => void
  onSave: () => void
}

/**
 * Title and description. The one section with an explicit Save: free prose needs a commit
 * point, unlike the toggles elsewhere. The edit buffer is owned by RoomEditor so it survives
 * a hop to another sub-tab and can be guarded against loss on navigation.
 */
export function RoomDetailsTab({
  title,
  description,
  dirty,
  busy,
  error,
  onTitle,
  onDescription,
  onSave,
}: Props) {
  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}

      <Field label="Title">
        <input value={title} onChange={(e) => onTitle(e.target.value)} />
      </Field>

      <Field label="Description">
        <Textarea rows={6} value={description} onChange={onDescription} />
      </Field>

      <div className="row">
        <button type="button" className="primary" disabled={busy || !dirty} onClick={onSave}>
          {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
        </button>
        <span className="dim detail">
          Saves reach anyone standing here immediately — there is no publish step.
        </span>
      </div>
    </div>
  )
}
