import { Button } from '../../ui/Button'
import type { RoomDraft } from '../../net/builderApi'
import type { DraftStatus } from './useRoomDraft'

interface Props {
  status: DraftStatus
  elapsed: number
  draft: RoomDraft | null
  warnings: string[]
  error: string | null
  onUseTitle: () => void
  onUseDescription: () => void
  onUseBoth: () => void
  onDiscard: () => void
}

/**
 * How long before the wait stops looking like a hang and starts looking like slow work.
 *
 * Generation is measured at 1.3-1.8 tokens a second, so a room is around three minutes and a
 * builder who has not been told that will assume it is broken well before then.
 */
const SLOW_AFTER = 20

function elapsedText(elapsed: number): string {
  if (elapsed < 60) return `${elapsed}s`
  return `${Math.floor(elapsed / 60)}m ${String(elapsed % 60).padStart(2, '0')}s`
}

/**
 * What the assistant came back with, offered rather than applied.
 *
 * <b>Nothing here writes to the room.</b> The buttons copy text into the edit buffer the builder
 * already owns, which leaves the existing Save and its dirty flag as the only commit point - so a
 * draft nobody likes is discarded by pressing Discard or by navigating away, and one somebody
 * likes is still saved by a person through the same PATCH as any hand-typed edit.
 *
 * Title and description are offered separately because they fail separately: the prose is often
 * worth keeping when the title is not, and being made to take both is what turns a useful
 * suggestion into a thing people stop pressing.
 */
export function DraftPanel({
  status,
  elapsed,
  draft,
  warnings,
  error,
  onUseTitle,
  onUseDescription,
  onUseBoth,
  onDiscard,
}: Props) {
  if (status === 'idle') return null

  if (status === 'working') {
    return (
      <div className="draft-panel" role="status" aria-live="polite">
        <p>Drafting… {elapsedText(elapsed)}</p>
        {elapsed >= SLOW_AFTER && (
          <p className="dim detail">
            This runs on the server's own model and takes a few minutes. You can keep editing —
            the draft will appear here when it is done.
          </p>
        )}
      </div>
    )
  }

  if (status === 'failed') {
    return (
      <div className="draft-panel bad" role="alert">
        <p>{error ?? 'The draft failed.'}</p>
        <Button onClick={onDiscard}>Dismiss</Button>
      </div>
    )
  }

  if (!draft) return null

  return (
    <div className="draft-panel" role="region" aria-label="Suggested draft">
      <p className="dim detail">
        A suggestion, not a change. Nothing is saved until you save it.
      </p>

      <Field label="Suggested title">{draft.title}</Field>
      <Field label="Suggested description">{draft.description}</Field>

      {warnings.length > 0 && (
        <div className="draft-warnings">
          {/* Said before the text is taken, not after it is saved. These are the things the
              output grammar could not prevent — two exits the same way, prose that describes a
              door — and they are easy to miss by eye precisely because the draft reads well. */}
          <p className="dim detail">Worth a look:</p>
          <ul>
            {warnings.map((warning) => (
              <li key={warning} className="bad detail">
                This draft {warning}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="row">
        <Button variant="primary" onClick={onUseBoth}>
          Use both
        </Button>
        <Button onClick={onUseTitle}>Title only</Button>
        <Button onClick={onUseDescription}>Description only</Button>
        <Button onClick={onDiscard}>Discard</Button>
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: string }) {
  return (
    <div className="draft-field">
      <span className="dim detail">{label}</span>
      <p>{children}</p>
    </div>
  )
}
