import * as AlertDialog from '@radix-ui/react-alert-dialog'
import type { ReactNode } from 'react'

interface ConfirmDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description?: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  /** Renders the confirm button in the danger style; use for deletes. */
  destructive?: boolean
  busy?: boolean
  /**
   * Runs the action. The dialog is NOT auto-closed - the caller closes it via `onOpenChange`
   * after the async work settles, so the button can show a busy state and stay put on error.
   */
  onConfirm: () => void
}

/**
 * A typed confirmation over Radix AlertDialog, replacing the four native `confirm()` calls.
 * AlertDialog (not Dialog) is deliberate: it traps focus on the safest action and announces
 * as an alert, which is the right semantics for a destructive prompt.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  destructive,
  busy,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <AlertDialog.Root open={open} onOpenChange={onOpenChange}>
      <AlertDialog.Portal>
        <AlertDialog.Overlay className="dlg-overlay" />
        <AlertDialog.Content className="dlg dlg-confirm" aria-describedby={undefined}>
          <AlertDialog.Title className="dlg-title">{title}</AlertDialog.Title>
          {description && (
            <AlertDialog.Description className="dlg-desc">{description}</AlertDialog.Description>
          )}

          <div className="dlg-footer">
            <AlertDialog.Cancel asChild>
              <button type="button" disabled={busy}>
                {cancelLabel}
              </button>
            </AlertDialog.Cancel>
            <button
              type="button"
              className={destructive ? 'danger-button' : 'primary'}
              disabled={busy}
              onClick={onConfirm}
            >
              {busy ? 'Working…' : confirmLabel}
            </button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  )
}
