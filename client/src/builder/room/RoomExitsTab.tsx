import { useState } from 'react'
import { Button } from '../../ui/Button'
import { builderApi, type RoomDetail, type RoomExit } from '../../net/builderApi'
import { AddExitDialog } from '../dialogs/AddExitDialog'
import { ExitConditionsDialog } from '../dialogs/ExitConditionsDialog'

interface Props {
  room: RoomDetail
  onChanged: (room: RoomDetail) => void
  onNavigate: (roomKey: string) => void
}

/**
 * The room's exits. Adding one is a dialog; the per-row actions (materialize a dangling exit,
 * unlink) stay inline because they are single clicks on an existing row, not forms.
 */
export function RoomExitsTab({ room, onChanged, onNavigate }: Props) {
  const [error, setError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [gating, setGating] = useState<RoomExit | null>(null)

  function run(work: Promise<RoomDetail>) {
    void work
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'That did not work.'))
  }

  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}

      <ul className="exit-list">
        {room.exits.length === 0 && <li className="dim">No exits.</li>}
        {room.exits.map((exit) => (
          <li key={exit.direction}>
            <strong>{exit.direction}</strong>
            {exit.targetExists ? (
              <Button variant="link" onClick={() => onNavigate(exit.to)}>
                {exit.to}
              </Button>
            ) : (
              // A dangling link is legal, not an error - the target may just not be built yet.
              <>
                <span className="bad">{exit.to} — not built</span>
                <button type="button" onClick={() => run(builderApi.dig(room.key, exit.direction))}>
                  materialize
                </button>
              </>
            )}
            {/*
              A gated exit says so on the row rather than only inside the dialog. Who may pass is
              as much a property of the exit as where it goes, and a lock nobody can see from the
              list is one a builder forgets they set (PLAN.md §4.15).
            */}
            {(exit.requiredFlagKey || exit.requiredItemKey) && (
              <span className="dim" title={gateSummary(exit)}>
                gated
              </span>
            )}
            <Button variant="link" onClick={() => setGating(exit)}>
              {exit.requiredFlagKey || exit.requiredItemKey ? 'gate…' : 'gate'}
            </Button>
            <Button

              variant="link"
              onClick={() => run(builderApi.removeExit(room.key, exit.direction))}
            >
              unlink
            </Button>
          </li>
        ))}
      </ul>

      <button type="button" onClick={() => setAdding(true)}>
        + Add exit
      </button>

      <AddExitDialog
        open={adding}
        onOpenChange={setAdding}
        roomKey={room.key}
        taken={room.exits.map((e) => e.direction)}
        onChanged={onChanged}
      />

      <ExitConditionsDialog
        open={gating !== null}
        onOpenChange={(open) => !open && setGating(null)}
        roomKey={room.key}
        exit={gating}
        onChanged={onChanged}
      />
    </div>
  )
}

/** The tooltip on a gated row: what it takes to pass, in the order the engine asks. */
function gateSummary(exit: RoomExit): string {
  const needs = [
    exit.requiredFlagKey && `the flag '${exit.requiredFlagKey}'`,
    exit.requiredItemKey && `the item '${exit.requiredItemKey}'`,
  ].filter(Boolean)

  return `Needs ${needs.join(' and ')}.`
}
