import { useCalendar } from '../context/CalendarContext'

/**
 * Renders a date as dd/mm/yyyy in whichever calendar is active.
 *
 * Props:
 *   date      string | Date  — AD date (ISO 'yyyy-mm-dd' or Date)
 *   dateBs    string         — optional pre-computed BS string from the API
 *   both      boolean        — show BS and AD side by side
 *   suffix    boolean        — show the " BS" / " AD" tag (default true)
 *   fallback  string         — shown when there is no date
 */
export default function FormattedDate({
  date,
  dateBs,
  both = false,
  suffix = true,
  fallback = '—',
  className = '',
}) {
  const { formatDate, formatDateShort, formatDateBoth } = useCalendar()

  if (!date && !dateBs) {
    return <span className={`text-slate-400 ${className}`}>{fallback}</span>
  }

  const text = both
    ? formatDateBoth(date, dateBs)
    : suffix
      ? formatDate(date, dateBs)
      : formatDateShort(date, dateBs)

  return <span className={className}>{text}</span>
}
