import axios from 'axios'
import { readAuth, writeAuth, clearAuth } from './tokenStore'

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api',
  headers: { 'Content-Type': 'application/json' },
})

// Called by AuthContext so a forced logout can update React state / redirect.
let onAuthLost = () => {}
export function setAuthLostHandler(fn) {
  onAuthLost = fn
}

// ── Request: attach the current access token ───────────────────────────
api.interceptors.request.use((config) => {
  const auth = readAuth()
  if (auth?.accessToken) {
    config.headers.Authorization = `Bearer ${auth.accessToken}`
  }
  return config
})

// ── Response: on a 401, try the refresh token once, then replay ────────
let refreshing = null

async function refreshTokens() {
  const auth = readAuth()
  if (!auth?.refreshToken) throw new Error('no refresh token')

  // A bare axios call — not `api` — so this request skips the interceptors
  // and cannot recurse back into refresh handling.
  const { data } = await axios.post(
    `${import.meta.env.VITE_API_URL || '/api'}/Auth/refresh`,
    { refreshToken: auth.refreshToken },
    { headers: { 'Content-Type': 'application/json' } },
  )
  writeAuth(data)
  return data.accessToken
}

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error.config
    const status = error.response?.status

    if (status === 401 && !original._retried) {
      original._retried = true
      try {
        refreshing = refreshing || refreshTokens()
        const newToken = await refreshing
        refreshing = null
        original.headers.Authorization = `Bearer ${newToken}`
        return api(original)
      } catch {
        refreshing = null
        clearAuth()
        onAuthLost()
      }
    }

    return Promise.reject(error)
  },
)

export default api
