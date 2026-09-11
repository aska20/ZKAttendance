import { useCallback, useEffect, useState } from 'react'
import { agent, errorText, dt } from '../api'

const TABS = ['Pending', 'Sent', 'Dead']

/**
 * The outgoing tray. Punches land here first and a worker drains them, so a
 * dropped link never loses a scan.
 */
export default function Outbox() {
  const [tab, setTab] = useState('Pending')
  const [data, setData] = useState(null)
  const [busy, setBusy] = useState('')
  const [note, setNote] = useState('')

  const load = useCallback(() => {
    agent.outbox(tab).then(setData).catch(() => setData({ rows: [] }))
  }, [tab])

  useEffect(() => {
    load()
    const id = setInterval(load, 10000)
    return () => clearInterval(id)
  }, [load])

  async function drain() {
    setBusy('drain')
    setNote('')
    try {
      const r = await agent.drain()
      setNote(`${r.pending} still waiting.`)
      load()
    } catch (e) {
      setNote(errorText(e))
    } finally {
      setBusy('')
    }
  }

  async function retry() {
    setBusy('retry')
    try {
      const r = await agent.retryDead()
      setNote(`${r.requeued} put back in the queue.`)
      load()
    } finally {
      setBusy('')
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-3 gap-3">
        <Stat label="Waiting" value={data?.pending ?? 0} tone="amber" />
        <Stat label="Sent" value={data?.sent ?? 0} tone="emerald" />
        <Stat label="Failed" value={data?.dead ?? 0} tone="rose" />
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div className="inline-flex rounded-lg bg-slate-100 p-0.5 text-sm font-medium">
          {TABS.map((t) => (
            <button
              key={t}
              onClick={() => setTab(t)}
              className={`rounded px-3 py-1.5 transition ${
                tab === t ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500'
              }`}
            >
              {t}
            </button>
          ))}
        </div>

        <button className="btn" onClick={drain} disabled={Boolean(busy)}>
          {busy === 'drain' ? 'Sending...' : 'Send now'}
        </button>
        <button className="btn-ghost" onClick={retry} disabled={Boolean(busy) || !data?.dead}>
          Retry failed
        </button>
        <span className="text-xs text-slate-500">{note || 'Sends automatically every 20 seconds.'}</span>
      </div>

      <section className="card overflow-hidden">
        {!data ? (
          <p className="py-12 text-center text-sm text-slate-400">Loading...</p>
        ) : data.rows.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            {tab === 'Pending' ? 'Nothing waiting. Everything has been sent.' : `No ${tab.toLowerCase()} rows.`}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Enrol no.</th>
                  <th className="px-4 py-2.5 font-medium">Punch time</th>
                  <th className="px-4 py-2.5 font-medium">Device</th>
                  <th className="px-4 py-2.5 font-medium">Tries</th>
                  <th className="px-4 py-2.5 font-medium">Detail</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.rows.map((r) => (
                  <tr key={r.outboxId}>
                    <td className="px-4 py-2.5 font-mono text-xs">{r.biometricUserId}</td>
                    <td className="px-4 py-2.5 tabular-nums">{dt(r.punchTime)}</td>
                    <td className="px-4 py-2.5 text-slate-500">{r.deviceId}</td>
                    <td className="px-4 py-2.5 tabular-nums text-slate-500">{r.attempts}</td>
                    <td className="px-4 py-2.5 text-xs text-slate-500">
                      {r.lastError || (r.sentAt ? `sent ${dt(r.sentAt)}` : '\u2014')}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}

function Stat({ label, value, tone }) {
  const colour = { amber: 'text-amber-600', emerald: 'text-emerald-700', rose: 'text-rose-600' }[tone]
  return (
    <div className="card p-4">
      <div className="text-[11px] font-medium uppercase tracking-wide text-slate-400">{label}</div>
      <div className={`mt-1 text-2xl font-semibold tabular-nums ${value ? colour : 'text-slate-800'}`}>
        {value}
      </div>
    </div>
  )
}
