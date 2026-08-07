import { useEffect, useState } from 'react'
import { builderApi, type RoomDetail, type ValidationWarning } from '../../net/builderApi'
import { Tabs, type TabItem } from '../../ui/Tabs'
import { OverflowMenu } from '../../ui/OverflowMenu'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'
import { useNavGuard } from '../NavGuard'
import { SECTIONS, type Section } from '../routes'
import { RoomDetailsTab } from './RoomDetailsTab'
import { RoomFlagsTab } from './RoomFlagsTab'
import { RoomTerrainTab } from './RoomTerrainTab'
import { RoomExitsTab } from './RoomExitsTab'
import { RoomSpawnersTab } from './RoomSpawnersTab'
import { RenameRoomDialog } from '../dialogs/RenameRoomDialog'

interface Props {
  roomKey: string
  section: Section
  warnings: ValidationWarning[]
  onSection: (section: Section) => void
  onChanged: () => void
  onDeleted: () => void
  onNavigate: (roomKey: string) => void
}

/** Which section each warning kind belongs under, so the tabs can show per-section counts. */
const WARNING_SECTION: Record<string, Section> = {
  'no-description': 'details',
  'dangling-exit': 'exits',
  'orphan-room': 'exits',
  'ragged-grid': 'terrain',
  'legend-gap': 'terrain',
  'inherited-pvp': 'flags',
  'unknown-flag': 'flags',
}

/**
 * The room editor: identity pinned at the top, the rest split into URL-addressable sub-tabs.
 * The Details buffer lives here (not in the sub-tab) so it survives a hop to Flags or Terrain,
 * and so a navigation away with unsaved prose can be caught before it is lost.
 */
export function RoomEditor({
  roomKey,
  section,
  warnings,
  onSection,
  onChanged,
  onDeleted,
  onNavigate,
}: Props) {
  const toast = useToast()
  const guard = useNavGuard()
  const [room, setRoom] = useState<RoomDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [busy, setBusy] = useState(false)

  // The Details edit buffer, lifted out of the sub-tab.
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [dirty, setDirty] = useState(false)
  const [savingDetails, setSavingDetails] = useState(false)

  useEffect(() => {
    let cancelled = false
    setRoom(null)
    setError(null)
    void builderApi
      .room(roomKey)
      .then((loaded) => {
        if (cancelled) return
        setRoom(loaded)
        setTitle(loaded.title)
        setDescription(loaded.description)
        setDirty(false)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load that room.')
      })
    return () => {
      cancelled = true
    }
  }, [roomKey])

  // Report unsaved prose to the nav guard, so a room switch, tab switch, or closing the builder
  // prompts before discarding it. Cleared on unmount so a stale flag can't block later.
  useEffect(() => {
    guard.setDirty(dirty)
    return () => guard.setDirty(false)
  }, [dirty, guard])

  // A hard reload or tab close cannot show a React dialog, so fall back to the browser's own.
  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  function applyChange(updated: RoomDetail) {
    setRoom(updated)
    onChanged()
  }

  async function saveDetails() {
    if (!room) return
    setSavingDetails(true)
    setError(null)
    try {
      // Field-scoped: only the prose. A whole-object PATCH would carry (and so replace) flags,
      // grid and legend, clobbering another builder's concurrent edits (PLAN §1).
      const updated = await builderApi.updateRoom(room.key, { title, description })
      setRoom(updated)
      setDirty(false)
      onChanged()
      toast.notify('Room saved')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setSavingDetails(false)
    }
  }

  async function confirmDelete() {
    if (!room) return
    setBusy(true)
    try {
      await builderApi.deleteRoom(room.key)
      toast.notify('Room deleted')
      setDeleting(false)
      setDirty(false)
      onDeleted()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
    } finally {
      setBusy(false)
    }
  }

  if (error && !room) return <p className="bad">{error}</p>
  if (!room) return <p className="dim">Loading…</p>

  const countFor = (target: Section) =>
    warnings.filter((w) => (WARNING_SECTION[w.kind] ?? 'details') === target).length

  const tabs: TabItem[] = SECTIONS.map((value) => {
    const count = countFor(value)
    return {
      value,
      label: (
        <>
          {value}
          {value === 'details' && dirty && <span className="tab-dot" title="Unsaved changes">●</span>}
          {count > 0 && <span className="tab-count">{count}</span>}
        </>
      ),
    }
  })

  return (
    <div className="room-editor">
      <header className="room-editor-head">
        <div>
          <h2>{room.title}</h2>
          <code className="room-key">{room.key}</code>
        </div>
        <OverflowMenu
          actions={[
            { label: 'Rename…', onSelect: () => setRenaming(true) },
            { label: 'Delete room…', onSelect: () => setDeleting(true), destructive: true },
          ]}
        />
      </header>

      {error && <p className="bad">{error}</p>}

      <Tabs
        value={section}
        onValueChange={(v) => onSection(v as Section)}
        tabs={tabs}
        aria-label="Room sections"
      />

      <div className="room-section">
        {section === 'details' && (
          <RoomDetailsTab
            title={title}
            description={description}
            dirty={dirty}
            busy={savingDetails}
            error={error}
            onTitle={(v) => {
              setTitle(v)
              setDirty(true)
            }}
            onDescription={(v) => {
              setDescription(v)
              setDirty(true)
            }}
            onSave={() => void saveDetails()}
          />
        )}
        {section === 'flags' && <RoomFlagsTab room={room} onChanged={applyChange} />}
        {section === 'terrain' && <RoomTerrainTab room={room} onChanged={applyChange} />}
        {section === 'exits' && (
          <RoomExitsTab room={room} onChanged={applyChange} onNavigate={onNavigate} />
        )}
        {section === 'spawners' && <RoomSpawnersTab room={room} />}
      </div>

      <RenameRoomDialog
        open={renaming}
        onOpenChange={setRenaming}
        roomKey={room.key}
        onRenamed={(next) => {
          toast.notify('Room renamed')
          setDirty(false)
          onNavigate(next.key)
          onChanged()
        }}
      />

      <ConfirmDialog
        open={deleting}
        onOpenChange={setDeleting}
        title={`Delete ${room.key}?`}
        description="Exits pointing at it will be left dangling. Anyone standing here is moved to a neighbouring room."
        destructive
        busy={busy}
        confirmLabel="Delete room"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}
