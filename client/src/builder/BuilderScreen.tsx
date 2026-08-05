import { useCallback, useEffect, useState } from 'react'
import {
  builderApi,
  type RoomDetail,
  type RoomFlagDefinition,
  type WorldSummary,
  type ZoneSummary,
  type ZoneValidation,
} from '../net/builderApi'
import { RoomEditor } from './RoomEditor'
import { ZoneCanvas } from './ZoneCanvas'

interface Props {
  /**
   * The room the builder's character is standing in, pushed down from the game screen.
   * Follow mode re-targets the editor as they walk, which is what makes "edit the room you
   * are in" work (PLAN.md §7.6).
   */
  occupiedRoom: string | null
  onClose: () => void
}

export function BuilderScreen({ occupiedRoom, onClose }: Props) {
  const [flagDefinitions, setFlagDefinitions] = useState<RoomFlagDefinition[]>([])
  const [worlds, setWorlds] = useState<WorldSummary[]>([])
  const [zones, setZones] = useState<ZoneSummary[]>([])
  const [rooms, setRooms] = useState<RoomDetail[]>([])

  const [worldKey, setWorldKey] = useState<string | null>(null)
  const [zoneKey, setZoneKey] = useState<string | null>(null)
  const [roomKey, setRoomKey] = useState<string | null>(null)

  const [validation, setValidation] = useState<ZoneValidation | null>(null)
  const [follow, setFollow] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    void builderApi.roomFlags().then(setFlagDefinitions).catch(() => undefined)
    void builderApi
      .worlds()
      .then((loaded) => {
        setWorlds(loaded)
        if (loaded.length > 0) setWorldKey((current) => current ?? loaded[0].key)
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not load worlds.'))
  }, [])

  useEffect(() => {
    if (!worldKey) return
    void builderApi
      .zones(worldKey)
      .then((loaded) => {
        setZones(loaded)
        setZoneKey((current) =>
          current && loaded.some((z) => z.key === current) ? current : (loaded[0]?.key ?? null),
        )
      })
      .catch(() => setZones([]))
  }, [worldKey])

  const refreshZone = useCallback(() => {
    if (!zoneKey) {
      setRooms([])
      setValidation(null)
      return
    }

    void builderApi.rooms(zoneKey).then(setRooms).catch(() => setRooms([]))
    void builderApi.validate(zoneKey).then(setValidation).catch(() => setValidation(null))
  }, [zoneKey])

  useEffect(refreshZone, [refreshZone])

  // Follow mode. Selecting the occupied room also pulls the tree over to its world and zone,
  // so walking across a zone boundary re-targets everything rather than showing a room that
  // is not in the list on the left.
  useEffect(() => {
    if (!follow || !occupiedRoom) return

    const [world, zone] = occupiedRoom.split('.')
    setWorldKey(world)
    setZoneKey(`${world}.${zone}`)
    setRoomKey(occupiedRoom)
  }, [follow, occupiedRoom])

  const warningsFor = (key: string) =>
    validation?.warnings.filter((w) => w.entityKey === key) ?? []

  return (
    <div className="builder">
      <aside className="builder-tree">
        <div className="builder-head">
          <h2>World builder</h2>
          <button type="button" className="link" onClick={onClose}>
            close
          </button>
        </div>

        {error && <p className="bad">{error}</p>}

        <WorldTree
          worlds={worlds}
          zones={zones}
          rooms={rooms}
          worldKey={worldKey}
          zoneKey={zoneKey}
          roomKey={roomKey}
          onWorld={setWorldKey}
          onZone={(key) => {
            setZoneKey(key)
            setRoomKey(null)
          }}
          onRoom={setRoomKey}
          onCreated={() => {
            void builderApi.worlds().then(setWorlds)
            if (worldKey) void builderApi.zones(worldKey).then(setZones)
            refreshZone()
          }}
        />

        <label className="follow-toggle">
          <input type="checkbox" checked={follow} onChange={(e) => setFollow(e.target.checked)} />
          Follow my character
          {occupiedRoom && <code className="dim"> {occupiedRoom}</code>}
        </label>
      </aside>

      <main className="builder-main">
        {zoneKey && (
          <ZoneCanvas
            rooms={rooms}
            selected={roomKey}
            occupied={occupiedRoom}
            onSelect={setRoomKey}
            onChanged={refreshZone}
          />
        )}

        {roomKey ? (
          <>
            <RoomWarnings warnings={warningsFor(roomKey)} />
            <RoomEditor
              roomKey={roomKey}
              flagDefinitions={flagDefinitions}
              onChanged={refreshZone}
              onDeleted={() => {
                setRoomKey(null)
                refreshZone()
              }}
              onNavigate={(key) => setRoomKey(key)}
            />
          </>
        ) : (
          <p className="dim">Pick a room, or dig one from the room you are standing in.</p>
        )}
      </main>

      <aside className="builder-side">
        {zoneKey && <ZonePanel zoneKey={zoneKey} zones={zones} onSaved={refreshZone} />}
        <ValidationPanel validation={validation} onSelect={setRoomKey} />
      </aside>
    </div>
  )
}

