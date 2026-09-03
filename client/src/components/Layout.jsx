import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'
import NotificationBell from './NotificationBell'

const managerNav = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/attendance', label: 'Attendance' },
  { to: '/employees', label: 'Employees' },
  { to: '/departments', label: 'Departments' },
  { to: '/branches', label: 'Branches' },
  { to: '/devices', label: 'Devices' },
  { to: '/holidays', label: 'Holidays' },
  { to: '/reports/daily', label: 'Daily Report' },
  { to: '/reports/summary', label: 'Summary Report' },
  { to: '/errors', label: 'Error Log' },
]

const employeeNav = [{ to: '/my-attendance', label: 'My Attendance' }]

export default function Layout() {
  const { username, role, isManager, isAdmin, signOut } = useAuth()
  const nav = isManager ? managerNav : employeeNav

  return (
    <div className="flex min-h-full">
      <aside className="flex w-56 shrink-0 flex-col bg-slate-900 text-slate-100">
        <div className="flex items-center justify-between px-5 py-4">
          <span className="text-lg font-semibold tracking-tight">
            ZK<span className="text-sky-400">Attendance</span>
          </span>
          <NotificationBell />
        </div>
        <nav className="flex-1 space-y-1 overflow-y-auto px-3 pb-4">
          {nav.map((item) => (
            <NavItem key={item.to} {...item} />
          ))}

          {isAdmin && (
            <>
              <div className="my-2 border-t border-slate-800" />
              <NavItem to="/employees/pending" label="Pending Approvals" />
            </>
          )}
          <div className="my-2 border-t border-slate-800" />
          <NavItem to="/profile" label="Profile & Security" />
        </nav>
        <div className="border-t border-slate-800 px-5 py-4 text-xs text-slate-400">
          <div className="font-medium text-slate-200">{username}</div>
          <div>{role}</div>
          <button
            onClick={signOut}
            className="mt-2 rounded bg-slate-800 px-2 py-1 text-slate-200 hover:bg-slate-700"
          >
            Sign out
          </button>
        </div>
      </aside>

      <main className="min-w-0 flex-1 overflow-x-auto p-8">
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
