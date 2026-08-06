import { useEffect, useState } from 'react'
import {
  builderApi,
  DIRECTIONS,
  type RoomDetail,
  type RoomFlagDefinition,
} from '../net/builderApi'
import { GridPainter } from './GridPainter'
import { SpawnerManager } from './SpawnerManager'

interface Props {
  roomKey: string
  flagDefinitions: RoomFlagDefinition[]
  onChanged: (room: RoomDetail) => void
  onDeleted: (roomKey: string) => void
  onNavigate: (roomKey: string) => void
}

export function RoomEditor({
  roomKey,
  flagDefinitions,
  onChanged,
  onDeleted,
  onNavigate,
}: Props) {
  const [room, setRoom] = useState<RoomDetail | null>(null)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [grid, setGrid] = useState<string[]>([])
  const [legend, setLegend] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    let cancelled = false

    setRoom(null)
    setError(null)

    void builderApi
      .room(roomKey)
      .then((loaded) => {
        if (cancelled) return
        apply(loaded)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load that room.')
      })

    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [roomKey])

  function apply(loaded: RoomDetail) {
    setRoom(loaded)
    setTitle(loaded.title)
    setDescription(loaded.description)
    setGrid(loaded.grid)
    setLegend(loaded.legend)
    setDirty(false)
  }

  async function save(patch: Record<string, unknown>) {
    setBusy(true)
    setError(null)

    try {
      const updated = await builderApi.updateRoom(roomKey, patch)
      apply(updated)
      onChanged(updated)
      return updated
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      return null
    } finally {
      setBusy(false)
    }
  }

  if (error && !room) return <p className="bad">{error}</p>
  if (!room) return <p className="dim">Loading…</p>

  return (
    <div className="room-editor">
      <header>
        <h2>{room.title}</h2>
        <code className="room-key">{room.key}</code>
      </header>

      {error && <p className="bad">{error}</p>}

      <label>
        Title
        <input
          value={title}
          onChange={(e) => {
            setTitle(e.target.value)
            setDirty(true)
          }}
        />
      </label>

      <label>
        Description
        <textarea
          rows={6}
          value={description}
          onChange={(e) => {
            setDescription(e.target.value)
            setDirty(true)
          }}
        />
      </label>

      <div className="row">
        <button
          type="button"
          className="primary"
          disabled={busy || !dirty}
          onClick={() => void save({ title, description })}
        >
          {busy ? 'Saving…' : dirty ? 'Save' : 'Saved'}
        </button>
        <span className="dim detail">
          Saves reach anyone standing here immediately — there is no publish step.
        </span>
      </div>

      <FlagEditor
        room={room}
        definitions={flagDefinitions}
        onToggle={(key, value) => {
          const next = { ...room.flags }
          if (value === null) delete next[key]
          else next[key] = value
          void save({ flags: next })
        }}
      />

      <section className="editor-section">
        <h3>Terrain</h3>
        <GridPainter
          grid={grid}
          legend={legend}
          onChange={(nextGrid, nextLegend) => {
            setGrid(nextGrid)
            setLegend(nextLegend)
            void save({ grid: nextGrid, legend: nextLegend })
          }}
        />
      </section>

      <ExitEditor room={room} onChanged={apply} onNavigate={onNavigate} onError={setError} />

      <section className="editor-section">
        <SpawnerManager
          roomKey={room.key}
          zoneKey={room.zoneKey}
          onChanged={() => {/* Room doesn't need refresh for spawner changes */}}
        />
      </section>

      <section className="editor-section danger">
        <h3>Room</h3>
        <RenameControl roomKey={room.key} onRenamed={(next) => onNavigate(next.key)} />
        <button
          type="button"
          className="danger-button"
          onClick={() => {
            // Occupants are moved to a sibling room rather than orphaned (PLAN.md §7.4), so
            // this is recoverable-ish, but the exits pointing here will dangle.
            if (!confirm(`Delete ${room.key}? Exits pointing at it will be left dangling.`)) return
            void builderApi
              .deleteRoom(room.key)
              .then(() => onDeleted(room.key))
              .catch((e) => setError(e instanceof Error ? e.message : 'Delete failed.'))
          }}
        >
          Delete room
        </button>
      </section>
    </div>
  )
}

