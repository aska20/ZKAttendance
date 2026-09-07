import StatusMark, { normaliseStatus, statusMeta } from './StatusMark'
import { hm, hoursText } from '../../lib/dates'

/**
 * One day for one employee in the attendance grid.
 *
 * The status comes from the server, which is also where the "don't mark a day
 * absent before it has happened" rule lives — a future day arrives as
 * "Upcoming", not "Absent", so nothing here has to guess.
 */
export default function AttendanceCell({ cell, day, onClick }) {
  if (!cell) {
    return (
      <td className="border-l border-slate-100 px-2 py-2 text-center text-xs text-slate-300">—</td>
    )
  }

  // Weekly off arrives either as its own status or as a holiday named "Weekly off".
  const weeklyOffByName =
    day?.isHoliday && (day?.holidayName === 'Weekly off' || day?.holidayType === 'Weekly off')

  const status = weeklyOffByName && cell.status === 'Holiday' ? 'Day Off' : normaliseStatus(cell.status)
  const meta = statusMeta(status)

  const timeRange =
    cell.firstIn ? `${hm(cell.firstIn)}${cell.lastOut ? ` – ${hm(cell.lastOut)}` : ''}` : ''

  const title = (() => {
    switch (status) {
      case 'Present':
      case 'Half Day': {
        const parts = [meta.label]
        if (timeRange) parts.push(timeRange)
        if (cell.hours > 0) parts.push(hoursText(cell.hours))
        if (cell.punchCount > 2) parts.push(`${cell.punchCount} scans`)
        return `${parts.join(' · ')} — click for the full day`
      }
      case 'Absent':
        return 'Absent — no scan recorded. Click for detail.'
      case 'Holiday':
        return `Holiday: ${day?.holidayName || cell.holidayName || 'Holiday'}`
      case 'Day Off':
        return `Weekly off (${day?.holidayName || 'Saturday'})`
      case 'Upcoming':
        return 'Not due yet — this day has not finished'
      case 'Not Joined':
        return 'Before this employee joined'
      default:
        return meta.label
    }
  })()

  const interactive = status !== 'Upcoming' && status !== 'Not Joined'

  return (
    <td className="border-l border-slate-100 px-1 py-1.5 text-center">
      <button
        type="button"
        onClick={interactive ? onClick : undefined}
        disabled={!interactive}
        title={title}
        className={`inline-flex flex-col items-center gap-0.5 rounded-lg px-1 py-0.5 transition ${
          interactive ? `cursor-pointer ${meta.hover}` : 'cursor-default'
        }`}
      >
        <StatusMark status={status} />
        {/* First in / last out under the mark, so the common case needs no click */}
        {timeRange && (
          <span className="hidden text-[9px] leading-none text-slate-400 sm:block">{hm(cell.firstIn)}</span>
        )}
      </button>
    </td>
  )
}
