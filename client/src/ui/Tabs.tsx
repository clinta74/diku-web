import * as TabsPrimitive from '@radix-ui/react-tabs'
import type { ReactNode } from 'react'

export interface TabItem {
  value: string
  label: ReactNode
}

interface TabsProps {
  value: string
  onValueChange: (value: string) => void
  tabs: TabItem[]
  /** Extra class on the tab list, e.g. to distinguish the top bar from the room sub-tabs. */
  className?: string
  'aria-label'?: string
}

/**
 * A route-driven tab *bar* over Radix Tabs - real `role="tablist"`, roving tabindex, and
 * arrow-key navigation, which the three hand-rolled `<button>`s never had. The panels live
 * elsewhere (a router `<Outlet/>`), so this renders only the triggers; `onValueChange`
 * navigates rather than swapping local state.
 */
export function Tabs({ value, onValueChange, tabs, className, ...rest }: TabsProps) {
  return (
    <TabsPrimitive.Root value={value} onValueChange={onValueChange} activationMode="manual">
      <TabsPrimitive.List className={className ? `tabbar ${className}` : 'tabbar'} aria-label={rest['aria-label']}>
        {tabs.map((tab) => (
          <TabsPrimitive.Trigger key={tab.value} value={tab.value} className="tab">
            {tab.label}
          </TabsPrimitive.Trigger>
        ))}
      </TabsPrimitive.List>
    </TabsPrimitive.Root>
  )
}
