import { Outlet, useLocation, useNavigate } from 'react-router'
import { BuilderDataProvider } from './BuilderData'
import { NavGuardProvider, useNavGuard } from './NavGuard'
import { ToastProvider } from '../ui/Toast'
import { Tabs, type TabItem } from '../ui/Tabs'
import type { BuilderTab } from './routes'
import './builder.css'
import '../ui/ui.css'

/** Passed down to the routed tabs via the router Outlet. */
export interface BuilderOutletContext {
  occupiedRoom: string | null
  follow: boolean
  setFollow: (value: boolean) => void
}

interface BuilderShellProps {
  occupiedRoom: string | null
  follow: boolean
  onFollowChange: (value: boolean) => void
  onClose: () => void
}

const TABS: TabItem[] = [
  { value: 'world', label: 'World' },
  { value: 'mobs', label: 'Mobs' },
  { value: 'items', label: 'Items' },
]

function tabFromPath(pathname: string): BuilderTab {
  if (pathname.startsWith('/builder/mobs')) return 'mobs'
  if (pathname.startsWith('/builder/items')) return 'items'
  return 'world'
}

/**
 * The builder chrome: header, the World/Mobs/Items tab bar, and a router Outlet the tabs fill.
 * The data provider, toast host, and unsaved-changes guard live here so every tab and dialog
 * shares them.
 */
export function BuilderShell(props: BuilderShellProps) {
  return (
    <BuilderDataProvider>
      <ToastProvider>
        <NavGuardProvider>
          <ShellBody {...props} />
        </NavGuardProvider>
      </ToastProvider>
    </BuilderDataProvider>
  )
}

function ShellBody({ occupiedRoom, follow, onFollowChange, onClose }: BuilderShellProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const guard = useNavGuard()
  const tab = tabFromPath(location.pathname)

  return (
    <div className="builder">
      <div className="builder-topbar">
        <h2 className="builder-brand">World builder</h2>

        <Tabs
          value={tab}
          onValueChange={(value) => guard.run(() => navigate(`/builder/${value}`))}
          tabs={TABS}
          aria-label="Builder sections"
        />

        <div className="builder-topbar-right">
          {tab === 'world' && (
            <label className="follow-toggle">
              <input
                type="checkbox"
                checked={follow}
                onChange={(e) => onFollowChange(e.target.checked)}
              />
              Follow my character
              {occupiedRoom && <code className="dim"> {occupiedRoom}</code>}
            </label>
          )}
          <button
            type="button"
            className="builder-exit"
            onClick={() => guard.run(onClose)}
            title="Return to the game"
          >
            ✕ Exit builder
          </button>
        </div>
      </div>

      <Outlet context={{ occupiedRoom, follow, setFollow: onFollowChange } satisfies BuilderOutletContext} />
    </div>
  )
}
