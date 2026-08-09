import { useEffect, useState } from 'react'
import { builderApi, type MultiplierPreview } from '../../net/builderApi'

interface Props {
  zoneKey: string
  /** Bumped by the caller after a save, so the table reflects the numbers just written. */
  refreshToken: number
}

/**
 * PLAN.md §7.5: every template in the zone with its base value beside its resolved value.
 *
 * The endpoint and `BuilderQueries.PreviewAsync` shipped with Phase 3 and were correct; nothing
 * ever called them, because there was no way to change a multiplier and therefore nothing to
 * preview. This is the missing half.
 */
export function MultiplierPreviewPanel({ zoneKey, refreshToken }: Props) {
  const [preview, setPreview] = useState<MultiplierPreview | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setError(null)
    void builderApi
      .zonePreview(zoneKey)
      .then((loaded) => {
        if (!cancelled) setPreview(loaded)
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load the preview.')
      })
    return () => {
      cancelled = true
    }
  }, [zoneKey, refreshToken])

  if (error) return <p className="bad">{error}</p>
  if (!preview) return <p className="dim">Loading preview…</p>

  if (preview.templates.length === 0) {
    return (
      <p className="dim">
        Nothing spawns in this zone yet, so there is nothing to scale. Add a spawner and the
        resolved numbers will appear here.
      </p>
    )
  }

  return (
    <div className="preview-table-wrap">
      <table className="preview-table">
        <thead>
          <tr>
            <th>Template</th>
            <th>Stat</th>
            <th className="numeric">Base</th>
            <th className="numeric">Resolved</th>
          </tr>
        </thead>
        <tbody>
          {preview.templates.map((row) =>
            Object.entries(row.resolvedStats).map(([stat, resolved], index) => {
              const base = row.baseStats[stat]
              return (
                <tr key={`${row.templateKey}-${stat}`}>
                  {/* The name spans its stats rather than repeating on every line. */}
                  {index === 0 && (
                    <th scope="row" rowSpan={Object.keys(row.resolvedStats).length}>
                      {row.templateName || row.templateKey}
                      <span className="dim"> · {row.kind}</span>
                    </th>
                  )}
                  <td>{stat}</td>
                  <td className="numeric dim">{base === undefined ? '—' : String(base)}</td>
                  <td className={resolved === Number(base) ? 'numeric dim' : 'numeric strong'}>
                    {resolved}
                  </td>
                </tr>
              )
            }),
          )}
        </tbody>
      </table>
    </div>
  )
}
