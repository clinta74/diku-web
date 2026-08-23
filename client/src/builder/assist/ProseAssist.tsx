import { useEffect, useState } from 'react'
import { Button } from '../../ui/Button'
import type { ProseKind } from '../../net/builderApi'
import { DraftPanel, type DraftField } from './DraftPanel'
import { assistAvailable, useRoomDraft } from './useRoomDraft'

interface Props {
  kind: ProseKind
  /** The template or quest key. The entity has to exist: its numbers are the context. */
  entityKey: string
  name: string
  description: string
  /** Quests only. Omit for mobs and items. */
  summary?: string
  onName: (value: string) => void
  onDescription: (value: string) => void
  onSummary?: (value: string) => void
}

/**
 * A Suggest button and its draft panel, for the three kinds that are prose and nothing else.
 *
 * <b>One component for mob, item and quest</b> because the job is the same for all three: the
 * numbers are already decided, and what is wanted is words that match them. The kind only changes
 * which brief the model is given (server-side, in `AssistSchema.ForProse`) and whether there is a
 * summary — so three copies of this would be three places for the wait, the warnings and the
 * refusal-to-apply to drift apart.
 *
 * Nothing here writes to the world. Taking a suggestion copies text into the edit buffer the
 * editor already owns, and its existing Save stays the only commit point.
 */
export function ProseAssist({
  kind,
  entityKey,
  name,
  description,
  summary,
  onName,
  onDescription,
  onSummary,
}: Props) {
  const draft = useRoomDraft()
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

  // A key is required because the assist describes something that exists rather than inventing it;
  // an unsaved new template has nothing for the model to be accurate about yet.
  if (!canAssist || !entityKey) return null

  const fields: DraftField[] = draft.prose
    ? [
        { key: 'name', label: 'Name', value: draft.prose.name },
        ...(draft.prose.summary
          ? [{ key: 'summary', label: 'Summary', value: draft.prose.summary }]
          : []),
        { key: 'description', label: 'Description', value: draft.prose.description },
      ]
    : []

  const apply = (keys: string[]) => {
    if (!draft.prose) return
    if (keys.includes('name')) onName(draft.prose.name)
    if (keys.includes('description')) onDescription(draft.prose.description)
    if (keys.includes('summary') && draft.prose.summary && onSummary) {
      onSummary(draft.prose.summary)
    }
    draft.discard()
  }

  return (
    <>
      <div className="row">
        <Button
          disabled={draft.status === 'working'}
          onClick={() =>
            void draft.requestProse({
              kind,
              key: entityKey,
              // The buffers, not the saved row. Somebody reaches for this when they have half a
              // sentence they are unhappy with, and that text may never have been saved.
              name,
              description,
              summary,
            })
          }
        >
          {draft.status === 'working' ? 'Drafting…' : 'Suggest wording'}
        </Button>
      </div>

      <DraftPanel
        status={draft.status}
        warming={draft.warming}
        elapsed={draft.elapsed}
        fields={fields}
        warnings={draft.warnings}
        error={draft.error}
        onUse={apply}
        onDiscard={draft.discard}
      />
    </>
  )
}
