import { useEffect, useState } from 'react'
import { attendance } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { Modal, Badge } from './ui'
import { hm } from '../lib/dates'

// Every raw punch behind one "Present" cell — however many times the person
// scanned. The overview shows first-in / last-out; this shows the whole trail.
export default function DayDetailModal({ employeeId, employeeName, date, dateBs, onClose }) {
  const [data, setData] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    attendance.day(employeeId, date)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
  }, [employeeId, date])

  return (
    <Modal title={`${employeeName}, ${dateBs ? dateBs + ' BS (' + date + ')' : date}`} onClose={onClose} wide>
      {error && <p className="text-sm text-red-600">{error}</p>}
      {!data && !error && <p className="text-sm text-slate-500">Loading…</p>}
      {data && (
        <>
          <div className="mb-3 flex gap-4 text-sm">
            <span><span className="text-slate-500">Scans:</span> <b>{data.count}</b></span>
            <span><span className="text-slate-500">First in:</span> <b>{data.firstIn ? hm(data.firstIn) : '—'}</b></span>
            <span><span className="text-slate-500">Last out:</span> <b>{data.lastOut ? hm(data.lastOut) : '—'}</b></span>
          </div>
          {data.count === 0 ? (
            <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 ring-1 ring-red-200">
              No check-in or check-out. Marked <b>Absent</b> for this day.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="text-left text-slate-500">
                  <tr className="border-b border-slate-100">
                    <th className="py-2 pr-4 font-medium">#</th>
                    <th className="py-2 pr-4 font-medium">Time</th>
                    <th className="py-2 pr-4 font-medium">Type</th>
                    <th className="py-2 pr-4 font-medium">Method</th>
                    <th className="py-2 pr-4 font-medium">Device</th>
                    <th className="py-2 pr-4 font-medium">Source</th>
                  </tr>
                </thead>
                <tbody>
                  {data.punches.map((p, i) => (
                    <tr key={p.logId} className="border-b border-slate-50 last:border-0">
                      <td className="py-2 pr-4 text-slate-400">{i + 1}</td>
                      <td className="py-2 pr-4 font-medium">{hm(p.time)}</td>
                      <td className="py-2 pr-4">{p.attendanceType || '—'}</td>
                      <td className="py-2 pr-4">{p.verifyMethod || '—'}</td>
                      <td className="py-2 pr-4">{p.device || '—'}</td>
                      <td className="py-2 pr-4">
                        <Badge tone={p.isManual ? 'sky' : 'slate'}>{p.isManual ? 'Manual' : 'Device'}</Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </Modal>
  )
}
