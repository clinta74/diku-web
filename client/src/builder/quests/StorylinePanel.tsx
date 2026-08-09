import { useEffect, useState } from 'react'
import { builderApi, type Storyline } from '../../net/builderApi'
import { chainOrder } from './quests'

interface Props {
  zoneKey: string | null
  selectedKey: string | null
  onSelect: (key: string) => void
  /** Bumped by the editor after a save, so the chain redraws without a remount. */
  revision: number
}

/**
 * The chain, as indented text rather than as a canvas.
 *
 * Prerequisites are the only storyline mechanism there is (§4.9), so the shape worth seeing is
 * depth: what can be started now, and what has to wait. A node graph would be prettier and would
 * answer that question less directly — indentation *is* "how far in is this".
 *
 * Cycles and unreachable quests are called out by name. Both mean the same thing to a player —
 * the quest can never be offered — and neither is visible from the quest's own editor, because
 * the fault is in the relationship rather than in either quest.
 */
export function StorylinePanel({ zoneKey, selectedKey, onSelect, revision }: Props) {
  const [graph, setGraph] = useState<Storyline | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!zoneKey) {
      setGraph(null)
      return
    }

    let cancelled = false
    setLoading(true)
    void builderApi
      .storyline(zoneKey)
      .then((loaded) => {
        if (!cancelled) setGraph(loaded)
      })
      .catch(() => {
        if (!cancelled) setGraph(null)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [zoneKey, revision])

  if (!zoneKey) {
    return <p className="dim detail">Select a quest to see its zone's chain.</p>
  }

  if (loading && !graph) {
    return <p className="dim detail">Loading chain…</p>
  }

  if (!graph || graph.nodes.length === 0) {
    return <p className="dim detail">No quests in this zone yet.</p>
  }

  const cycles = new Set(graph.cycles)
  const unreachable = new Set(graph.unreachable)
  const ordered = chainOrder(graph.nodes, graph.edges)

  return (
    <div className="section-body">
      <ul className="chain-list">
        {ordered.map(({ node, depth }) => (
          <li key={node.key} style={{ paddingLeft: `${depth * 1.1}rem` }}>
            <button
              type="button"
              className={node.key === selectedKey ? 'link selected' : 'link'}
              onClick={() => onSelect(node.key)}
              disabled={node.external}
              title={node.external ? `Lives in ${node.zoneKey}` : node.key}
            >
              {depth > 0 && <span className="dim">└ </span>}
              {node.name || node.key}
            </button>

            {node.external && <span className="dim"> · {node.zoneKey}</span>}
            {cycles.has(node.key) && <span className="bad"> · in a cycle</span>}
            {!cycles.has(node.key) && unreachable.has(node.key) && (
              <span className="bad"> · unreachable</span>
            )}
          </li>
        ))}
      </ul>

      {graph.cycles.length > 0 && (
        <p className="bad detail">
          A prerequisite cycle makes every quest in it unstartable — nothing in the loop can ever
          be the first one done.
        </p>
      )}

      {graph.missingPrerequisites.length > 0 && (
        <ul className="warning-list">
          {graph.missingPrerequisites.map((miss) => (
            <li key={`${miss.quest}-${miss.missing}`} className="warn">
              <code>{miss.quest}</code> requires <code>{miss.missing}</code>, which does not exist.
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
