import { useCallback, useEffect, useState } from 'react'
import { builderApi, type RoomDetail, type Spawner } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'
import { SpawnerDialog } from '../dialogs/SpawnerDialog'
import { Button } from '../../ui/Button'
import { ConfirmDialog } from '../../ui/ConfirmDialog'
import { useToast } from '../../ui/Toast'

interface Props {
  room: RoomDetail
}

/** Spawners that populate this room. Add/edit share one dialog; removal confirms. */
export function RoomSpawnersTab({ room }: Props) {
  const { mobTemplates, itemTemplates, rooms } = useBuilderData()
  const toast = useToast()
  const [spawners, setSpawners] = useState<Spawner[]>([])
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<Spawner | null>(null)
  const [adding, setAdding] = useState(false)
  const [removing, setRemoving] = useState<Spawner | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => {
    void builderApi
      .spawners(room.zoneKey)
      .then((all) => setSpawners(all.filter((s) => s.roomKeys.includes(room.key))))
      .catch(() => setSpawners([]))
  }, [room.zoneKey, room.key])

  useEffect(load, [load])

  /**
   * Removal is scoped to this room when the spawner covers others.
   *
   * The list shows every spawner *containing* this room, so a zone-wide spawner appears in each
   * of its rooms. "Remove" used to delete the whole thing while the confirmation said it would
   * "stop populating this room" — a builder tidying one room could empty twenty.
   */
  async function confirmRemove() {
    if (!removing) return
    setBusy(true)
    setError(null)
    try {
      const remaining = removing.roomKeys.filter((k) => k !== room.key)

      if (remaining.length > 0) {
        await builderApi.updateSpawner(removing.id, { roomKeys: remaining })
        toast.notify('Spawner no longer fills this room')
      } else {
        await builderApi.deleteSpawner(removing.id)
        toast.notify('Spawner removed')
      }

      setRemoving(null)
      load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not remove the spawner.')
    } finally {
      setBusy(false)
    }
  }

  const zoneRooms = rooms.filter((r) => r.zoneKey === room.zoneKey)

  return (
    <div className="section-body">
      {error && <p className="bad">{error}</p>}

      {spawners.length === 0 ? (
        <p className="dim">No spawners in this room.</p>
      ) : (
        <ul className="spawner-list">
          {spawners.map((spawner) => {
            const elsewhere = spawner.roomKeys.length - 1
            return (
              <li key={spawner.id}>
                <div className="spawner-info">
                  <strong>{spawner.templateKey}</strong>
                  <span className="dim">
                    {spawner.templateKind === 'Item' ? 'item · ' : ''}
                    {spawner.targetCount}× · respawn {spawner.respawnSeconds}s
                    {/* Unlike the wander note below, this is printed on every mob row rather than
                        only when it is unusual. The level is not a setting with a quiet default —
                        it is what the fight is, it decides whether killing it teaches anyone
                        anything (§4.7), and it is not readable from the template once a zone has
                        scaled it. */}
                    {spawner.templateKind === 'Mob' && spawner.fightsAtLevel > 0 &&
                      ` · fights at level ${spawner.fightsAtLevel}`}
                    {/* The name this placement gives the mob, when it gives one. The template key
                        above says what is placed; this says what the room will call it (§4.8). */}
                    {spawner.templateKind === 'Mob' && spawner.nameModifier && spawner.spawnsAs
                      ? ` · as ${spawner.spawnsAs}`
                      : ''}
                    {/* Only an override is worth a word here. "Follows the template" is the
                        default on every spawner, so printing it would say nothing on most rows
                        and bury the two that are actually unusual. */}
                    {spawner.wander === 'never' ? ' · never wanders' : ''}
                    {spawner.wander === 'always' ? ' · always wanders' : ''}
                    {/* Say so plainly: the count is shared across the whole set, so this room
                        does not necessarily get targetCount of them. */}
                    {elsewhere > 0 && ` · shared with ${elsewhere} other room${elsewhere === 1 ? '' : 's'}`}
                  </span>
                </div>
                <div className="spawner-actions">
                  <button type="button" onClick={() => setEditing(spawner)}>
                    Edit
                  </button>
                  <Button variant="danger" onClick={() => setRemoving(spawner)}>
                    Remove
                  </Button>
                </div>
              </li>
            )
          })}
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
        itemTemplates={itemTemplates}
        zoneRooms={zoneRooms}
        editing={editing}
        onSaved={() => {
          load()
          toast.notify('Spawner saved')
        }}
      />

      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={
          removing && removing.roomKeys.length > 1 ? 'Stop filling this room?' : 'Delete spawner?'
        }
        description={
          removing
            ? removing.roomKeys.length > 1
              ? `The ${removing.templateKey} spawner covers ${removing.roomKeys.length} rooms. This removes only this one; the spawner keeps filling the other ${removing.roomKeys.length - 1}.`
              : `This room is the only one the ${removing.templateKey} spawner fills, so the spawner is deleted outright.`
            : undefined
        }
        destructive
        busy={busy}
        confirmLabel={removing && removing.roomKeys.length > 1 ? 'Remove from this room' : 'Delete spawner'}
        onConfirm={() => void confirmRemove()}
      />
    </div>
  )
}
