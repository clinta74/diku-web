import { useEffect, useRef } from 'react'
import { useNavigate, useOutletContext, useParams } from 'react-router'
import { useBuilderData } from '../BuilderData'
import { useNavGuard } from '../NavGuard'
import { keysFromParams, toWorldPath, type Section } from '../routes'
import type { BuilderOutletContext } from '../BuilderShell'
import { ZoneCanvas } from '../ZoneCanvas'
import { RoomEditor } from '../room/RoomEditor'
import { WorldTree } from './WorldTree'
import { WorldPanel } from './WorldPanel'
import { ZonePanel } from './ZonePanel'
import { ValidationPanel } from './ValidationPanel'

/**
 * The world tab: tree on the left, canvas + room editor in the middle, zone flags + warnings
 * on the right. Selection is entirely URL-driven; this component turns route params into data
 * loads and clicks into navigations.
 */
export function WorldTab() {
  const navigate = useNavigate()
  const params = useParams()
  const { worldKey, zoneKey, roomKey, section } = keysFromParams(params)
  const { worlds, rooms, validation, loadZones, loadZone } = useBuilderData()
  const { occupiedRoom, follow } = useOutletContext<BuilderOutletContext>()
  const guard = useNavGuard()

  // No world in the URL yet: land on the first one, replacing so Back does not trap here.
  useEffect(() => {
    if (!worldKey && worlds.length > 0) {
      navigate(toWorldPath(worlds[0].key), { replace: true })
    }
  }, [worldKey, worlds, navigate])

  useEffect(() => {
    void loadZones(worldKey)
  }, [worldKey, loadZones])

  useEffect(() => {
    void loadZone(zoneKey)
  }, [zoneKey, loadZone])

  // Follow mode: walking re-targets the editor. This must fire only on an *actual move* - a ref
  // tracks the last room we followed to, so re-runs caused by the navigate identity changing
  // (which happens on every navigation) do not yank you back off a room you just clicked.
  // Replace, not push, so a walk does not bury the Back button one entry per room crossed.
  const followedTo = useRef<string | null>(null)
  useEffect(() => {
    if (!follow) {
      followedTo.current = null
      return
    }
    if (!occupiedRoom || guard.isDirty()) return
    if (occupiedRoom === followedTo.current) return
    followedTo.current = occupiedRoom
    const [w, z] = occupiedRoom.split('.')
    navigate(toWorldPath(w, `${w}.${z}`, occupiedRoom, section), { replace: true })
  }, [follow, occupiedRoom, section, navigate, guard])

  // Room and world/zone changes route through the guard so unsaved prose prompts first.
  const goRoom = (key: string) => guard.run(() => navigate(toWorldPath(worldKey, zoneKey, key)))
  const goWorld = (key: string) => guard.run(() => navigate(toWorldPath(key)))
  const goZone = (key: string) => guard.run(() => navigate(toWorldPath(worldKey, key)))
  const roomWarnings = validation?.warnings.filter((w) => w.entityKey === roomKey) ?? []

  return (
    <div className="builder-columns">
      <aside className="builder-col">
        <WorldTree
          worldKey={worldKey}
          zoneKey={zoneKey}
          roomKey={roomKey}
          onWorld={goWorld}
          onZone={goZone}
          onRoom={goRoom}
        />
      </aside>

      <main className="builder-col">
        {zoneKey && (
          <ZoneCanvas
            rooms={rooms}
            selected={roomKey}
            occupied={occupiedRoom}
            onSelect={goRoom}
            onChanged={() => void loadZone(zoneKey)}
          />
        )}

        {roomKey ? (
          <RoomEditor
            key={roomKey}
            roomKey={roomKey}
            section={section}
            warnings={roomWarnings}
            onSection={(s: Section) => navigate(toWorldPath(worldKey, zoneKey, roomKey, s))}
            onChanged={() => void loadZone(zoneKey)}
            onDeleted={() => navigate(toWorldPath(worldKey, zoneKey))}
            onNavigate={goRoom}
          />
        ) : (
          <p className="dim">Pick a room, or dig one from the room you are standing in.</p>
        )}
      </main>

      <aside className="builder-col">
        {/* The zone panel wins the slot when a zone is selected: it is the narrower scope, and
            it carries the difficulty preview, which is what a builder is usually here for. The
            world panel is how you reach a world's own properties at all. */}
        {zoneKey ? (
          <ZonePanel zoneKey={zoneKey} />
        ) : (
          worldKey && (
            <WorldPanel worldKey={worldKey} onDeleted={() => navigate(toWorldPath(null))} />
          )
        )}
        <ValidationPanel validation={validation} onSelect={goRoom} />
      </aside>
    </div>
  )
}
