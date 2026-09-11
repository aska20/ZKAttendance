import { useCallback, useEffect, useState } from 'react'
import { agent, errorText, dt } from '../api'

/**
 * The sync workflow, on one screen:
 *   choose device -> connect over the LAN -> read -> queue in the outbox
 * The posting to App1 is the outbox worker's job, not this page's.
 */
export default function Devices({ status }) {
  const [devices, setDevices] = useState(null)
  const [runs, setRuns] = useState([])
  const [error, setError] = useState('')
  const [syncing, setSyncing] = useState(null)
  const [result, setResult] = useState(null)

  const load = useCallback(() => {
    setError('')
    agent.devices().then((d) => setDevices(d.devices || [])).catch((e) => {
      setError(errorText(e))
      setDevices([])
    })
    agent.runs().then(setRuns).catch(() => setRuns([]))
  }, [])

  useEffect(load, [load])

  async function sync(device) {
    setSyncing(device.deviceId)
    setResult(null)
    try {
      const run = await agent.sync(device.deviceId)
      setResult(run)
      load()
    } catch (e) {
      setResult({ status: 'Failed', deviceName: device.deviceName, message: errorText(e) })
    } finally {
      setSyncing(null)
    }
  }

  if (status && !status.configured) {
    return (
      <div className="card p-6">
        <h2 className="text-sm font-semibold text-slate-900">Not configured</h2>
        <p className="mt-2 text-sm text-slate-600">
          Register this agent in the central app, then paste its key and secret into{' '}
          <code className="rounded bg-slate-100 px-1.5 py-0.5 text-xs">appsettings.json</code> and
          restart.
        </p>
        <pre className="mt-3 overflow-x-auto rounded-lg bg-slate-900 p-3 text-xs text-slate-100">
{`POST /api/LocalServers
{ "serverName": "Head office agent", "branchId": 1 }`}
        </pre>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {status && !status.connected && (
        <p className="rounded-lg bg-amber-50 px-4 py-3 text-sm text-amber-900 ring-1 ring-amber-200">
          Cannot reach the central server. You can still sync devices. Punches are saved locally and
          sent when the link returns.
        </p>
      )}

      {error && (
        <p className="rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700 ring-1 ring-rose-200">{error}</p>
      )}

      {result && (
        <div
          className={`rounded-lg px-4 py-3 text-sm ring-1 ${
            result.status === 'Success'
              ? 'bg-emerald-50 text-emerald-900 ring-emerald-200'
              : 'bg-rose-50 text-rose-800 ring-rose-200'
          }`}
        >
          <b>{result.deviceName}</b> — {result.message}
        </div>
      )}

      <section className="card overflow-hidden">
        <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
          <h2 className="text-sm font-semibold text-slate-900">Registered devices</h2>
          <button className="btn-ghost !px-2.5 !py-1 !text-xs" onClick={load}>
            Refresh
          </button>
        </div>

        {!devices ? (
          <p className="py-12 text-center text-sm text-slate-400">Loading...</p>
        ) : devices.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            No devices assigned to this agent in the central app.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Device</th>
                  <th className="px-4 py-2.5 font-medium">Address</th>
                  <th className="px-4 py-2.5 font-medium">Model</th>
                  <th className="px-4 py-2.5 font-medium">Role</th>
                  <th className="px-4 py-2.5 text-right font-medium">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {devices.map((d) => (
                  <tr key={d.deviceId} className="hover:bg-slate-50/70">
                    <td className="px-4 py-2.5 font-medium text-slate-800">{d.deviceName}</td>
                    <td className="px-4 py-2.5 tabular-nums text-slate-600">
                      {d.deviceIP}:{d.devicePort}
                    </td>
                    <td className="px-4 py-2.5 text-slate-500">{d.deviceModel || '\u2014'}</td>
                    <td className="px-4 py-2.5">
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs font-semibold ${
                          d.role === 'Master'
                            ? 'bg-sky-50 text-sky-700'
                            : 'bg-slate-100 text-slate-500'
                        }`}
                      >
                        {d.role}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      <button
                        className="btn !px-3 !py-1 !text-xs"
                        disabled={syncing === d.deviceId}
                        onClick={() => sync(d)}
                      >
                        {syncing === d.deviceId ? 'Syncing...' : 'Sync'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card overflow-hidden">
        <h2 className="border-b border-slate-100 px-4 py-3 text-sm font-semibold text-slate-900">
          Recent syncs
        </h2>
        {runs.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">
            Nothing yet. Press Sync on a device above.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  <th className="px-4 py-2.5 font-medium">Device</th>
                  <th className="px-4 py-2.5 font-medium">Started</th>
                  <th className="px-4 py-2.5 font-medium">Read</th>
                  <th className="px-4 py-2.5 font-medium">Queued</th>
                  <th className="px-4 py-2.5 font-medium">Result</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {runs.map((r) => (
                  <tr key={r.syncRunId}>
                    <td className="px-4 py-2.5 font-medium text-slate-800">{r.deviceName}</td>
                    <td className="px-4 py-2.5 tabular-nums text-slate-600">{dt(r.startedAt)}</td>
                    <td className="px-4 py-2.5 tabular-nums">{r.recordsRead}</td>
                    <td className="px-4 py-2.5 tabular-nums font-semibold text-sky-700">
                      {r.recordsQueued}
                    </td>
                    <td className="px-4 py-2.5">
                      <span
                        className={`rounded-full px-2 py-0.5 text-xs font-semibold ${
                          r.status === 'Success'
                            ? 'bg-emerald-50 text-emerald-700'
                            : r.status === 'Failed'
                              ? 'bg-rose-50 text-rose-700'
                              : 'bg-amber-50 text-amber-700'
                        }`}
                      >
                        {r.status}
                      </span>
                      <div className="mt-0.5 text-xs text-slate-400">{r.message}</div>
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
