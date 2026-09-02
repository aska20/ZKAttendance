import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { notifications } from '../api/resources'
import { ymd, hm } from '../lib/dates'

export default function NotificationBell() {
  const [open, setOpen] = useState(false)
  const [count, setCount] = useState(0)
  const [items, setItems] = useState([])
  const rootRef = useRef(null)
  const navigate = useNavigate()

  async function refreshCount() {
    try {
      const { count } = await notifications.unreadCount()
      setCount(count)
    } catch { /* ignore */ }
  }

  useEffect(() => {
    refreshCount()
    const t = setInterval(refreshCount, 30000)
    return () => clearInterval(t)
  }, [])

  useEffect(() => {
    if (!open) return
    notifications.list({ take: 20 }).then(setItems).catch(() => {})
    function onDoc(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  async function openItem(n) {
    if (!n.isRead) {
      try { await notifications.markRead(n.notificationId) } catch { /* ignore */ }
    }
    setOpen(false)
    refreshCount()
    if (n.linkPath) navigate(n.linkPath)
  }

  async function markAll() {
    try { await notifications.markAllRead() } catch { /* ignore */ }
    setItems((xs) => xs.map((x) => ({ ...x, isRead: true })))
    setCount(0)
  }

  return (
    <div ref={rootRef} className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        className="relative rounded-md p-1.5 text-slate-300 hover:bg-slate-800 hover:text-white"
        title="Notifications"
      >
        <span className="text-lg leading-none">🔔</span>
        {count > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-bold text-white">
            {count > 9 ? '9+' : count}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute left-0 z-40 mt-2 w-80 rounded-lg bg-white text-slate-800 shadow-xl ring-1 ring-slate-200">
          <div className="flex items-center justify-between border-b border-slate-100 px-3 py-2 text-sm">
            <span className="font-semibold">Notifications</span>
            {items.some((n) => !n.isRead) && (
              <button onClick={markAll} className="text-xs text-sky-600 hover:underline">Mark all read</button>
            )}
          </div>
          <div className="max-h-96 overflow-y-auto">
            {items.length === 0 && <p className="px-3 py-6 text-center text-sm text-slate-400">Nothing yet.</p>}
            {items.map((n) => (
              <button
                key={n.notificationId}
                onClick={() => openItem(n)}
                className={`block w-full border-b border-slate-50 px-3 py-2.5 text-left text-sm last:border-0 hover:bg-slate-50 ${
                  n.isRead ? 'text-slate-500' : 'font-medium text-slate-800'
                }`}
              >
                <div className="flex items-start gap-2">
                  {!n.isRead && <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-sky-500" />}
                  <div className={n.isRead ? 'pl-4' : ''}>
                    <div>{n.message}</div>
                    <div className="mt-0.5 text-xs text-slate-400">
                      {ymd(n.createdDate)} {hm(n.createdDate)}
                    </div>
                  </div>
                </div>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
