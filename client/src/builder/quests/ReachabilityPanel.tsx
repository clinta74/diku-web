import type { ReachabilityWarning } from '../../net/builderApi'

interface Props {
  warnings: ReachabilityWarning[]
  checking: boolean
}

/**
 * Why this quest could not be finished, shown beside the fields that cause it.
 *
 * `/reachability` walks loot tables and spawners to answer the one question that cannot be
 * answered by reading the quest: **is the required item obtainable at all?** §10 lists that as
 * the failure that has to be caught in the editor, because it fails silently in play — the quest
 * reads correctly, the journal reads correctly, and the player just wanders.
 *
 * Advisory, never blocking. Content is routinely wired before its targets exist (§7.4), so a
 * warning here is often a note about work still to do rather than a mistake.
 */
export function ReachabilityPanel({ warnings, checking }: Props) {
  if (checking) {
    return <p className="dim detail">Checking…</p>
  }

  if (warnings.length === 0) {
    return <p className="dim detail">Finishable: every piece this quest needs can be obtained.</p>
  }

  return (
    <ul className="warning-list">
      {warnings.map((warning, index) => (
        <li key={`${warning.kind}-${index}`} className="warn">
          {warning.message}
          {(warning.itemKey ?? warning.mobKey) && (
            <code className="dim"> {warning.itemKey ?? warning.mobKey}</code>
          )}
        </li>
      ))}
    </ul>
  )
}
