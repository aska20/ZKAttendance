// One date format across the whole app: year-month-day, BS first then AD.
// The API returns both (dateBs + an ISO ad string) almost everywhere.

function ymd(value) {
  if (!value) return ''
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return String(value)
  const p = (n) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

function hm(value) {
  if (!value) return ''
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return ''
  const p = (n) => String(n).padStart(2, '0')
  return `${p(d.getHours())}:${p(d.getMinutes())}`
}

// "2026-08-31 17:40" — year-month-day, 24h
export function dateTime(value) {
  const d = ymd(value)
  const t = hm(value)
  return d ? `${d} ${t}` : '—'
}

export { ymd, hm }
