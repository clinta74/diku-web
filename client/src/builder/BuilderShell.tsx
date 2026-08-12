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
  /** Admins get the Accounts tab; builders do not, and the route refuses them as well. */
  isAdmin: boolean
  onClose: () => void
}

const WORLD_TABS: TabItem[] = [
  { value: 'world', label: 'World' },
  { value: 'mobs', label: 'Mobs' },
  { value: 'items', label: 'Items' },
  { value: 'quests', label: 'Quests' },
]

const ACCOUNTS_TAB: TabItem = { value: 'accounts', label: 'Accounts' }

function tabFromPath(pathname: string): BuilderTab {
  if (pathname.startsWith('/builder/mobs')) return 'mobs'
  if (pathname.startsWith('/builder/items')) return 'items'
  if (pathname.startsWith('/builder/quests')) return 'quests'
  if (pathname.startsWith('/builder/accounts')) return 'accounts'
  return 'world'
}

/**
 * The builder chrome: header, the tab bar (World/Mobs/Items/Quests, plus Accounts for admins),
 * and a router Outlet the tabs fill.
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

function ShellBody({ occupiedRoom, follow, onFollowChange, isAdmin, onClose }: BuilderShellProps) {
  const navigate = useNavigate()
  const location = useLocation()
  const guard = useNavGuard()
  const tab = tabFromPath(location.pathname)

  // Hiding the tab is presentation, not access control - the route and the API both refuse a
  // non-admin independently, so a hand-typed /builder/accounts gets nowhere.
  const tabs = isAdmin ? [...WORLD_TABS, ACCOUNTS_TAB] : WORLD_TABS

  return (
    <div className="builder">
      <div className="builder-topbar">
        <h2 className="builder-brand">World builder</h2>

        <Tabs
          value={tab}
          onValueChange={(value) => guard.run(() => navigate(`/builder/${value}`))}
          tabs={tabs}
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