/**
 * Flags render from the registry the server serves, so a newly registered flag appears here
 * with no client change at all (PLAN.md §4.10).
 *
 * The three-state control matters: "off" is a decision about this room, while "inherit" removes
 * the key so the zone or world decides. A two-state checkbox would make those indistinguishable.
 */
function FlagEditor({
  room,
  definitions,
  onToggle,
}: {
  room: RoomDetail
  definitions: RoomFlagDefinition[]
  onToggle: (key: string, value: boolean | null) => void
}) {
  return (
    <section className="editor-section">
      <h3>Flags</h3>
      <ul className="flag-list">
        {definitions.map((definition) => {
          const resolved = room.resolved.find((r) => r.key === definition.key)
          const own = room.flags[definition.key]
          const declared = own !== undefined
          const value = resolved?.value ?? definition.default

          return (
            <li key={definition.key} className={declared ? 'flag' : 'flag inherited'}>
              <div className="flag-head">
                <strong>{definition.key}</strong>
                <span className={value ? 'good' : 'dim'}>{value ? 'on' : 'off'}</span>
                {!declared && resolved && resolved.source !== 'default' && (
                  <span className="dim"> · from {resolved.source}</span>
                )}
              </div>

              <p className="dim detail">{definition.summary}</p>

              <div className="flag-controls">
                <button
                  type="button"
                  className={declared && own ? 'selected' : ''}
                  onClick={() => onToggle(definition.key, true)}
                >
                  on
                </button>
                <button
                  type="button"
                  className={declared && !own ? 'selected' : ''}
                  onClick={() => onToggle(definition.key, false)}
                >
                  off
                </button>
                <button
                  type="button"
                  className={!declared ? 'selected' : ''}
                  onClick={() => onToggle(definition.key, null)}
                  title="Remove the key so the zone or world decides"
                >
                  inherit
                </button>
              </div>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

function ExitEditor({
  room,
  onChanged,
  onNavigate,
  onError,
}: {
  room: RoomDetail
  onChanged: (room: RoomDetail) => void
  onNavigate: (roomKey: string) => void
  onError: (message: string) => void
}) {
  const [target, setTarget] = useState('')
  const [direction, setDirection] = useState<string>(DIRECTIONS[0])

  function run(work: Promise<RoomDetail>) {
    void work
      .then(onChanged)
      .catch((e) => onError(e instanceof Error ? e.message : 'That did not work.'))
  }

  return (
    <section className="editor-section">
      <h3>Exits</h3>

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
              // A dangling link is legal, not an error - the target may simply not be built
              // yet (PLAN.md §7.4). Offering the dig is the fastest way to resolve it.
              <>
                <span className="bad">{exit.to} — not built</span>
                <button
                  type="button"
                  onClick={() => run(builderApi.dig(room.key, exit.direction))}
                >
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

      <div className="row">
        <select value={direction} onChange={(e) => setDirection(e.target.value)}>
          {DIRECTIONS.map((d) => (
            <option key={d} value={d}>
              {d}
            </option>
          ))}
        </select>

        <input
          value={target}
          placeholder="world.zone.room"
          spellCheck={false}
          onChange={(e) => setTarget(e.target.value)}
        />

        <button
          type="button"
          disabled={!target.trim()}
          onClick={() => run(builderApi.setExit(room.key, direction, target.trim()))}
        >
          link
        </button>

        <button type="button" onClick={() => run(builderApi.dig(room.key, direction))}>
          dig
        </button>
      </div>

      <p className="dim detail">
        Linking to a room that does not exist yet is allowed. Dig creates it — or fills in a
        dangling exit using the key that exit already names.
      </p>
    </section>
  )
}

function RenameControl({
  roomKey,
  onRenamed,
}: {
  roomKey: string
  onRenamed: (room: RoomDetail) => void
}) {
  const [value, setValue] = useState(roomKey)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => setValue(roomKey), [roomKey])

  return (
    <div className="row">
      <input value={value} spellCheck={false} onChange={(e) => setValue(e.target.value)} />
      <button
        type="button"
        disabled={value === roomKey}
        onClick={() =>
          void builderApi
            .renameRoom(roomKey, value.trim())
            .then(onRenamed)
            .catch((e) => setError(e instanceof Error ? e.message : 'Rename failed.'))
        }
      >
        rename
      </button>
      {error && <span className="bad">{error}</span>}
      <span className="dim detail">Rewrites every exit that pointed here.</span>
    </div>
  )
}
