import { useId } from 'react'

export interface PickerOption {
  key: string
  name: string
}

interface Props {
  value: string
  onChange: (key: string) => void
  options: PickerOption[]
  placeholder?: string
  disabled?: boolean
  'aria-label'?: string
}

/**
 * A type-ahead picker over template keys, showing the key and its name together.
 *
 * A `<select>` was fine with a dozen templates and stops being fine well before a hundred: it
 * cannot be typed into, so finding `rusted-blade` means scrolling a list sorted by whatever order
 * the API returned. A native `<datalist>` gives filtering, keyboard navigation, and the browser's
 * own overlay for free — and unlike a hand-rolled combobox it needs no focus management to be
 * accessible.
 *
 * The option's *value* is the key, because the key is what gets stored; the label is the display
 * name, which is what a builder actually remembers. Browsers render both.
 *
 * Free text is allowed through deliberately. Content keys are wired before the thing they point
 * at exists (PLAN.md §7.4), so refusing an unknown key here would block a legitimate authoring
 * order — the reachability checks are where a dangling reference gets reported.
 */
export function TemplatePicker({
  value,
  onChange,
  options,
  placeholder,
  disabled,
  ...rest
}: Props) {
  const listId = useId()
  const known = options.some((o) => o.key === value)

  return (
    <>
      <input
        list={listId}
        className={value && !known ? 'picker-unknown' : undefined}
        value={value}
        disabled={disabled}
        placeholder={placeholder ?? 'type to search…'}
        spellCheck={false}
        aria-label={rest['aria-label']}
        onChange={(e) => onChange(e.target.value)}
      />
      <datalist id={listId}>
        {options.map((option) => (
          <option key={option.key} value={option.key}>
            {option.name || option.key}
          </option>
        ))}
      </datalist>
    </>
  )
}
