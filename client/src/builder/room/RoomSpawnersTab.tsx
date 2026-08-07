import { useCallback, useEffect, useState } from 'react'
import { builderApi, type RoomDetail, type Spawner } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { SpawnerDialog } from '../dialogs/SpawnerDialog'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'

interface Props {
  room: RoomDetail
}

/** Spawners that place mobs in this room. Add/edit share one dialog; delete confirms. */
export function RoomSpawnersTab({ room }: Props) {
  const { mobTemplates } = useBuilderData()
  const toast = useToast()
  const [spawners, setSpawners] = useState<Spawner[]>([])
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<Spawner | null>(null)
  const [adding, setAdding] = useState(false)
  const [deleting, setDeleting] = useState<Spawner | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    void builderApi
      .spawners(room.zoneKey)
      .then((all) => setSpawners(all.filter((s) => s.roomKeys.includes(room.key))))
      .catch(() => setSpawners([]))
  }, [room.zoneKey, room.key])

  useEffect(load, [load])

  async function confirmDelete() {
    if (!deleting) return
    setBusy(true)
    try {
      await builderApi.deleteSpawner(deleting.id)
      setDeleting(null)
      load()
      toast.notify('Spawner removed')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not delete the spawner.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}

      {spawners.length === 0 ? (
        <p className="dim">No spawners in this room.</p>
      ) : (
        <ul className="spawner-list">
          {spawners.map((spawner) => (
            <li key={spawner.id}>
              <div className="spawner-info">
                <strong>{spawner.templateKey}</strong>
                <span className="dim">
                  {spawner.targetCount}× · respawn {spawner.respawnSeconds}s
                  {spawner.sentinel ? ' · sentinel' : ''}
                </span>
              </div>
              <div className="spawner-actions">
                <button type="button" onClick={() => setEditing(spawner)}>
                  Edit
                </button>
                <button type="button" className="danger-button" onClick={() => setDeleting(spawner)}>
                  Remove
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      <button type="button" onClick={() => setAdding(true)}>
        + Add spawner
      </button>

      <SpawnerDialog
        open={adding || editing !== null}
        onOpenChange={(open) => {
          if (!open) {
            setAdding(false)
            setEditing(null)
          }
        }}
        zoneKey={room.zoneKey}
        roomKey={room.key}
        mobTemplates={mobTemplates}
        editing={editing}
        onSaved={() => {
          load()
          toast.notify('Spawner saved')
        }}
      />

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title="Remove spawner?"
        description={
          deleting ? `The ${deleting.templateKey} spawner will stop populating this room.` : undefined
        }
        destructive
        busy={busy}
        confirmLabel="Remove"
        onConfirm={() => void confirmDelete()}
      />
    </div>
  )
}
