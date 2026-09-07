import { FaStar } from 'react-icons/fa'
import { hm } from '../../lib/dates'

/**
 * Single attendance status cell in the grid.
 * Uses iconic format matching the status legend:
 *   - Present   → (✓) Green check circle
 *   - Absent    → (⊗) Red cross circle
 *   - Holiday   → ★ Amber star
 *   - Day Off   → 📅 Sky blue calendar
 *   - Half Day  → ◷ Red/amber clock
 */
export default function AttendanceCell({ cell, day, onClick }) {
  if (!cell) {
    return (
      <td className="border-l border-slate-100 px-2 py-2 text-center text-slate-300 text-xs">
        —
      </td>
    )
  }

  const isWeeklyOff =
    day?.isHoliday &&
    (day?.holidayName === 'Weekly off' || day?.holidayName?.toLowerCase().includes('off'))
  const isHoliday = day?.isHoliday && !isWeeklyOff

  // 1. Day Off (Weekly off / weekend off)
  if (isWeeklyOff || cell.status === 'Day Off' || cell.status === 'Weekly off') {
    return (
      <td className="border-l border-slate-100 px-1 py-1.5 text-center">
        <button
          type="button"
          onClick={onClick}
          className="inline-flex h-7 w-7 items-center justify-center rounded-lg text-sky-500 hover:bg-sky-50 hover:scale-115 transition-all cursor-pointer"
          title={`Day Off (${day?.holidayName || 'Weekly off'})`}
        >
          <svg
            className="h-4.5 w-4.5 text-sky-500 shrink-0"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <rect width="18" height="18" x="3" y="4" rx="2" />
            <path d="M16 2v4M8 2v4M3 10h18" />
          </svg>
        </button>
      </td>
    )
  }

  // 2. Holiday (Special holiday / festival)
  if (isHoliday || cell.status === 'Holiday') {
    return (
      <td className="border-l border-slate-100 px-1 py-1.5 text-center bg-amber-50/20">
        <button
          type="button"
          onClick={onClick}
          className="inline-flex h-7 w-7 items-center justify-center rounded-lg text-amber-500 hover:bg-amber-100/50 hover:scale-115 transition-all cursor-pointer"
          title={`Holiday: ${day?.holidayName || cell.holidayName || 'Holiday'}`}
        >
          <FaStar className="h-4 w-4 text-amber-400 drop-shadow-2xs shrink-0" />
        </button>
      </td>
    )
  }

  // 3. Absent (circle with cross)
  if (cell.status === 'Absent') {
    return (
      <td className="border-l border-slate-100 px-1 py-1.5 text-center">
        <button
          type="button"
          onClick={onClick}
          className="inline-flex h-7 w-7 items-center justify-center rounded-lg text-rose-500 hover:bg-rose-50 hover:scale-115 transition-all cursor-pointer"
          title="Absent — click for detail"
        >
          <svg
            className="h-4.5 w-4.5 text-rose-500 shrink-0"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.8"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="m15 9-6 6m0-6 6 6" />
          </svg>
        </button>
      </td>
    )
  }

  // 4. Present / Half Day
  const timeRange = hm(cell.firstIn) + (cell.lastOut ? '–' + hm(cell.lastOut) : '')

  // Half day (< 4 hours)
  if (cell.status === 'Half Day' || (cell.hours > 0 && cell.hours < 4)) {
    return (
      <td className="border-l border-slate-100 px-1 py-1.5 text-center">
        <button
          type="button"
          onClick={onClick}
          className="inline-flex h-7 w-7 items-center justify-center rounded-lg text-amber-500 hover:bg-amber-50 hover:scale-115 transition-all cursor-pointer"
          title={`Half Day (${timeRange}, ${cell.hours}h) — click for detail`}
        >
          <svg
            className="h-4.5 w-4.5 text-amber-500 shrink-0"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.9"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="M12 7v5l3 3" />
          </svg>
        </button>
      </td>
    )
  }

  // Standard Present (circle with checkmark)
  return (
    <td className="border-l border-slate-100 px-1 py-1.5 text-center">
      <button
        type="button"
        onClick={onClick}
        className="inline-flex h-7 w-7 items-center justify-center rounded-lg text-emerald-600 hover:bg-emerald-50 hover:scale-115 transition-all cursor-pointer"
        title={
          timeRange
            ? `Present (${timeRange}${cell.hours > 0 ? `, ${cell.hours}h` : ''}) — click for detail`
            : 'Present — click for detail'
        }
      >
        <svg
          className="h-4.5 w-4.5 text-emerald-600 shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="m8.5 12.5 2.5 2.5 4.5-5" />
        </svg>
      </button>
    </td>
  )
}
