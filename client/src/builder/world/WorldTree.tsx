import { useState } from 'react'
import { Button } from '../../ui/Button'
import { builderApi } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { EntityFormDialog } from '../dialogs/EntityFormDialog'

interface WorldTreeProps {
  worldKey: string | null
  zoneKey: string | null
  roomKey: string | null
  onWorld: (key: string) => void
  onZone: (key: string) => void
  onRoom: (key: string) => void
}

/**
 * The world → zone → room navigator. Selection is driven by the URL (the callbacks navigate),
 * and every "+ new" opens a portalled dialog instead of a native prompt.
 */
export function WorldTree({ worldKey, zoneKey, roomKey, onWorld, onZone, onRoom }: WorldTreeProps) {
  const { worlds, zones, rooms, refreshWorlds, loadZones, loadZone } = useBuilderData()
  const [filter, setFilter] = useState('')
  const [creating, setCreating] = useState<'world' | 'zone' | 'room' | null>(null)

  const visibleRooms = rooms.filter(
    (r) =>
      !filter ||
      r.key.includes(filter.toLowerCase()) ||
      r.title.toLowerCase().includes(filter.toLowerCase()),
  )

  return (
    <div className="tree">
      <div className="tree-section">
        <div className="tree-head">
          <h3>Worlds</h3>
          <Button variant="link" onClick={() => setCreating('world')}>
            + new
          </Button>
        </div>
        <ul>
          {worlds.map((world) => (
            <li key={world.key}>
              <button
                type="button"
                className={world.key === worldKey ? 'selected' : ''}
                onClick={() => onWorld(world.key)}
              >
                {world.name}
                <span className="dim"> · {world.zoneCount} zones</span>
              </button>
            </li>
          ))}
        </ul>
      </div>

      <div className="tree-section">
        <div className="tree-head">
          <h3>Zones</h3>
          <Button
           
            variant="link"
            onClick={() => setCreating('zone')}
            disabled={!worldKey}
          >
            + new
          </Button>
        </div>
        <ul>
          {zones.map((zone) => (
            <li key={zone.key}>
              <button
                type="button"
                className={zone.key === zoneKey ? 'selected' : ''}
                onClick={() => onZone(zone.key)}
              >
                {zone.name}
                <span className="dim"> · {zone.roomCount} rooms</span>
              </button>
            </li>
          ))}
        </ul>
      </div>

      <div className="tree-section">
        <div className="tree-head">
          <h3>Rooms</h3>
          <Button
           
            variant="link"
            onClick={() => setCreating('room')}
            disabled={!zoneKey}
          >
            + new
          </Button>
        </div>

        <input
          className="tree-filter"
          value={filter}
          placeholder="filter"
          spellCheck={false}
          onChange={(e) => setFilter(e.target.value)}
        />

        <ul className="room-list">
          {visibleRooms.map((room) => (
            <li key={room.key}>
              <button
                type="button"
                className={room.key === roomKey ? 'selected' : ''}
                onClick={() => onRoom(room.key)}
              >
                {room.title}
                {room.flags.unfinished === true && <span className="dim"> · stub</span>}
              </button>
            </li>
          ))}
        </ul>
      </div>

      <EntityFormDialog
        open={creating === 'world'}
        onOpenChange={(open) => setCreating(open ? 'world' : null)}
        title="New world"
        keyLabel="World key"
        keyPlaceholder="the-underdark"
        onSubmit={async ({ key, name }) => {
          await builderApi.createWorld(key, { name: name || key, description: '' })
          await refreshWorlds()
          onWorld(key)
        }}
      />

      <EntityFormDialog
        open={creating === 'zone'}
        onOpenChange={(open) => setCreating(open ? 'zone' : null)}
        title="New zone"
        keyLabel="Zone slug"
        keyPlaceholder="sunken-crypt"
        keyPrefix={worldKey ? `${worldKey}.` : undefined}
        onSubmit={async ({ key, name }) => {
          if (!worldKey) return
          const zoneKeyNext = `${worldKey}.${key}`
          await builderApi.createZone(zoneKeyNext, { worldKey, name: name || key, description: '' })
          await Promise.all([refreshWorlds(), loadZones(worldKey)])
          onZone(zoneKeyNext)
        }}
      />

      <EntityFormDialog
        open={creating === 'room'}
        onOpenChange={(open) => setCreating(open ? 'room' : null)}
        title="New room"
        keyLabel="Room slug"
        keyPlaceholder="north-gate"
        keyPrefix={zoneKey ? `${zoneKey}.` : undefined}
        withName={false}
        onSubmit={async ({ key }) => {
          if (!zoneKey) return
          const roomKeyNext = `${zoneKey}.${key}`
          await builderApi.createRoom(roomKeyNext, { zoneKey, title: key, description: '' })
          await loadZone(zoneKey)
          onRoom(roomKeyNext)
        }}
      />
    </div>
  )
}
