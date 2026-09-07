import { useEffect, useMemo, useState } from 'react'
import { attendance } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { Modal, Badge } from './ui'
import { hm, hoursText, dmy, bsDmy } from '../lib/dates'
import { adToBs } from '../lib/nepaliCalendar'
import { useCalendar } from '../context/CalendarContext'
import StatusMark from './attendance/StatusMark'

/**
 * The whole day behind a single cell, laid out as a summary report:
 *
 *   1. A header band — who, which date (both calendars), and the verdict.
 *   2. Summary figures — first in, last out, time inside, number of scans.
 *   3. Sessions — the punches paired up into in/out spells.
 *   4. Every raw scan, in order, however many there were.
 *
 * The grid only ever shows the earliest check-in and the latest check-out.
 * Someone who steps out for lunch and scans four times still reads as one
 * clean row up there; the detail lives here.
 */
export default function DayDetailModal({ employeeId, employeeName, date, dateBs, onClose }) {
  const { isBs } = useCalendar()
  const [data, setData] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    setData(null)
    setError('')
    attendance
      .day(employeeId, date)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
  }, [employeeId, date])

  const bs = dateBs || adToBs(date)?.dateBs

  /**
   * Pair the scans into sessions. Terminals report in/out inconsistently
   * across models, so trusting attendanceType alone gives wrong totals.
   * Falling back to strict alternation (in, out, in, out...) is what a payroll
   * clerk would do by hand, and it matches what the grid already counts.
   */
  const sessions = useMemo(() => {
    const punches = data?.punches ?? []
    if (punches.length === 0) return []

    const typed = punches.filter((p) => {
      const t = (p.attendanceType || '').toLowerCase()
      return t.includes('in') || t.includes('out')
    })
    const useTypes = typed.length === punches.length

    const out = []
    let open = null

    for (const p of punches) {
      const t = (p.attendanceType || '').toLowerCase()
      const isOut = useTypes ? t.includes('out') && !t.includes('in') : open !== null

      if (isOut && open) {
        out.push({ in: open, out: p })
        open = null
      } else if (!isOut) {
        if (open) out.push({ in: open, out: null }) // two check-ins in a row
        open = p
      }
    }
    if (open) out.push({ in: open, out: null })
    return out
  }, [data])

  const workedHours = useMemo(
    () =>
      sessions.reduce((sum, s) => {
        if (!s.out) return sum
        const ms = new Date(s.out.time) - new Date(s.in.time)
        return ms > 0 ? sum + ms / 3600000 : sum
      }, 0),
    [sessions],
  )

  const spanHours = useMemo(() => {
    if (!data?.firstIn || !data?.lastOut) return 0
    const ms = new Date(data.lastOut) - new Date(data.firstIn)
    return ms > 0 ? ms / 3600000 : 0
  }, [data])

  const status = !data
    ? null
    : data.count === 0
      ? 'Absent'
      : workedHours > 0 && workedHours < 4
        ? 'Half Day'
        : 'Present'

  return (
    <Modal
      title={
        <div className="min-w-0">
          <div className="truncate text-base font-semibold text-slate-900 sm:text-lg">{employeeName}</div>
          <div className="mt-0.5 text-xs font-normal text-slate-500">
            {isBs ? `${bsDmy(bs)} BS · ${dmy(date)} AD` : `${dmy(date)} AD · ${bsDmy(bs)} BS`}
          </div>
        </div>
      }
      onClose={onClose}
      wide
    >
      {error && <p className="text-sm text-red-600">{error}</p>}
      {!data && !error && <p className="text-sm text-slate-500">Loading...</p>}

      {data && (
        <div className="space-y-5">
          {/* Verdict */}
          <div className="flex items-center gap-3 rounded-xl bg-slate-50 px-4 py-3 ring-1 ring-slate-200">
            <StatusMark status={status} size="md" />
            <div className="min-w-0">
              <div className="text-sm font-semibold text-slate-900">
                {status === 'Absent' ? 'Marked absent' : status === 'Half Day' ? 'Half day' : 'Present'}
              </div>
              <div className="text-xs text-slate-500">
                {data.count === 0
                  ? 'No check-in or check-out was recorded on any device.'
                  : `${data.count} scan${data.count > 1 ? 's' : ''} across ${sessions.length} session${sessions.length > 1 ? 's' : ''}.`}
              </div>
            </div>
          </div>

          {/* Summary figures */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Figure label="First check-in" value={data.firstIn ? hm(data.firstIn) : '—'} tone="emerald" />
            <Figure label="Last check-out" value={data.lastOut ? hm(data.lastOut) : '—'} tone="sky" />
            <Figure label="Time inside" value={workedHours > 0 ? hoursText(workedHours) : '—'} />
            <Figure
              label="First to last"
              value={spanHours > 0 ? hoursText(spanHours) : '—'}
              hint={spanHours > workedHours + 0.02 ? 'includes breaks' : undefined}
            />
          </div>

          {/* Sessions */}
          {sessions.length > 0 && (
            <section>
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">Sessions</h3>
              <div className="overflow-hidden rounded-lg ring-1 ring-slate-200">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 text-left text-xs text-slate-500">
                    <tr>
                      <th className="px-3 py-2 font-medium">#</th>
                      <th className="px-3 py-2 font-medium">In</th>
                      <th className="px-3 py-2 font-medium">Out</th>
                      <th className="px-3 py-2 text-right font-medium">Duration</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {sessions.map((s, i) => {
                      const mins = s.out ? (new Date(s.out.time) - new Date(s.in.time)) / 60000 : null
                      return (
                        <tr key={i}>
                          <td className="px-3 py-2 text-slate-400">{i + 1}</td>
                          <td className="px-3 py-2 font-medium tabular-nums text-emerald-700">{hm(s.in.time)}</td>
                          <td className="px-3 py-2 font-medium tabular-nums text-sky-700">
                            {s.out ? hm(s.out.time) : <span className="text-amber-600">still in</span>}
                          </td>
                          <td className="px-3 py-2 text-right tabular-nums text-slate-600">
                            {mins != null ? hoursText(mins / 60) : '—'}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="bg-slate-50 text-sm font-semibold text-slate-800">
                    <tr>
                      <td colSpan={3} className="px-3 py-2 text-right">Total</td>
                      <td className="px-3 py-2 text-right tabular-nums">{hoursText(workedHours)}</td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            </section>
          )}

          {/* Every scan */}
          {data.count > 0 && (
            <section>
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
                All scans ({data.count})
              </h3>
              <div className="overflow-x-auto rounded-lg ring-1 ring-slate-200">
                <table className="w-full text-sm">
                  <thead className="bg-slate-50 text-left text-xs text-slate-500">
                    <tr>
                      <th className="px-3 py-2 font-medium">#</th>
                      <th className="px-3 py-2 font-medium">Time</th>
                      <th className="px-3 py-2 font-medium">Type</th>
                      <th className="px-3 py-2 font-medium">Method</th>
                      <th className="px-3 py-2 font-medium">Device</th>
                      <th className="px-3 py-2 font-medium">Source</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {data.punches.map((p, i) => (
                      <tr key={p.logId}>
                        <td className="px-3 py-2 text-slate-400">{i + 1}</td>
                        <td className="px-3 py-2 font-medium tabular-nums">{hm(p.time)}</td>
                        <td className="px-3 py-2 text-slate-600">{p.attendanceType || '—'}</td>
                        <td className="px-3 py-2 text-slate-600">{p.verifyMethod || '—'}</td>
                        <td className="px-3 py-2 text-slate-600">{p.device || '—'}</td>
                        <td className="px-3 py-2">
                          <Badge tone={p.isManual ? 'sky' : 'slate'}>{p.isManual ? 'Manual' : 'Device'}</Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {data.count === 0 && (
            <p className="rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-800 ring-1 ring-rose-200">
              No scan was recorded on this working day, so it counts as <b>absent</b>. If the person
              was in and a device was down, add the punch by hand from the attendance screen — the
              entry stays flagged as manual.
            </p>
          )}
        </div>
      )}
    </Modal>
  )
}

function Figure({ label, value, hint, tone = 'slate' }) {
  const toneClass = {
    emerald: 'text-emerald-700',
    sky: 'text-sky-700',
    slate: 'text-slate-800',
  }[tone]
  return (
    <div className="rounded-lg bg-white px-3 py-2.5 ring-1 ring-slate-200">
      <div className="text-[11px] font-medium uppercase tracking-wide text-slate-400">{label}</div>
      <div className={`mt-0.5 text-lg font-semibold tabular-nums ${toneClass}`}>{value}</div>
      {hint && <div className="text-[10px] text-slate-400">{hint}</div>}
    </div>
  )
}
