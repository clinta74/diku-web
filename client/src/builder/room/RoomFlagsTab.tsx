import { useState } from 'react'
import { builderApi, type RoomDetail } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'

interface Props {
  room: RoomDetail
  onChanged: (room: RoomDetail) => void
}

/**
 * The three-state flag control. "off" is a decision about this room; "inherit" removes the key
 * so the zone or world decides - a two-state checkbox could not tell those apart.
 *
 * Each toggle is a single-flag PUT, not a whole-map write, so two builders in one zone stop
 * erasing each other (PLAN §1). Flags render from the server registry, so a newly registered
 * flag appears here with no client change.
 */
export function RoomFlagsTab({ room, onChanged }: Props) {
  const { flagDefinitions } = useBuilderData()
  const [error, setError] = useState<string | null>(null)

  function set(key: string, value: boolean | null) {
    void builderApi
      .setRoomFlag(room.key, key, value)
      .then(onChanged)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not set that flag.'))
  }

  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}

      <ul className="flag-list">
        {flagDefinitions.map((definition) => {
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
                  onClick={() => set(definition.key, true)}
                >
                  on
                </button>
                <button
                  type="button"
                  className={declared && !own ? 'selected' : ''}
                  onClick={() => set(definition.key, false)}
                >
                  off
                </button>
                <button
                  type="button"
                  className={!declared ? 'selected' : ''}
                  onClick={() => set(definition.key, null)}
                  title="Remove the key so the zone or world decides"
                >
                  inherit
                </button>
              </div>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
