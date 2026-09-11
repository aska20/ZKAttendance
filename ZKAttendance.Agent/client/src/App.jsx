import { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Navigate, useNavigate } from 'react-router-dom'
import { auth, agent } from './api'
import Login from './pages/Login'
import Devices from './pages/Devices'
import Outbox from './pages/Outbox'
import Reports from './pages/Reports'

export default function App() {
  const [user, setUser] = useState(() => {
    const raw = sessionStorage.getItem('agent_user')
    return raw ? JSON.parse(raw) : null
  })
  const [status, setStatus] = useState(null)

  // Poll the link state so the header is honest about whether the central
  // server is reachable. Punches keep queueing either way.
  useEffect(() => {
    if (!user) return
    let alive = true
    const tick = () => agent.status().then((s) => alive && setStatus(s)).catch(() => {})
    tick()
    const id = setInterval(tick, 15000)
    return () => {
      alive = false
      clearInterval(id)
    }
  }, [user])

  if (!user) return <Login onLogin={setUser} />

  return (
    <div className="min-h-screen bg-slate-50">
      <Header user={user} status={status} onLogout={() => { sessionStorage.clear(); setUser(null) }} />
      <main className="mx-auto max-w-6xl px-4 py-6">
        <Routes>
          <Route path="/" element={<Navigate to="/devices" replace />} />
          <Route path="/devices" element={<Devices status={status} />} />
          <Route path="/outbox" element={<Outbox />} />
          <Route path="/reports" element={<Reports />} />
          <Route path="*" element={<Navigate to="/devices" replace />} />
        </Routes>
      </main>
    </div>
  )
}

function Header({ user, status, onLogout }) {
  const navigate = useNavigate()
  const pending = status?.outbox?.pending ?? 0

  return (
    <header className="border-b border-slate-200 bg-white">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-3">
        <button onClick={() => navigate('/devices')} className="text-left">
          <div className="text-base font-semibold text-slate-900">Attendance Agent</div>
          <div className="text-xs text-slate-500">
            {status?.serverName ? `${status.serverName} · ${status.branchName ?? ''}` : 'Local server'}
          </div>
        </button>

        <nav className="flex gap-1 text-sm font-medium">
          <Tab to="/devices">Devices</Tab>
          <Tab to="/outbox">
            Outbox
            {pending > 0 && (
              <span className="ml-1.5 rounded-full bg-amber-500 px-1.5 py-0.5 text-[10px] font-bold text-white">
                {pending}
              </span>
            )}
          </Tab>
          <Tab to="/reports">Reports</Tab>
        </nav>

        <div className="ml-auto flex items-center gap-3">
          <ConnectionPill status={status} />
          <span className="hidden text-xs text-slate-500 sm:inline">{user.username}</span>
          <button onClick={onLogout} className="text-xs font-medium text-slate-500 hover:text-rose-600">
            Sign out
          </button>
        </div>
      </div>
    </header>
  )
}

function Tab({ to, children }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        `rounded-lg px-3 py-1.5 transition ${
          isActive ? 'bg-sky-50 text-sky-700' : 'text-slate-600 hover:bg-slate-100'
        }`
      }
    >
      {children}
    </NavLink>
  )
}

function ConnectionPill({ status }) {
  if (!status) return <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-500">checking</span>

  if (!status.configured)
    return <span className="rounded-full bg-rose-50 px-2 py-0.5 text-xs font-semibold text-rose-700">not configured</span>

  return status.connected ? (
    <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-semibold text-emerald-700">connected</span>
  ) : (
    <span className="rounded-full bg-rose-50 px-2 py-0.5 text-xs font-semibold text-rose-700">offline</span>
  )
}
