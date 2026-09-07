import { useCallback, useEffect, useRef, useState } from 'react'
import { MdFingerprint } from 'react-icons/md'
import { FiRefreshCw, FiCheckCircle, FiAlertTriangle, FiWifiOff } from 'react-icons/fi'
import { enrollment as api } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { Button, Badge } from './ui'
import { useFeedback } from './feedback'

/**
 * The bridge between "added in this system" and "known to the machine at the door".
 *
 * Adding a person here only ever wrote a database row, so their first scan
 * either failed or arrived as an unattributed punch. This panel drives the
 * whole sequence:
 *
 *   1. Create the user record on the terminals.
 *   2. Press "Register on ZKTeco" — the terminal switches to its registration
 *      screen (place-finger, or the face camera) for this person, right then.
 *   3. Poll until a template appears.
 *   4. Copy that template to every other terminal, so a branch machine knows
 *      them without a second trip to the sensor.
 *
 * Enrolment is a physical act — somebody has to stand at the machine. What is
 * removed is having to do it again at every other machine.
 */
export default function DeviceEnrollPanel({ employeeId, employeeName, compact = false }) {
  const fb = useFeedback()
  const [devices, setDevices] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState('')
  const [waiting, setWaiting] = useState(null) // { deviceId, deviceName, since }
  const pollRef = useRef(null)

  const refresh = useCallback(async () => {
    setError('')
    try {
      const res = await api.status(employeeId)
      setDevices(res.devices || [])
      return res.devices || []
    } catch (err) {
      setError(apiErrorMessage(err))
      setDevices([])
      return []
    }
  }, [employeeId])

  useEffect(() => {
    refresh()
    return () => clearInterval(pollRef.current)
  }, [refresh])

  // While a terminal is waiting for a scan, poll until a template shows up.
  useEffect(() => {
    if (!waiting) return

    pollRef.current = setInterval(async () => {
      const list = await refresh()
      const target = list.find((d) => d.deviceId === waiting.deviceId)

      if (target?.templateCount > 0) {
        clearInterval(pollRef.current)
        setWaiting(null)
        fb.success(`${employeeName} enrolled on ${waiting.deviceName}.`)
        propagate()
        return
      }

      // Give up rather than poll for ever if nobody scans.
      if (Date.now() - waiting.since > 120000) {
        clearInterval(pollRef.current)
        setWaiting(null)
        fb.info('No scan was detected in two minutes. The terminal has been released.')
        api.cancel(employeeId, waiting.deviceId).catch(() => {})
      }
    }, 3000)

    return () => clearInterval(pollRef.current)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [waiting])

  async function pushUser() {
    setBusy('push')
    try {
      const report = await api.pushUser(employeeId)
      if (report.devicesUpdated > 0) {
        fb.success(`User record created on ${report.devicesUpdated} terminal(s).`)
      }
      for (const w of report.warnings || []) fb.error(w)
      await refresh()
    } catch (err) {
      fb.error(apiErrorMessage(err, 'Could not reach the terminals'))
    } finally {
      setBusy('')
    }
  }

  async function startEnroll(deviceId, deviceName) {
    setBusy(`start-${deviceId}`)
    try {
      const res = await api.start(employeeId, { deviceId })
      fb.success(res.message)
      setWaiting({ deviceId, deviceName, since: Date.now() })
    } catch (err) {
      fb.error(apiErrorMessage(err, 'The terminal would not start registration'))
    } finally {
      setBusy('')
    }
  }

  async function cancelEnroll() {
    if (!waiting) return
    clearInterval(pollRef.current)
    await api.cancel(employeeId, waiting.deviceId).catch(() => {})
    setWaiting(null)
    fb.info('Registration cancelled.')
  }

  async function propagate() {
    setBusy('propagate')
    try {
      const report = await api.propagate(employeeId)
      if (report.devicesUpdated > 0) {
        fb.success(`Copied to ${report.devicesUpdated} other terminal(s).`)
      }
      for (const w of report.warnings || []) fb.error(w)
      await refresh()
    } catch (err) {
      fb.error(apiErrorMessage(err, 'Could not copy to the other terminals'))
    } finally {
      setBusy('')
    }
  }

  if (devices === null) {
    return <p className="text-sm text-slate-400">Checking the terminals…</p>
  }

  if (devices.length === 0) {
    return (
      <div className="rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-800 ring-1 ring-amber-200">
        <FiAlertTriangle className="mr-1.5 inline h-4 w-4" />
        No active biometric terminal is registered, so there is nowhere to enrol.
        {error && <span className="mt-1 block text-xs">{error}</span>}
      </div>
    )
  }

  const master = devices.find((d) => d.role === 'Master') || devices[0]
  const anyEnrolled = devices.some((d) => d.templateCount > 0)

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium text-slate-700">
          <MdFingerprint className="h-4.5 w-4.5 text-sky-600" />
          Biometric terminals
        </div>
        <button
          type="button"
          onClick={refresh}
          className="inline-flex items-center gap-1 text-xs font-medium text-slate-500 transition hover:text-sky-600"
        >
          <FiRefreshCw className="h-3.5 w-3.5" /> Refresh
        </button>
      </div>

      {/* Live "go and scan" banner */}
      {waiting && (
        <div className="flex items-start gap-3 rounded-lg bg-sky-50 px-3 py-3 text-sm ring-1 ring-sky-200">
          <span className="relative mt-0.5 flex h-2.5 w-2.5 shrink-0">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-sky-400 opacity-75" />
            <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-sky-500" />
          </span>
          <div className="min-w-0 flex-1">
            <div className="font-semibold text-sky-900">
              {waiting.deviceName} is showing the registration screen
            </div>
            <div className="mt-0.5 text-xs text-sky-800">
              Ask {employeeName} to scan at the terminal now. This panel updates on its own.
            </div>
          </div>
          <button
            type="button"
            onClick={cancelEnroll}
            className="shrink-0 text-xs font-medium text-slate-500 hover:text-rose-600"
          >
            Cancel
          </button>
        </div>
      )}

      {/* Per-terminal state */}
      <ul className="divide-y divide-slate-100 overflow-hidden rounded-lg ring-1 ring-slate-200">
        {devices.map((d) => (
          <li key={d.deviceId} className="flex flex-wrap items-center gap-2 px-3 py-2.5">
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="truncate text-sm font-medium text-slate-800">{d.deviceName}</span>
                {d.role === 'Master' && <Badge tone="sky">Master</Badge>}
                {!d.reachable && (
                  <span className="inline-flex items-center gap-1 text-xs text-slate-400">
                    <FiWifiOff className="h-3.5 w-3.5" /> offline
                  </span>
                )}
              </div>
              <div className="mt-0.5 text-xs text-slate-500">
                Enrol number <span className="font-mono">{d.deviceUserId}</span>
                {d.message && <span className="ml-2 text-slate-400">· {d.message}</span>}
              </div>
            </div>

            <div className="flex shrink-0 items-center gap-2">
              {d.templateCount > 0 ? (
                <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-700">
                  <FiCheckCircle className="h-4 w-4" />
                  {d.templateCount} finger{d.templateCount > 1 ? 's' : ''}
                </span>
              ) : (
                <span className="text-xs text-slate-400">not enrolled</span>
              )}

              <Button
                type="button"
                variant="secondary"
                className="!px-2.5 !py-1 !text-xs"
                disabled={!d.reachable || busy === `start-${d.deviceId}` || Boolean(waiting)}
                onClick={() => startEnroll(d.deviceId, d.deviceName)}
              >
                {busy === `start-${d.deviceId}` ? 'Opening…' : 'Register here'}
              </Button>
            </div>
          </li>
        ))}
      </ul>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          disabled={Boolean(busy) || Boolean(waiting)}
          onClick={() => startEnroll(master.deviceId, master.deviceName)}
        >
          <MdFingerprint className="mr-1.5 inline h-4 w-4" />
          Register on ZKTeco
        </Button>

        <Button type="button" variant="secondary" disabled={Boolean(busy)} onClick={pushUser}>
          {busy === 'push' ? 'Writing…' : 'Create user on terminals'}
        </Button>

        {devices.length > 1 && (
          <Button
            type="button"
            variant="secondary"
            disabled={Boolean(busy) || !anyEnrolled}
            onClick={propagate}
            title={anyEnrolled ? undefined : 'Enrol a finger first — there is nothing to copy yet.'}
          >
            {busy === 'propagate' ? 'Copying…' : 'Copy to other terminals'}
          </Button>
        )}
      </div>

      {!compact && (
        <p className="text-xs text-slate-400">
          Registration opens on the terminal itself, so the person must be standing at it. Once one
          terminal has their finger, the others are filled from the stored copy — nobody has to scan
          twice.
        </p>
      )}

      {error && <p className="text-xs text-rose-600">{error}</p>}
    </div>
  )
}
