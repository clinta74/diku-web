import * as ToastPrimitive from '@radix-ui/react-toast'
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'

type Tone = 'good' | 'bad'

interface ToastMessage {
  id: number
  title: string
  tone: Tone
}

interface ToastApi {
  notify: (title: string, tone?: Tone) => void
}

const ToastContext = createContext<ToastApi | null>(null)

/** Fire a transient message. Replaces the ad-hoc `setSuccess(...) + setTimeout(...)` pattern. */
export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) {
    throw new Error('useToast must be used within <ToastProvider>')
  }
  return ctx
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [messages, setMessages] = useState<ToastMessage[]>([])

  const notify = useCallback((title: string, tone: Tone = 'good') => {
    // A monotonic-ish id; two toasts in the same millisecond still differ.
    setMessages((current) => [...current, { id: Date.now() + Math.random(), title, tone }])
  }, [])

  const api = useMemo<ToastApi>(() => ({ notify }), [notify])

  return (
    <ToastContext.Provider value={api}>
      <ToastPrimitive.Provider swipeDirection="right" duration={3000}>
        {children}

        {messages.map((message) => (
          <ToastPrimitive.Root
            key={message.id}
            className={`toast toast-${message.tone}`}
            onOpenChange={(open) => {
              if (!open) {
                setMessages((current) => current.filter((m) => m.id !== message.id))
              }
            }}
          >
            <ToastPrimitive.Title className="toast-title">{message.title}</ToastPrimitive.Title>
          </ToastPrimitive.Root>
        ))}

        <ToastPrimitive.Viewport className="toast-viewport" />
      </ToastPrimitive.Provider>
    </ToastContext.Provider>
  )
}
