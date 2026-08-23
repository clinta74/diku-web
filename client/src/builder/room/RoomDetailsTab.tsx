import { useEffect, useState } from 'react'
import { Button } from '../../ui/Button'
import { Field } from '../../ui/Field'
import { Textarea } from '../../ui/Textarea'
import { DraftPanel } from '../assist/DraftPanel'
import { assistAvailable, useRoomDraft } from '../assist/useRoomDraft'

interface Props {
  roomKey: string
  zoneKey: string
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
  roomKey,
  zoneKey,
  title,
  description,
  dirty,
  busy,
  error,
  onTitle,
  onDescription,
  onSave,
}: Props) {
  const draft = useRoomDraft()

  // Asked once per page load and cached in the module, so navigating between rooms does not
  // re-ask. A server with no model configured answers 404 and the button simply is not there —
  // which is the same thing PLAN.md §13 asks of every part of this: the builder works without it.
  const [canAssist, setCanAssist] = useState(false)

  useEffect(() => {
    let live = true
    void assistAvailable().then((yes) => {
      if (live) setCanAssist(yes)
    })
    return () => {
      live = false
    }
  }, [])

  // The panel names which fields to take; this is the only place that knows what they mean.
  const apply = (keys: string[]) => {
    if (!draft.draft) return
    if (keys.includes('title')) onTitle(draft.draft.title)
    if (keys.includes('description')) onDescription(draft.draft.description)
    draft.discard()
  }

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
        <Button variant="primary" disabled={busy || !dirty} onClick={onSave}>
          {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
        </Button>
        {canAssist && (
          <Button
            disabled={draft.status === 'working'}
            onClick={() =>
              void draft.request({
                zoneKey,
                roomKey,
                // The buffer, not the saved row. What is in front of the builder is what they
                // want help with, and it may never have been saved.
                title,
                description,
              })
            }
          >
            {draft.status === 'working' ? 'Drafting…' : 'Suggest'}
          </Button>
        )}
        <span className="dim detail">
          Saves reach anyone standing here immediately — there is no publish step.
        </span>
      </div>

      <DraftPanel
        status={draft.status}
        warming={draft.warming}
        elapsed={draft.elapsed}
        fields={
          draft.draft
            ? [
                { key: 'title', label: 'Title', value: draft.draft.title },
                { key: 'description', label: 'Description', value: draft.draft.description },
              ]
            : []
        }
        warnings={draft.warnings}
        error={draft.error}
        onUse={apply}
        onDiscard={draft.discard}
      />
    </div>
  )
}