function WorldTree({
  worlds,
  zones,
  rooms,
  worldKey,
  zoneKey,
  roomKey,
  onWorld,
  onZone,
  onRoom,
  onCreated,
}: {
  worlds: WorldSummary[]
  zones: ZoneSummary[]
  rooms: RoomDetail[]
  worldKey: string | null
  zoneKey: string | null
  roomKey: string | null
  onWorld: (key: string) => void
  onZone: (key: string) => void
  onRoom: (key: string) => void
  onCreated: () => void
}) {
  const [filter, setFilter] = useState('')
  const [error, setError] = useState<string | null>(null)

  const visible = rooms.filter(
    (r) =>
      !filter ||
      r.key.includes(filter.toLowerCase()) ||
      r.title.toLowerCase().includes(filter.toLowerCase()),
  )

  async function createWorld() {
    const key = prompt('New world key (lowercase, e.g. "the-underdark")')?.trim()
    if (!key) return

    try {
      await builderApi.createWorld(key, { name: key, description: '' })
      onCreated()
      onWorld(key)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that world.')
    }
  }

  async function createZone() {
    if (!worldKey) return
    const slug = prompt('New zone slug (lowercase, e.g. "sunken-crypt")')?.trim()
    if (!slug) return

    const key = `${worldKey}.${slug}`

    try {
      await builderApi.createZone(key, { worldKey, name: slug, description: '' })
      onCreated()
      onZone(key)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that zone.')
    }
  }

  async function createRoom() {
    if (!zoneKey) return
    const slug = prompt('New room slug (lowercase, e.g. "north-gate")')?.trim()
    if (!slug) return

    const key = `${zoneKey}.${slug}`

    try {
      await builderApi.createRoom(key, { zoneKey, title: slug, description: '' })
      onCreated()
      onRoom(key)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create that room.')
    }
  }

  return (
    <div className="tree">
      {error && <p className="bad">{error}</p>}

      <div className="tree-section">
        <div className="tree-head">
          <h3>Worlds</h3>
          <button type="button" className="link" onClick={() => void createWorld()}>
            + new
          </button>
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
          <button type="button" className="link" onClick={() => void createZone()}>
            + new
          </button>
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
          <button type="button" className="link" onClick={() => void createRoom()}>
            + new
          </button>
        </div>

        <input
          className="tree-filter"
          value={filter}
          placeholder="filter"
          spellCheck={false}
          onChange={(e) => setFilter(e.target.value)}
        />

        <ul className="room-list">
          {visible.map((room) => (
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
    </div>
  )
}

function ZonePanel({
  zoneKey,
  zones,
  onSaved,
}: {
  zoneKey: string
  zones: ZoneSummary[]
  onSaved: () => void
}) {
  const zone = zones.find((z) => z.key === zoneKey)
  const [error, setError] = useState<string | null>(null)

  if (!zone) return null

  function toggle(flag: string) {
    if (!zone) return

    const next = { ...zone.flags }
    if (next[flag]) delete next[flag]
    else next[flag] = true

    void builderApi
      .updateZone(zone.key, { flags: next })
      .then(onSaved)
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not save.'))
  }

  return (
    <section className="editor-section">
      <h3>{zone.name}</h3>
      <code className="dim">{zone.key}</code>

      {error && <p className="bad">{error}</p>}

      <p className="detail dim">
        Zone flags apply to every room that does not override them.
      </p>

      <div className="flag-controls">
        <button
          type="button"
          className={zone.flags.pvp ? 'selected' : ''}
          onClick={() => toggle('pvp')}
        >
          pvp
        </button>
        <button
          type="button"
          className={zone.flags.peaceful ? 'selected' : ''}
          onClick={() => toggle('peaceful')}
        >
          peaceful
        </button>
      </div>
    </section>
  )
}

/**
 * Advisory only — nothing here ever blocked a save (PLAN.md §7.4). Live editing means the
 * world is allowed to be broken; this is how a builder finds out that it is.
 */
function ValidationPanel({
  validation,
  onSelect,
}: {
  validation: ZoneValidation | null
  onSelect: (roomKey: string) => void
}) {
  if (!validation) return null

  return (
    <section className="editor-section">
      <h3>Warnings ({validation.warnings.length})</h3>

      {validation.warnings.length === 0 && <p className="dim">Nothing to report.</p>}

      <ul className="warning-list">
        {validation.warnings.map((warning, i) => (
          <li key={i} className={`warning ${warning.kind}`}>
            <button type="button" className="link" onClick={() => onSelect(warning.entityKey)}>
              {warning.entityKey.split('.').pop()}
            </button>
            <span className="dim"> {warning.message}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}

function RoomWarnings({ warnings }: { warnings: { kind: string; message: string }[] }) {
  if (warnings.length === 0) return null

  return (
    <ul className="room-warnings">
      {warnings.map((warning, i) => (
        <li key={i} className={warning.kind}>
          {warning.message}
        </li>
      ))}
    </ul>
  )
}
