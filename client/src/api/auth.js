import api from './client'

// POST /api/Auth/login -> { accessToken, refreshToken, expiresAt, username, role }
export async function login(username, password) {
  const { data } = await api.post('/Auth/login', { username, password })
  return data
}

// Best-effort: tell the server to drop the refresh token. A failure here
// (already expired, network) should not stop the client-side logout.
export async function revoke(refreshToken) {
  if (!refreshToken) return
  try {
    await api.post('/Auth/revoke', { refreshToken })
  } catch {
    /* ignore */
  }
}
