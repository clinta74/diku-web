import { useState } from 'react'
import { builderApi, type RoomDetail } from '../../net/builderApi'
import { GridPainter } from '../GridPainter'

interface Props {
  room: RoomDetail
  onChanged: (room: RoomDetail) => void
}

/** The ASCII terrain grid and its legend. Autosaves on every stroke - a field-scoped PATCH. */
export function RoomTerrainTab({ room, onChanged }: Props) {
  const [error, setError] = useState<string | null>(null)

  return (
    <div className="section-body">
      <p className="dim detail">Changes save as you paint.</p>
      {error && <p className="bad">{error}</p>}

      <GridPainter
        grid={room.grid}
        legend={room.legend}
        onChange={(grid, legend) => {
          void builderApi
            .updateRoom(room.key, { grid, legend })
            .then(onChanged)
            .catch((e) => setError(e instanceof Error ? e.message : 'Could not save terrain.'))
        }}
      />
    </div>
  )
}
