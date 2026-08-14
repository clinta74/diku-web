import type { CSSProperties, ReactNode } from 'react'
import { RailHandle } from './RailHandle'
import { useRailWidths } from './useRailWidths'

interface Props {
  /** The tree or list rail. */
  left: ReactNode
  /** The editor. Takes whatever the rails leave. */
  main: ReactNode
  /** The side panel, on the tabs that have one. */
  right?: ReactNode
  /** Dialogs and anything else that is not a column. Modals portal out of the grid. */
  children?: ReactNode
}

/**
 * The builder's column layout, with the rails draggable.
 *
 * <b>A wrapper rather than a rule each tab repeats</b>, so the handles cannot be present on some
 * screens and missing on others — six tabs share this layout, and a resize that works in the world
 * editor but not the mob editor reads as broken rather than absent.
 *
 * <b>Named props rather than positional children</b>, and that is not a style preference. The
 * first version read `Children.toArray` and treated a third child as the right rail; four of the
 * six tabs pass a dialog after their two columns, so the Abilities tab would have rendered its
 * create dialog as a column with a resize handle in front of it. Positional APIs fail silently at
 * exactly this kind of call site.
 *
 * Widths are shared across tabs on purpose: they live in one localStorage entry, so a tree widened
 * to fit `aldenmoor.millbrook.tavern-common` stays wide when you switch to Mobs.
 */
export function BuilderColumns({ left, main, right, children }: Props) {
  const { widths, setLeft, setRight, reset } = useRailWidths()

  return (
    <div
      className={right ? 'builder-columns' : 'builder-columns builder-columns-2'}
      style={
        {
          '--rail-left': `${widths.left}px`,
          '--rail-right': `${widths.right}px`,
        } as CSSProperties
      }
    >
      {left}

      <RailHandle side="left" width={widths.left} onResize={setLeft} onReset={reset} />

      {main}

      {right && (
        <>
          <RailHandle side="right" width={widths.right} onResize={setRight} onReset={reset} />
          {right}
        </>
      )}

      {children}
    </div>
  )
}
