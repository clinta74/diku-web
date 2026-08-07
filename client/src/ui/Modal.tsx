import * as Dialog from '@radix-ui/react-dialog'
import type { ReactNode } from 'react'

interface ModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  /** Optional supporting line under the title; doubles as the dialog's accessible description. */
  description?: string
  children: ReactNode
  /** Action row, typically a cancel + a confirm button. Rendered below the body. */
  footer?: ReactNode
}

/**
 * A portalled modal over Radix Dialog. The portal is the whole point: the old hand-rolled
 * dialog rendered inline inside the 15rem sidebar and was clipped by it. Radix also gives us
 * the focus trap, Escape-to-close, scroll lock, and backdrop that the old one lacked.
 */
export function Modal({ open, onOpenChange, title, description, children, footer }: ModalProps) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="dlg-overlay" />
        <Dialog.Content
          className="dlg"
          // Passing undefined opts out of Radix's description warning when we have none, and
          // lets its context wire the Description automatically when we do.
          aria-describedby={undefined}
        >
          <Dialog.Title className="dlg-title">{title}</Dialog.Title>
          {description && <Dialog.Description className="dlg-desc">{description}</Dialog.Description>}

          <div className="dlg-body">{children}</div>

          {footer && <div className="dlg-footer">{footer}</div>}

          <Dialog.Close asChild>
            <button type="button" className="dlg-close" aria-label="Close">
              ×
            </button>
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
