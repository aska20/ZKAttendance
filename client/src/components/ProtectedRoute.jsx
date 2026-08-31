import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../context/AuthContext'

// Auth-only gate. Redirects to /login when signed out.
export default function ProtectedRoute({ children }) {
  const { isAuthenticated } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }
  return children
}

// Role gate for a single route. A mismatch shows an inline notice (never a
// redirect — redirecting to another gated route can loop).
export function RoleGate({ roles, children }) {
  const { role, username, signOut } = useAuth()

  if (roles && !roles.includes(role)) {
    return (
      <div className="flex min-h-full items-center justify-center p-6">
        <div className="max-w-sm space-y-3 rounded-xl bg-white p-8 text-center shadow-sm ring-1 ring-slate-200">
          <h1 className="text-lg font-semibold text-slate-900">Not authorised</h1>
          <p className="text-sm text-slate-500">
            Signed in as <span className="font-medium">{username}</span> ({role || 'unknown'}).
            This screen is for {roles.join(' / ')} accounts.
          </p>
          <button
            onClick={signOut}
            className="rounded-md bg-slate-800 px-3 py-2 text-sm font-medium text-white hover:bg-slate-700"
          >
            Sign in as a different user
          </button>
        </div>
      </div>
    )
  }
  return children
}
