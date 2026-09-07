import { useCalendar } from '../context/CalendarContext'
import { adToBs } from '../lib/nepaliCalendar'

/**
 * FormattedDate
 *
 * Renders a date consistently in either BS or AD based on global CalendarContext.
 *
 * Props:
 *   date: string | Date  - AD date ISO string (e.g. "2026-08-12") or Date object
 *   dateBs?: string      - Optional pre-computed BS string (e.g. "2083-04-28")
 *   fallback?: string    - What to show if date is null/empty (default: "—")
 *   className?: string   - Additional CSS classes
 */
export default function FormattedDate({ date, dateBs, fallback = '—', className = '' }) {
  const { isBs } = useCalendar()

  if (!date) {
    return <span className={`text-slate-400 ${className}`}>{fallback}</span>
  }

  // ISO date string yyyy-mm-dd
  const isoStr = typeof date === 'string' ? date.slice(0, 10) : new Date(date).toISOString().slice(0, 10)

  if (isBs) {
    const bsVal = dateBs || adToBs(isoStr)?.dateBs
    if (bsVal) {
      return <span className={className}>{bsVal} BS</span>
    }
  }

  return <span className={className}>{isoStr} AD</span>
}
