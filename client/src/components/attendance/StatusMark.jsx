/**
 * Attendance register notation.
 *
 * The grid used to mix an amber star, a calendar glyph and two circle icons,
 * which read as decoration rather than data. This uses the notation an actual
 * muster roll uses — a single letter per day — so a column can be scanned
 * down and counted by eye:
 *
 *   P   present            ½   half day
 *   A   absent             H   holiday (festival / declared)
 *   O   weekly off         ·   not due yet (today or a future day)
 *   —   before joining
 */

export const STATUS_META = {
  Present: {
    mark: 'P',
    label: 'Present',
    text: 'text-emerald-700',
    bg: 'bg-emerald-50',
    ring: 'ring-emerald-200',
    hover: 'hover:bg-emerald-100',
  },
  'Half Day': {
    mark: '½',
    label: 'Half day',
    text: 'text-amber-700',
    bg: 'bg-amber-50',
    ring: 'ring-amber-200',
    hover: 'hover:bg-amber-100',
  },
  Absent: {
    mark: 'A',
    label: 'Absent',
    text: 'text-rose-700',
    bg: 'bg-rose-50',
    ring: 'ring-rose-200',
    hover: 'hover:bg-rose-100',
  },
  Holiday: {
    mark: 'H',
    label: 'Holiday',
    text: 'text-violet-700',
    bg: 'bg-violet-50',
    ring: 'ring-violet-200',
    hover: 'hover:bg-violet-100',
  },
  'Day Off': {
    mark: 'O',
    label: 'Weekly off',
    text: 'text-slate-500',
    bg: 'bg-slate-100',
    ring: 'ring-slate-200',
    hover: 'hover:bg-slate-200',
  },
  Upcoming: {
    mark: '·',
    label: 'Not due yet',
    text: 'text-slate-300',
    bg: 'bg-white',
    ring: 'ring-slate-100',
    hover: 'hover:bg-slate-50',
  },
  'Not Joined': {
    mark: '—',
    label: 'Before joining',
    text: 'text-slate-300',
    bg: 'bg-white',
    ring: 'ring-slate-100',
    hover: '',
  },
}

/** The backend sends a few aliases; fold them onto the canonical keys. */
export function normaliseStatus(status) {
  if (!status) return 'Upcoming'
  if (status === 'Weekly off' || status === 'WeeklyOff') return 'Day Off'
  if (status === 'Pending' || status === 'Future' || status === 'NotDue') return 'Upcoming'
  if (status === 'HalfDay') return 'Half Day'
  if (status === 'NotJoined') return 'Not Joined'
  return STATUS_META[status] ? status : 'Upcoming'
}

export function statusMeta(status) {
  return STATUS_META[normaliseStatus(status)]
}

/**
 * One letter in a rounded chip. `size` is 'sm' inside the dense grid and
 * 'md' in legends and detail panels.
 */
export default function StatusMark({ status, size = 'sm', className = '' }) {
  const meta = statusMeta(status)
  const dims = size === 'md' ? 'h-7 w-7 text-sm' : 'h-6.5 w-6.5 text-xs sm:h-7 sm:w-7 sm:text-sm'
  return (
    <span
      className={`inline-flex ${dims} items-center justify-center rounded-md font-bold tabular-nums ring-1 ring-inset ${meta.bg} ${meta.text} ${meta.ring} ${className}`}
    >
      {meta.mark}
    </span>
  )
}

/** The key that sits above the attendance grid. */
export function AttendanceLegend({ className = '' }) {
  const order = ['Present', 'Half Day', 'Absent', 'Holiday', 'Day Off', 'Upcoming']
  return (
    <div className={`mb-3.5 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs ${className}`}>
      <span className="text-sm font-semibold text-slate-800">Key</span>
      {order.map((key) => {
        const meta = STATUS_META[key]
        return (
          <span key={key} className="inline-flex items-center gap-1.5">
            <StatusMark status={key} />
            <span className="font-medium text-slate-600">{meta.label}</span>
          </span>
        )
      })}
    </div>
  )
}
