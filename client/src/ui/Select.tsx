import type { ReactNode } from 'react'

interface SelectProps {
  value: string
  onChange: (value: string) => void
  children: ReactNode
  disabled?: boolean
  'aria-label'?: string
}

/**
 * A themed dropdown. The native control is kept (so keyboard, typeahead, and the OS option list
 * all keep working) but its default chrome is removed and replaced with the builder's own frame
 * and chevron, so it reads as part of the same form as the inputs.
 */
export function Select({ value, onChange, children, disabled, ...rest }: SelectProps) {
  return (
    <div className={`select-wrap${disabled ? ' select-wrap-disabled' : ''}`}>
      <select
        className="select"
        value={value}
        disabled={disabled}
        aria-label={rest['aria-label']}
        onChange={(e) => onChange(e.target.value)}
      >
        {children}
      </select>
      <span className="select-chevron" aria-hidden="true">
        ▾
      </span>
    </div>
  )
}
