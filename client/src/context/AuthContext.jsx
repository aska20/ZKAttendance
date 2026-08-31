import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { readAuth, writeAuth, clearAuth } from '../api/tokenStore'
import { setAuthLostHandler } from '../api/client'
import { login as loginRequest, revoke } from '../api/auth'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [auth, setAuth] = useState(() => readAuth())
  const navigate = useNavigate()

  // When the API client gives up refreshing, drop the session and bounce
  // to the login screen.
  useEffect(() => {
    setAuthLostHandler(() => {
      setAuth(null)
      navigate('/login', { replace: true })
    })
  }, [navigate])

  async function signIn(username, password) {
    const data = await loginRequest(username, password)
    writeAuth(data)
    setAuth(data)
    return data
  }

  async function signOut() {
    await revoke(auth?.refreshToken)
    clearAuth()
    setAuth(null)
    navigate('/login', { replace: true })
  }

  const value = useMemo(
    () => ({
      auth,
      isAuthenticated: !!auth?.accessToken,
      role: auth?.role ?? null,
      isManager: auth?.role === 'Admin' || auth?.role === 'HR',
      isAdmin: auth?.role === 'Admin',
      username: auth?.username ?? null,
      signIn,
      signOut,
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [auth],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
