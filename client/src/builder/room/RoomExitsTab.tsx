import { useState } from 'react'
import { builderApi, type RoomDetail } from '../../net/builderApi'
import { AddExitDialog } from '../dialogs/AddExitDialog'

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
              <button type="button" className="link" onClick={() => onNavigate(exit.to)}>
                {exit.to}
              </button>
            ) : (
              // A dangling link is legal, not an error - the target may just not be built yet.
              <>
                <span className="bad">{exit.to} — not built</span>
                <button type="button" onClick={() => run(builderApi.dig(room.key, exit.direction))}>
                  materialize
                </button>
              </>
            )}
            <button
              type="button"
              className="link"
              onClick={() => run(builderApi.removeExit(room.key, exit.direction))}
            >
              unlink
            </button>
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
    </div>
  )
}
