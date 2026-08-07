import { useState } from 'react'
import { builderApi } from '../../net/builderApi'
import { useBuilderData } from '../BuilderData'

interface ZonePanelProps {
  zoneKey: string
}

/**
 * Zone-level flags, inherited by every room that does not override them. Still a whole-map
 * write - there is no per-flag zone primitive yet (PLAN §1) - but only two flags exist and
 * zones are rarely edited concurrently.
 */
export function ZonePanel({ zoneKey }: ZonePanelProps) {
  const { zones, loadZones } = useBuilderData()
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
      .then(() => loadZones(zone.worldKey))
      .catch((e) => setError(e instanceof Error ? e.message : 'Could not save.'))
  }

  return (
    <section className="editor-section">
      <h3>{zone.name}</h3>
      <code className="dim">{zone.key}</code>

      {error && <p className="bad">{error}</p>}

      <p className="detail dim">Zone flags apply to every room that does not override them.</p>

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
