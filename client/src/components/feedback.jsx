import { createContext, useCallback, useContext, useRef, useState } from 'react'
import { Modal, Button } from './ui'

// In-app toasts + confirm dialog, so nothing uses the browser's
// "localhost says…" alert / confirm / prompt boxes.

const FeedbackContext = createContext(null)

export function FeedbackProvider({ children }) {
  const [toasts, setToasts] = useState([])
  const [dialog, setDialog] = useState(null)
  const resolver = useRef(null)

  const toast = useCallback((message, tone = 'info') => {
    const id = Math.random().toString(36).slice(2)
    setToasts((t) => [...t, { id, message, tone }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4000)
  }, [])

  const api = {
    toast,
    success: (m) => toast(m, 'success'),
    error: (m) => toast(m, 'error'),
    info: (m) => toast(m, 'info'),

    // confirm({ title, message, confirmText, danger }) -> Promise<boolean>
    confirm: (opts) =>
      new Promise((resolve) => {
        resolver.current = resolve
        setDialog({ kind: 'confirm', confirmText: 'Confirm', ...opts })
      }),

    // promptText({ title, label, defaultValue, confirmText }) -> Promise<string | null>
    promptText: (opts) =>
      new Promise((resolve) => {
        resolver.current = resolve
        setDialog({ kind: 'prompt', confirmText: 'Save', value: opts.defaultValue ?? '', ...opts })
      }),
  }

  function finish(result) {
    const r = resolver.current
    resolver.current = null
    setDialog(null)
    r?.(result)
  }

  return (
    <FeedbackContext.Provider value={api}>
      {children}

      <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
        {toasts.map((t) => (
          <div
            key={t.id}
            className={`min-w-[220px] max-w-sm rounded-lg px-4 py-2.5 text-sm shadow-lg ring-1 ${
              t.tone === 'success' ? 'bg-green-600 text-white ring-green-700'
                : t.tone === 'error' ? 'bg-red-600 text-white ring-red-700'
                : 'bg-slate-800 text-white ring-slate-900'
            }`}
          >
            {t.message}
          </div>
        ))}
      </div>

      {dialog && (
        <Modal title={dialog.title} onClose={() => finish(dialog.kind === 'prompt' ? null : false)}>
          {dialog.message && <p className="text-sm text-slate-600">{dialog.message}</p>}

          {dialog.kind === 'prompt' && (
            <label className="mt-3 block text-sm">
              {dialog.label && <span className="mb-1 block font-medium text-slate-700">{dialog.label}</span>}
              <input
                autoFocus
                className="input"
                value={dialog.value}
                onChange={(e) => setDialog({ ...dialog, value: e.target.value })}
                onKeyDown={(e) => { if (e.key === 'Enter') finish(dialog.value) }}
              />
            </label>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <Button variant="ghost" onClick={() => finish(dialog.kind === 'prompt' ? null : false)}>Cancel</Button>
            <Button
              variant={dialog.danger ? 'danger' : 'primary'}
              onClick={() => finish(dialog.kind === 'prompt' ? dialog.value : true)}
            >
              {dialog.confirmText}
            </Button>
          </div>
        </Modal>
      )}
    </FeedbackContext.Provider>
  )
}

export function useFeedback() {
  const ctx = useContext(FeedbackContext)
  if (!ctx) throw new Error('useFeedback must be used inside <FeedbackProvider>')
  return ctx
}
