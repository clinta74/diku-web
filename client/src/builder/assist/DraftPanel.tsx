import { Button } from '../../ui/Button'
import type { DraftStatus } from './useRoomDraft'

/** One thing the draft is offering, and the buffer it would go into. */
export interface DraftField {
  key: string
  label: string
  value: string
}

interface Props {
  status: DraftStatus
  /** True while the model is still caching the world canon — a much longer wait than a draft. */
  warming?: boolean
  elapsed: number
  fields: DraftField[]
  warnings: string[]
  error: string | null
  onUse: (keys: string[]) => void
  onDiscard: () => void
}

/**
 * How long before the wait stops looking like a hang and starts looking like slow work.
 *
 * Generation is measured at 1.3-1.8 tokens a second, so a description is around three minutes and
 * a builder who has not been told that will assume it is broken well before then.
 */
const SLOW_AFTER = 20

function elapsedText(elapsed: number): string {
  if (elapsed < 60) return `${elapsed}s`
  return `${Math.floor(elapsed / 60)}m ${String(elapsed % 60).padStart(2, '0')}s`
}

/**
 * What the assistant came back with, offered rather than applied.
 *
 * <b>Nothing here writes to the world.</b> The buttons copy text into the edit buffer the builder
 * already owns, which leaves the existing Save and its dirty flag as the only commit point — so a
 * draft nobody likes is discarded by pressing Discard or by navigating away, and one somebody
 * likes is still saved by a person through the same PATCH as any hand-typed edit.
 *
 * <b>Fields are taken one at a time as well as together</b>, because they fail separately: the
 * prose is often worth keeping when the name is not, and being made to take everything is what
 * turns a useful suggestion into a thing people stop pressing.
 *
 * Generic over the fields rather than knowing about rooms, so the mob, item and quest editors get
 * the same panel — and the same wait, the same warnings and the same refusal to apply anything —
 * without a second copy of it drifting away from this one.
 */
export function DraftPanel({
  status,
  warming = false,
  elapsed,
  fields,
  warnings,
  error,
  onUse,
  onDiscard,
}: Props) {
  if (status === 'idle') return null

  if (status === 'working') {
    return (
      <div className="draft-panel" role="status" aria-live="polite">
        <p>
          {warming ? 'Waiting for the model to warm up' : 'Drafting'}… {elapsedText(elapsed)}
        </p>
        {warming ? (
          // Said as soon as it is known rather than after SLOW_AFTER, because this wait is a
          // different order of magnitude - the model reads the whole world once after a restart,
          // and on modest hardware that is tens of minutes rather than the three a draft takes.
          // Somebody watching a counter climb past what they expected deserves to know why.
          <p className="dim detail">
            The server reads the whole world once after a restart, which takes a while. Your draft
            is queued and will start on its own — nothing is lost by leaving this.
          </p>
        ) : (
          elapsed >= SLOW_AFTER && (
            <p className="dim detail">
              This runs on the server's own model and takes a few minutes. You can keep editing —
              the draft will appear here when it is done.
            </p>
          )
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

  if (fields.length === 0) return null

  return (
    <div className="draft-panel" role="region" aria-label="Suggested draft">
      <p className="dim detail">A suggestion, not a change. Nothing is saved until you save it.</p>

      {fields.map((field) => (
        <div className="draft-field" key={field.key}>
          <span className="dim detail">Suggested {field.label.toLowerCase()}</span>
          <p>{field.value}</p>
        </div>
      ))}

      {warnings.length > 0 && (
        <div className="draft-warnings">
          {/* Said before the text is taken, not after it is saved. These are the things the
              output grammar could not prevent — two exits the same way, a content key in the
              prose — and they are easy to miss by eye precisely because the draft reads well. */}
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
        <Button variant="primary" onClick={() => onUse(fields.map((f) => f.key))}>
          Use all
        </Button>
        {fields.map((field) => (
          <Button key={field.key} onClick={() => onUse([field.key])}>
            {field.label} only
          </Button>
        ))}
        <Button onClick={onDiscard}>Discard</Button>
      </div>
    </div>
  )
}
