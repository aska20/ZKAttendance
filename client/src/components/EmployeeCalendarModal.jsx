import { useEffect, useMemo, useState } from 'react'
import { overview } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { ymd, hm } from '../lib/dates'
import { Modal, Button } from './ui'
import DayDetailModal from './DayDetailModal'

const WD = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

export default function EmployeeCalendarModal({ employeeId, employeeName, onClose }) {
  const [month, setMonth] = useState(() => {
    const d = new Date()
    return { y: d.getFullYear(), m: d.getMonth() }
  })
  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [detail, setDetail] = useState(null)

  const first = useMemo(() => new Date(month.y, month.m, 1), [month])
  const daysInMonth = new Date(month.y, month.m + 1, 0).getDate()

  useEffect(() => {
    setError(''); setData(null)
    overview.get({
      from: ymd(new Date(month.y, month.m, 1)),
      to: ymd(new Date(month.y, month.m + 1, 0)),
      employeeId,
    })
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
  }, [month, employeeId])

  const emp = data?.departments?.[0]?.employees?.[0]
  const cellByIso = emp?.cells || {}
  const dayMeta = useMemo(() => {
    const m = {}
    for (const d of data?.days || []) m[d.dateIso] = d
    return m
  }, [data])

  const cells = []
  for (let i = 0; i < first.getDay(); i++) cells.push(null)
  for (let d = 1; d <= daysInMonth; d++) cells.push(d)

  const monthLabel = first.toLocaleString('default', { month: 'long', year: 'numeric' })
  const bsLabel = data ? `${data.fromBs} – ${data.toBs} BS` : ''

  const tone = (s) => s === 'Present' ? 'bg-green-50 border-green-200 text-green-700'
    : s === 'Absent' ? 'bg-red-50 border-red-200 text-red-600'
    : s === 'Holiday' ? 'bg-sky-50 border-sky-200 text-sky-600'
    : 'border-slate-200 text-slate-300'

  return (
    <Modal title={`${employeeName} — attendance calendar`} onClose={onClose} wide>
      <div className="mb-3 flex items-center justify-between">
        <Button variant="secondary" onClick={() => setMonth(({ y, m }) => m === 0 ? { y: y - 1, m: 11 } : { y, m: m - 1 })}>← Prev</Button>
        <div className="text-center">
          <div className="font-semibold text-slate-800">{monthLabel}</div>
          <div className="text-xs text-slate-400">{bsLabel}</div>
        </div>
        <Button variant="secondary" onClick={() => setMonth(({ y, m }) => m === 11 ? { y: y + 1, m: 0 } : { y, m: m + 1 })}>Next →</Button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {!data && !error && <p className="text-sm text-slate-500">Loading…</p>}

      {data && (
        <>
          <div className="mb-3 flex gap-4 text-xs text-slate-500">
            <span><b className="text-green-600">{emp?.presentDays ?? 0}</b> present</span>
            <span><b className="text-red-500">{emp?.absentDays ?? 0}</b> absent</span>
            <span><b>{emp?.totalDays ?? 0}</b> working days</span>
          </div>

          <div className="grid grid-cols-7 gap-1 text-center text-[11px] font-medium text-slate-400">
            {WD.map((w) => <div key={w} className="py-1">{w}</div>)}
          </div>
          <div className="grid grid-cols-7 gap-1">
            {cells.map((d, i) => {
              if (d === null) return <div key={i} />
              const iso = ymd(new Date(month.y, month.m, d))
              const c = cellByIso[iso]
              const meta = dayMeta[iso]
              const clickable = c && c.status === 'Present'
              return (
                <button
                  key={i}
                  disabled={!clickable}
                  onClick={() => clickable && setDetail({ date: iso, dateBs: meta?.dateBs })}
                  title={meta ? `${meta.dateBs} BS${meta.holidayName ? ` — ${meta.holidayName}${meta.holidayType && meta.holidayType !== 'Weekly off' ? ` (${meta.holidayType})` : ''}` : ''}` : iso}
                  className={`flex h-16 flex-col items-center justify-center rounded-md border text-xs ${tone(c?.status)} ${clickable ? 'hover:brightness-95' : ''}`}
                >
                  <span className="font-semibold">{d}</span>
                  {c?.status === 'Present' && (
                    <span className="text-[9px] leading-tight">{hm(c.firstIn)}{c.lastOut ? '–' + hm(c.lastOut) : ''}</span>
                  )}
                  {c?.status && c.status !== 'Present' && <span className="text-[9px]">{c.status}</span>}
                </button>
              )
            })}
          </div>
        </>
      )}

      {detail && (
        <DayDetailModal
          employeeId={employeeId}
          employeeName={employeeName}
          date={detail.date}
          dateBs={detail.dateBs}
          onClose={() => setDetail(null)}
        />
      )}
    </Modal>
  )
}
