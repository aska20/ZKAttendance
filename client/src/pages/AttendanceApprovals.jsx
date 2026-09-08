import { useCallback, useEffect, useMemo, useState } from 'react'
import { approvals as api, departments as deptApi } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { hm, todayIso } from '../lib/dates'
import { PageHeader, Card, Button, Badge, ErrorText, Select } from '../components/ui'
import { useFeedback } from '../components/feedback'
import { useAuth } from '../context/AuthContext'
import FormattedDate from '../components/FormattedDate'
import DateToggle from '../components/DateToggle'
import NepaliDatePicker from '../components/NepaliDatePicker'

const TABS = [
  { key: 'Pending', label: 'Pending' },
  { key: 'Approved', label: 'Approved' },
  { key: 'Rejected', label: 'Rejected' },
  { key: 'All', label: 'All' },
]

export default function AttendanceApprovals() {
  const fb = useFeedback()
  const { role } = useAuth()
  const isAdmin = role === 'Admin'

  const [tab, setTab] = useState('Pending')
  const [range, setRange] = useState(() => {
    const from = new Date(Date.now() - 29 * 864e5)
    const p = (n) => String(n).padStart(2, '0')
    return {
      from: `${from.getFullYear()}-${p(from.getMonth() + 1)}-${p(from.getDate())}`,
      to: todayIso(),
    }
  })
  const [departmentId, setDepartmentId] = useState('')
  const [depts, setDepts] = useState([])

  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(null)
  const [selected, setSelected] = useState(new Set())

  useEffect(() => {
    deptApi.list().then(setDepts).catch(() => setDepts([]))
  }, [])

  const load = useCallback(() => {
    setError('')
    setSelected(new Set())
    api
      .list({
        status: tab,
        from: range.from,
        to: range.to,
        departmentId: departmentId || undefined,
      })
      .then(setData)
      .catch((e) => {
        setError(apiErrorMessage(e))
        // Leaving data null kept the card on "Loading..." underneath the error.
        setData({ items: [], pendingCount: 0 })
      })
  }, [tab, range.from, range.to, departmentId])

  useEffect(load, [load])

  const items = data?.items ?? []
  const pendingRows = useMemo(() => items.filter((i) => i.status === 'Pending'), [items])

  function toggle(id) {
    setSelected((prev) => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })
  }

  function toggleAll() {
    setSelected((prev) =>
      prev.size === pendingRows.length ? new Set() : new Set(pendingRows.map((r) => r.approvalId)),
    )
  }

  async function decide(row, action) {
    setBusy(row.approvalId)
    try {
      await api[action](row.approvalId)
      fb.success(`${row.employeeName} ${action === 'approve' ? 'approved' : 'rejected'}`)
      load()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    } finally {
      setBusy(null)
    }
  }

  async function approveSelected() {
    const ids = [...selected]
    if (ids.length === 0) return
    const ok = await fb.confirm({
      title: 'Approve selected',
      message: `Approve ${ids.length} late arrival${ids.length > 1 ? 's' : ''}?`,
      confirmText: 'Approve',
    })
    if (!ok) return

    setBusy('bulk')
    try {
      const res = await api.approveMany(ids)
      fb.success(`${res.approved} approved`)
      load()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    } finally {
      setBusy(null)
    }
  }

  return (
    <div>
      <PageHeader
        title="Attendance Approvals"
        subtitle={
          data
            ? `Arrivals after ${data.approvalRequiredAfter} need approval`
            : 'Late arrivals waiting on a decision'
        }
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <DateToggle />
            <NepaliDatePicker
              value={range.from}
              onChange={(from) => setRange((r) => ({ ...r, from }))}
              className="w-40"
            />
            <NepaliDatePicker
              value={range.to}
              onChange={(to) => setRange((r) => ({ ...r, to }))}
              className="w-40"
            />
          </div>
        }
      />

      {error && <ErrorText>{error}</ErrorText>}

      {/* Tabs and filters */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div className="inline-flex rounded-lg bg-slate-100 p-0.5 text-sm font-medium ring-1 ring-slate-200">
          {TABS.map((t) => (
            <button
              key={t.key}
              type="button"
              onClick={() => setTab(t.key)}
              className={`rounded px-3 py-1.5 transition ${
                tab === t.key ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-800'
              }`}
            >
              {t.label}
              {t.key === 'Pending' && data?.pendingCount > 0 && (
                <span className="ml-1.5 rounded-full bg-rose-500 px-1.5 py-0.5 text-[10px] font-bold text-white">
                  {data.pendingCount}
                </span>
              )}
            </button>
          ))}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Select
            value={departmentId}
            onChange={(e) => setDepartmentId(e.target.value)}
            className="w-48"
          >
            <option value="">All departments</option>
            {depts.map((d) => (
              <option key={d.departmentId} value={d.departmentId}>
                {d.departmentName}
              </option>
            ))}
          </Select>

          {isAdmin && selected.size > 0 && (
            <Button onClick={approveSelected} disabled={busy === 'bulk'}>
              {busy === 'bulk' ? 'Approving...' : `Approve ${selected.size}`}
            </Button>
          )}
        </div>
      </div>

      <Card className="overflow-hidden">
        {!data ? (
          <p className="py-16 text-center text-sm text-slate-400">Loading...</p>
        ) : items.length === 0 ? (
          <p className="py-16 text-center text-sm text-slate-400">
            {error
              ? 'Could not load approvals.'
              : tab === 'Pending'
                ? 'Nothing waiting for approval.'
                : 'No records in this range.'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-slate-200 bg-slate-50 text-left text-xs text-slate-500">
                <tr>
                  {isAdmin && pendingRows.length > 0 && (
                    <th className="w-10 px-3 py-3">
                      <input
                        type="checkbox"
                        checked={selected.size === pendingRows.length && pendingRows.length > 0}
                        onChange={toggleAll}
                        className="h-4 w-4 rounded border-slate-300"
                      />
                    </th>
                  )}
                  <th className="px-4 py-3 font-medium">Employee</th>
                  <th className="px-4 py-3 font-medium">Date</th>
                  <th className="px-4 py-3 font-medium">Check in</th>
                  <th className="px-4 py-3 font-medium">Late by</th>
                  <th className="px-4 py-3 font-medium">Status</th>
                  <th className="px-4 py-3 text-right font-medium">Action</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {items.map((r) => (
                  <tr key={r.approvalId} className="transition hover:bg-slate-50/70">
                    {isAdmin && pendingRows.length > 0 && (
                      <td className="px-3 py-2.5">
                        {r.status === 'Pending' && (
                          <input
                            type="checkbox"
                            checked={selected.has(r.approvalId)}
                            onChange={() => toggle(r.approvalId)}
                            className="h-4 w-4 rounded border-slate-300"
                          />
                        )}
                      </td>
                    )}
                    <td className="px-4 py-2.5">
                      <div className="flex items-center gap-2.5">
                        <Initial name={r.employeeName} photoUrl={r.photoUrl} />
                        <div className="min-w-0">
                          <div className="truncate font-medium text-slate-800">{r.employeeName}</div>
                          <div className="truncate text-xs text-slate-400">{r.departmentName}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-2.5 tabular-nums text-slate-600">
                      <FormattedDate date={r.date} dateBs={r.dateBs} suffix={false} />
                    </td>
                    <td className="px-4 py-2.5 font-semibold tabular-nums text-slate-800">
                      {r.firstCheckIn ? hm(r.firstCheckIn) : '\u2014'}
                    </td>
                    <td className="px-4 py-2.5 tabular-nums text-rose-600">
                      {r.minutesLate > 0 ? `${r.minutesLate} min` : '\u2014'}
                    </td>
                    <td className="px-4 py-2.5">
                      <StatusBadge status={r.status} decidedBy={r.decidedBy} />
                    </td>
                    <td className="px-4 py-2.5 text-right">
                      {r.status === 'Pending' && isAdmin ? (
                        <div className="inline-flex gap-1.5">
                          <button
                            type="button"
                            disabled={busy === r.approvalId}
                            onClick={() => decide(r, 'approve')}
                            className="rounded-md bg-emerald-600 px-2.5 py-1 text-xs font-semibold text-white transition hover:bg-emerald-700 disabled:opacity-50"
                          >
                            Approve
                          </button>
                          <button
                            type="button"
                            disabled={busy === r.approvalId}
                            onClick={() => decide(r, 'reject')}
                            className="rounded-md bg-white px-2.5 py-1 text-xs font-semibold text-rose-600 ring-1 ring-rose-200 transition hover:bg-rose-50 disabled:opacity-50"
                          >
                            Reject
                          </button>
                        </div>
                      ) : (
                        <span className="text-xs text-slate-400">
                          {r.status === 'Pending' ? 'Admin only' : ''}
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}

/** Small photo, or the first letter when there is none. */
function Initial({ name, photoUrl }) {
  if (photoUrl) {
    return <img src={photoUrl} alt="" className="h-9 w-9 shrink-0 rounded-full object-cover" />
  }
  return (
    <span className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-sky-100 text-sm font-semibold text-sky-700">
      {(name || '?').charAt(0).toUpperCase()}
    </span>
  )
}

function StatusBadge({ status, decidedBy }) {
  if (status === 'Pending') return <Badge tone="amber">Pending</Badge>
  if (status === 'Approved')
    return (
      <span className="inline-flex flex-col">
        <Badge tone="green">Approved</Badge>
        {decidedBy && <span className="mt-0.5 text-[10px] text-slate-400">by {decidedBy}</span>}
      </span>
    )
  if (status === 'Rejected')
    return (
      <span className="inline-flex flex-col">
        <Badge tone="red">Rejected</Badge>
        {decidedBy && <span className="mt-0.5 text-[10px] text-slate-400">by {decidedBy}</span>}
      </span>
    )
  return <Badge tone="slate">On time</Badge>
}
