import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getDashboardSummary } from '../api/dashboard'
import { apiErrorMessage } from '../lib/errors'
import { dateTime, hm } from '../lib/dates'

function Stat({ label, value }) {
  return (
    <div className="rounded-xl bg-white p-5 shadow-sm ring-1 ring-slate-200">
      <div className="text-sm text-slate-500">{label}</div>
      <div className="mt-1 text-2xl font-semibold text-slate-900">{value}</div>
    </div>
  )
}

export default function Dashboard() {
  const [data, setData] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    getDashboardSummary()
      .then(setData)
      .catch((err) => {
        if (err?.response?.status === 403) {
          setError('Your account is not allowed to view the dashboard (Admin/HR only).')
        } else {
          setError(apiErrorMessage(err, 'Could not reach the API'))
        }
      })
  }, [])

  if (error) return <p className="rounded-md bg-red-50 px-4 py-3 text-red-700 ring-1 ring-red-200">{error}</p>
  if (!data) return <p className="text-slate-500">Loading…</p>

  const { counters, today, recentLogs, lastSyncTime } = data

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold text-slate-900">Dashboard</h1>
        <p className="text-sm text-slate-500">
          {data.dateBs} BS &middot; {data.dateAd}
        </p>
      </div>

      {data.unregisteredCount > 0 && (
        <Link
          to="/employees"
          className="block rounded-xl border-l-4 border-l-amber-500 bg-amber-50 px-5 py-4 ring-1 ring-amber-200 hover:bg-amber-100"
        >
          <div className="font-semibold text-amber-900">
            {data.unregisteredCount} person{data.unregisteredCount > 1 ? 's are' : ' is'} enrolled on a device but not added to the system
          </div>
          <div className="mt-0.5 text-sm text-amber-800">
            Their punches are being stored but not counted. Add them from the Employees page →
          </div>
        </Link>
      )}

      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        <Stat label="Employees" value={counters.totalEmployees} />
        <Stat label="Branches" value={counters.totalBranches} />
        <Stat label="Devices" value={counters.totalDevices} />
        <Stat label="Active devices" value={counters.activeDevices} />
        <Stat label="Present today" value={today.present} />
        <Stat label="Absent today" value={today.absent} />
        <Stat label="Unattributed punches" value={today.unattributed ?? 0} />
        <Stat label="Late today" value={today.late} />
        <Stat
          label="Last sync"
          value={lastSyncTime ? dateTime(lastSyncTime) : '—'}
        />
      </div>

      <div className="rounded-xl bg-white shadow-sm ring-1 ring-slate-200">
        <div className="border-b border-slate-100 px-5 py-3 text-sm font-medium text-slate-700">
          Today&apos;s punches
        </div>
        {recentLogs.length === 0 ? (
          <p className="px-5 py-4 text-sm text-slate-500">No punches recorded yet today.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="text-left text-slate-500">
              <tr className="border-b border-slate-100">
                <th className="px-5 py-2 font-medium">Time</th>
                <th className="px-5 py-2 font-medium">Biometric ID</th>
                <th className="px-5 py-2 font-medium">Type</th>
                <th className="px-5 py-2 font-medium">Device</th>
                <th className="px-5 py-2 font-medium">Branch</th>
              </tr>
            </thead>
            <tbody>
              {recentLogs.map((l) => (
                <tr key={l.logId} className="border-b border-slate-50 last:border-0">
                  <td className="px-5 py-2">
                    {hm(l.punchTimeAd)}
                    <span className="ml-2 text-xs text-slate-400">{l.punchTimeBs} BS</span>
                  </td>
                  <td className="px-5 py-2">{l.biometricUserId}</td>
                  <td className="px-5 py-2">{l.attendanceType || '—'}</td>
                  <td className="px-5 py-2">{l.device || '—'}</td>
                  <td className="px-5 py-2">{l.branch || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
