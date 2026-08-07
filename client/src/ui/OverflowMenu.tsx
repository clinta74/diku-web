import * as DropdownMenu from '@radix-ui/react-dropdown-menu'

export interface MenuAction {
  label: string
  onSelect: () => void
  /** Renders the item in the danger colour; use for Delete. */
  destructive?: boolean
}

interface OverflowMenuProps {
  actions: MenuAction[]
  /** Accessible name for the trigger; defaults to a generic label. */
  label?: string
}

/**
 * The `⋯` overflow menu over Radix DropdownMenu - portalled, focus-managed, Escape-closable.
 * Used for a room's Rename / Delete so those rare, mostly-destructive actions move out of the
 * editor scroll.
 */
export function OverflowMenu({ actions, label = 'More actions' }: OverflowMenuProps) {
  return (
    <DropdownMenu.Root>
      <DropdownMenu.Trigger asChild>
        <button type="button" className="overflow-trigger" aria-label={label}>
          ⋯
        </button>
      </DropdownMenu.Trigger>

      <DropdownMenu.Portal>
        <DropdownMenu.Content className="menu" align="end" sideOffset={4}>
          {actions.map((action) => (
            <DropdownMenu.Item
              key={action.label}
              className={action.destructive ? 'menu-item menu-item-danger' : 'menu-item'}
              onSelect={action.onSelect}
            >
              {action.label}
            </DropdownMenu.Item>
          ))}
        </DropdownMenu.Content>
      </DropdownMenu.Portal>
    </DropdownMenu.Root>
  )
}
