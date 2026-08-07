import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { ConfirmDialog } from '../ui/ConfirmDialog'

/**
 * Guards navigation that would discard unsaved work. `useBlocker` can't be used here - it needs
 * a data router, and the app runs on a plain <BrowserRouter> - so instead the editor reports
 * whether it is dirty, and navigation entry points route through `run`, which prompts first.
 */
interface NavGuardValue {
  /** The editor calls this as its unsaved state changes. */
  setDirty: (dirty: boolean) => void
  /** Read the current dirty state without subscribing (for the follow effect). */
  isDirty: () => boolean
  /** Run a navigation, prompting to discard first if there are unsaved changes. */
  run: (intent: () => void) => void
}

const NavGuardContext = createContext<NavGuardValue | null>(null)

export function useNavGuard(): NavGuardValue {
  const ctx = useContext(NavGuardContext)
  if (!ctx) {
    throw new Error('useNavGuard must be used within <NavGuardProvider>')
  }
  return ctx
}

export function NavGuardProvider({ children }: { children: ReactNode }) {
  const dirty = useRef(false)
  const [pending, setPending] = useState<(() => void) | null>(null)

  const setDirty = useCallback((value: boolean) => {
    dirty.current = value
  }, [])

  const isDirty = useCallback(() => dirty.current, [])

  const run = useCallback((intent: () => void) => {
    if (dirty.current) {
      // Store the intent to run if the user confirms the discard.
      setPending(() => intent)
    } else {
      intent()
    }
  }, [])

  const value = useMemo<NavGuardValue>(() => ({ setDirty, isDirty, run }), [setDirty, isDirty, run])

  return (
    <NavGuardContext.Provider value={value}>
      {children}
      <ConfirmDialog
        open={pending !== null}
        onOpenChange={(open) => {
          if (!open) setPending(null)
        }}
        title="Discard unsaved changes?"
        description="This room's title or description has changes you haven't saved."
        destructive
        confirmLabel="Discard changes"
        cancelLabel="Keep editing"
        onConfirm={() => {
          dirty.current = false
          const go = pending
          setPending(null)
          go?.()
        }}
      />
    </NavGuardContext.Provider>
  )
}
