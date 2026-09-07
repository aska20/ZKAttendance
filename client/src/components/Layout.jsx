import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import NotificationBell from './NotificationBell'

export default function Layout() {
  const { username, role, isManager, isAdmin, signOut } = useAuth()
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const location = useLocation()

  // Close mobile drawer on route change
  useEffect(() => {
    setMobileMenuOpen(false)
  }, [location.pathname])

  const navContent = (
    <>
      <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
        {isManager ? (
          <>
            <NavItem to="/" label="Dashboard" end />
            <NavItem to="/departments" label="Departments" />
            <NavItem to="/employees" label="Employees" end />
            {isAdmin && (
              <NavItem to="/employees/pending" label="Pending Approvals" />
            )}
            <NavItem to="/reports/daily" label="Daily Report" />
            <NavItem to="/attendance" label="Attendance" />
            <NavItem to="/reports/summary" label="Summary Report" />
            <NavItem to="/unregistered" label="Unregistered IDs" />
            <NavItem to="/holidays" label="Holidays" />
            <NavItem to="/branches" label="Branches" />
            <NavItem to="/devices" label="Devices" />
            <NavItem to="/errors" label="Error Log" />
          </>
        ) : (
          <NavItem to="/my-attendance" label="My Attendance" />
        )}

        <div className="my-2 border-t border-slate-800" />
        <NavItem to="/profile" label="Profile & Security" />
      </nav>
      <div className="shrink-0 border-t border-slate-800 px-5 py-4 text-xs text-slate-400">
        <div className="font-medium text-slate-200 truncate">{username}</div>
        <div className="truncate">{role}</div>
        <button
          type="button"
          onClick={signOut}
          className="mt-2 rounded bg-slate-800 px-2.5 py-1 text-xs font-medium text-slate-200 hover:bg-slate-700 hover:text-white transition"
        >
          Sign out
        </button>
      </div>
    </>
  )

  return (
    <div className="flex min-h-screen flex-col md:flex-row bg-slate-50">
      {/* ── Mobile Topbar (screen < md) ── */}
      <header className="sticky top-0 z-30 flex h-14 shrink-0 items-center justify-between border-b border-slate-800 bg-slate-900 px-4 text-slate-100 md:hidden">
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => setMobileMenuOpen(true)}
            className="rounded-lg p-1.5 text-slate-300 hover:bg-slate-800 hover:text-white transition"
            aria-label="Open menu"
          >
            <svg className="h-6 w-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>
          <span className="text-base font-semibold tracking-tight">
            ZK<span className="text-sky-400">Attendance</span>
          </span>
        </div>
        <div className="flex items-center gap-2">
          <NotificationBell />
        </div>
      </header>

      {/* ── Mobile Drawer Backdrop & Menu (screen < md) ── */}
      {mobileMenuOpen && (
        <div className="fixed inset-0 z-50 flex md:hidden">
          <div
            className="fixed inset-0 bg-slate-950/60 backdrop-blur-xs transition-opacity"
            onClick={() => setMobileMenuOpen(false)}
          />
          <div className="relative flex h-full w-64 max-w-[82vw] flex-1 flex-col bg-slate-900 text-slate-100 shadow-2xl">
            <div className="flex shrink-0 items-center justify-between px-5 py-4 border-b border-slate-800">
              <span className="text-lg font-semibold tracking-tight">
                ZK<span className="text-sky-400">Attendance</span>
              </span>
              <button
                type="button"
                onClick={() => setMobileMenuOpen(false)}
                className="rounded-lg p-1 text-slate-400 hover:bg-slate-800 hover:text-white transition"
                aria-label="Close menu"
              >
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
            {navContent}
          </div>
        </div>
      )}

      {/* ── Desktop Sidebar (screen >= md) ── */}
      <aside className="sticky top-0 hidden h-screen w-56 shrink-0 flex-col bg-slate-900 text-slate-100 md:flex z-30">
        <div className="flex shrink-0 items-center justify-between px-5 py-4">
          <span className="text-lg font-semibold tracking-tight">
            ZK<span className="text-sky-400">Attendance</span>
          </span>
          <NotificationBell />
        </div>
        {navContent}
      </aside>

      {/* ── Main Content Area ── */}
      <main className="min-w-0 flex-1 p-4 sm:p-6 lg:p-8">
        <Outlet />
      </main>
    </div>
  )
}

function NavItem({ to, label, end }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `block rounded-md px-3 py-2 text-sm font-medium transition ${
          isActive ? 'bg-sky-600 text-white' : 'text-slate-300 hover:bg-slate-800 hover:text-white'
        }`
      }
    >
      {label}
    </NavLink>
  )
}
