// ─────────────────────────────────────────────────────────────────────────────
// One date format across the whole app: dd/mm/yyyy.
//
// ISO (yyyy-mm-dd) is used ONLY when talking to the API. Anything a human
// reads goes through dmy() / bsDmy() / dateTime() and comes out as dd/mm/yyyy.
// ─────────────────────────────────────────────────────────────────────────────

export const pad = (n) => String(n).padStart(2, '0')

export function toDate(value) {
  if (!value) return null
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value
  // 'yyyy-mm-dd' is parsed as UTC by Date(); force local so the day never shifts.
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [y, m, d] = value.split('-').map(Number)
    return new Date(y, m - 1, d)
  }
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? null : d
}

/** ISO yyyy-mm-dd — API wire format only, never shown to the user. */
export function ymd(value) {
  const d = toDate(value)
  if (!d) return ''
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** dd/mm/yyyy — the one display format. */
export function dmy(value) {
  const d = toDate(value)
  if (!d) return ''
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`
}

/** "2083-05-22" (BS, as the API sends it) → "22/05/2083". */
export function bsDmy(bsString) {
  if (!bsString) return ''
  const parts = String(bsString).split('-')
  if (parts.length !== 3) return String(bsString)
  const [y, m, d] = parts
  return `${pad(Number(d))}/${pad(Number(m))}/${y}`
}

/** 24-hour clock, "09:05". */
export function hm(value) {
  const d = toDate(value)
  if (!d) return ''
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** "07/09/2026 09:05" */
export function dateTime(value) {
  const d = dmy(value)
  const t = hm(value)
  return d ? `${d} ${t}` : '—'
}

/** 7.75 → "7h 45m". Keeps hour totals readable instead of "7.75". */
export function hoursText(hours) {
  const n = Number(hours)
  if (!Number.isFinite(n) || n <= 0) return '—'
  const h = Math.floor(n)
  const m = Math.round((n - h) * 60)
  if (m === 60) return `${h + 1}h 00m`
  return `${h}h ${pad(m)}m`
}

export function todayIso() {
  return ymd(new Date())
}

/** True when the date is strictly after today. Used to keep hire dates sane. */
export function isFutureDate(value) {
  const d = toDate(value)
  if (!d) return false
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  d.setHours(0, 0, 0, 0)
  return d.getTime() > today.getTime()
}

/** Sun..Sat, short. */
export const WEEKDAYS_SHORT = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
