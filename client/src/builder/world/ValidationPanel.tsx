import type { ZoneValidation } from '../../net/builderApi'
import { Button } from '../../ui/Button'

interface ValidationPanelProps {
  validation: ZoneValidation | null
  onSelect: (roomKey: string) => void
}

/**
 * Advisory warnings for the current zone. Nothing here ever blocked a save (PLAN.md §7.4) -
 * live editing means the world is allowed to be broken, and this is how a builder finds out.
 */
export function ValidationPanel({ validation, onSelect }: ValidationPanelProps) {
  if (!validation) return null

  return (
    <section className="editor-section">
      <h3>Warnings ({validation.warnings.length})</h3>

      {validation.warnings.length === 0 && <p className="dim">Nothing to report.</p>}

      <ul className="warning-list">
        {validation.warnings.map((warning, i) => (
          <li key={i} className={`warning ${warning.kind}`}>
            <Button variant="link" onClick={() => onSelect(warning.entityKey)}>
              {warning.entityKey.split('.').pop()}
            </Button>
            <span className="dim"> {warning.message}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}
