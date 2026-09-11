import { useEffect, useState } from 'react'
import { agent, errorText, dt } from '../api'

/**
 * A read-only check against App1. The agent holds no attendance of its own, so
 * these numbers come from the central server, which stays the source of truth.
 */
export default function Reports() {
  const today = new Date().toISOString().slice(0, 10)
  const [date, setDate] = useState(today)
  const [data, setData] = useState(null)
  const [error, setError] = useState('')

  useEffect(() => {
    setError('')
    setData(null)
    agent.summary(date).then(setData).catch((e) => setError(errorText(e)))
  }, [date])

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <label className="text-sm font-medium text-slate-700">Date</label>
        <input
          type="date"
          value={date}
          max={today}
          onChange={(e) => setDate(e.target.value)}
          className="input w-44 tabular-nums"
        />
      </div>

      {error && (
        <p className="rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700 ring-1 ring-rose-200">{error}</p>
      )}

      {!data && !error && <p className="text-sm text-slate-400">Loading...</p>}

      {data && (
        <>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Stat label="Punches" value={data.totalPunches} />
            <Stat label="Employees" value={data.distinctEmployees} />
            <Stat label="Unmapped" value={data.unmapped} tone={data.unmapped ? 'rose' : undefined} />
            <Stat label="Last punch" value={data.lastPunchAt ? dt(data.lastPunchAt).slice(-5) : '\u2014'} />
          </div>

          {data.unmapped > 0 && (
            <p className="rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-900 ring-1 ring-amber-200">
              {data.unmapped} punch(es) came from enrol numbers nobody is mapped to. Fix the mapping
              in the central app so they attribute correctly.
            </p>
          )}

          <p className="text-xs text-slate-400">
            Figures come from the central server. Full reports live there.
          </p>
        </>
      )}
    </div>
  )
}

function Stat({ label, value, tone }) {
  return (
    <div className="card p-4">
      <div className="text-[11px] font-medium uppercase tracking-wide text-slate-400">{label}</div>
      <div
        className={`mt-1 text-2xl font-semibold tabular-nums ${
          tone === 'rose' ? 'text-rose-600' : 'text-slate-800'
        }`}
      >
        {value}
      </div>
    </div>
  )
}
