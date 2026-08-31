// One place that knows how auth state is persisted. The rest of the app goes
// through these helpers so swapping localStorage for something else is a
// single-file change.
const KEY = 'zkattendance.auth'

export function readAuth() {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

export function writeAuth(auth) {
  try {
    localStorage.setItem(KEY, JSON.stringify(auth))
  } catch {
    /* storage unavailable (private window, quota) — session stays in memory only */
  }
}

export function clearAuth() {
  try {
    localStorage.removeItem(KEY)
  } catch {
    /* ignore */
  }
}
