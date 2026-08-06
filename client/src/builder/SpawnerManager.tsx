import { useEffect, useState } from 'react'
import { builderApi, type MobTemplate, type Spawner } from '../net/builderApi'

interface Props {
  roomKey: string
  zoneKey: string
  onChanged: () => void
}

export function SpawnerManager({ roomKey, zoneKey, onChanged }: Props) {
  const [spawners, setSpawners] = useState<Spawner[]>([])
  const [mobTemplates, setMobTemplates] = useState<MobTemplate[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [selectedTemplate, setSelectedTemplate] = useState<string>('')
  const [targetCount, setTargetCount] = useState(1)
  const [respawnSeconds, setRespawnSeconds] = useState(60)

  // Load spawners for this zone
  useEffect(() => {
    let cancelled = false

    void builderApi
      .spawners(zoneKey)
      .then((loaded) => {
        if (cancelled) return
        // Filter to spawners that contain this room
        const filtered = loaded.filter((s) => s.roomKeys.includes(roomKey))
        setSpawners(filtered)
      })
      .catch(() => setSpawners([]))

    return () => {
      cancelled = true
    }
  }, [roomKey, zoneKey])

  // Load mob templates once
  useEffect(() => {
    void builderApi
      .mobTemplates()
      .then(setMobTemplates)
      .catch(() => setMobTemplates([]))
  }, [])

  async function addSpawner() {
    if (!selectedTemplate) {
      setError('Select a mob template')
      return
    }

    setBusy(true)
    setError(null)

    try {
      const spawner = await builderApi.createSpawner({
        zoneKey,
        templateKey: selectedTemplate,
        templateKind: 'Mob',
        roomKeys: [roomKey],
        targetCount,
        respawnSeconds,
      })
      setSpawners([...spawners, spawner])
      setSelectedTemplate('')
      setTargetCount(1)
      setRespawnSeconds(60)
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create spawner.')
    } finally {
      setBusy(false)
    }
  }

  async function removeSpawner(id: string) {
    if (!window.confirm('Remove this spawner?')) return

    setBusy(true)
    setError(null)

    try {
      await builderApi.deleteSpawner(id)
      setSpawners(spawners.filter((s) => s.id !== id))
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not delete spawner.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="spawner-manager">
      <h3>Spawners in this room</h3>

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
                  {spawner.targetCount}x, respawn {spawner.respawnSeconds}s
                </span>
              </div>
              <button
                type="button"
                className="bad"
                onClick={() => void removeSpawner(spawner.id)}
                disabled={busy}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="spawner-form">
        <label>
          Mob template
          <select
            value={selectedTemplate}
            onChange={(e) => setSelectedTemplate(e.target.value)}
            disabled={busy}
          >
            <option value="">— Select mob —</option>
            {mobTemplates.map((template) => (
              <option key={template.key} value={template.key}>
                {template.name || template.key}
              </option>
            ))}
          </select>
        </label>

        <label>
          Target count
          <input
            type="number"
            value={targetCount}
            onChange={(e) => setTargetCount(parseInt(e.target.value) || 1)}
            disabled={busy}
            min="1"
          />
        </label>

        <label>
          Respawn seconds
          <input
            type="number"
            value={respawnSeconds}
            onChange={(e) => setRespawnSeconds(parseInt(e.target.value) || 60)}
            disabled={busy}
            min="10"
            step="10"
          />
        </label>

        <button
          type="button"
          onClick={() => void addSpawner()}
          disabled={!selectedTemplate || busy}
          className="good"
        >
          Add spawner
        </button>
      </div>
    </div>
  )
}
