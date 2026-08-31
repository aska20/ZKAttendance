import { useEffect, useState } from 'react'
import { attendance } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Table, Field, Input, Select, Button, ErrorText, Badge } from '../components/ui'

const QUICK = [
  ['', 'Custom range'],
  ['today', 'Today'], ['yesterday', 'Yesterday'],
  ['thisweek', 'This week'], ['lastweek', 'Last week'],
  ['thismonth', 'This month'], ['lastmonth', 'Last month'],
  ['last7days', 'Last 7 days'], ['last30days', 'Last 30 days'],
]
const STATUSES = ['', 'Full Day', 'Check-in Only', 'Absent']

const blank = {
  search: '', quickFilter: 'last7days', fromDate: '', toDate: '',
  branchId: '', deviceId: '', status: '', minWorkHours: '', maxWorkHours: '',
}

export default function AttendanceLog() {
  const [filters, setFilters] = useState(blank)
  const [applied, setApplied] = useState(blank)
  const [page, setPage] = useState(1)
  const [opts, setOpts] = useState({ branches: [], devices: [] })
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    attendance.logFilters().then(setOpts).catch(() => {})
  }, [])

  useEffect(() => {
    setLoading(true); setError('')
    const params = Object.fromEntries(
      Object.entries({ ...applied, page }).filter(([, v]) => v !== '' && v != null),
    )
    attendance.log(params)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
  }, [applied, page])

  function apply(e) {
    e.preventDefault()
    setPage(1)
    setApplied(filters)
  }

  const set = (k) => (e) => setFilters({ ...filters, [k]: e.target.value })

  const columns = [
    { key: 'date', header: 'Date', render: (r) => <div><div>{r.nepaliDate}</div><div className="text-xs text-slate-400">{new Date(r.date).toLocaleDateString()}</div></div> },
    { key: 'employeeName', header: 'Employee', render: (r) => <div><span className="font-medium text-slate-800">{r.employeeName}</span> <span className="text-xs text-slate-400">#{r.biometricUserId}</span></div> },
    { key: 'in', header: 'In', render: (r) => r.checkInTime ? new Date(r.checkInTime).toLocaleTimeString() : '—' },
    { key: 'out', header: 'Out', render: (r) => r.checkOutTime ? new Date(r.checkOutTime).toLocaleTimeString() : '—' },
    { key: 'hours', header: 'Hours', render: (r) => r.workingHours?.toFixed(2) ?? '0' },
    { key: 'branchName', header: 'Branch', render: (r) => <div>{r.branchName || '—'}{r.isCrossDevice && <Badge tone="amber">cross-device</Badge>}</div> },
    { key: 'status', header: 'Status', render: (r) => <Badge tone={r.status === 'Full Day' ? 'green' : r.status === 'Absent' ? 'red' : 'amber'}>{r.status}</Badge> },
  ]

  const s = data?.stats

  return (
    <div>
      <PageHeader title="Attendance Logs" subtitle="Punches grouped per employee-day with computed hours and status" />

      <Card className="mb-4 p-4">
        <form onSubmit={apply} className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <Field label="Search (biometric ID)"><Input value={filters.search} onChange={set('search')} /></Field>
          <Field label="Quick filter">
            <Select value={filters.quickFilter} onChange={set('quickFilter')}>
              {QUICK.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
          </Field>
          <Field label="From"><Input type="date" value={filters.fromDate} onChange={set('fromDate')} disabled={!!filters.quickFilter} /></Field>
          <Field label="To"><Input type="date" value={filters.toDate} onChange={set('toDate')} disabled={!!filters.quickFilter} /></Field>
          <Field label="Branch">
            <Select value={filters.branchId} onChange={set('branchId')}>
              <option value="">All</option>
              {opts.branches.map((b) => <option key={b.branchId} value={b.branchId}>{b.branchName}</option>)}
            </Select>
          </Field>
          <Field label="Device">
            <Select value={filters.deviceId} onChange={set('deviceId')}>
              <option value="">All</option>
              {opts.devices.map((d) => <option key={d.deviceId} value={d.deviceId}>{d.deviceName}</option>)}
            </Select>
          </Field>
          <Field label="Status">
            <Select value={filters.status} onChange={set('status')}>
              {STATUSES.map((v) => <option key={v} value={v}>{v || 'Any'}</option>)}
            </Select>
          </Field>
          <div className="flex items-end gap-2">
            <Button type="submit">Apply</Button>
            <Button type="button" variant="secondary" onClick={() => { setFilters(blank); setApplied(blank); setPage(1) }}>Reset</Button>
          </div>
        </form>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}

      {s && (
        <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-5">
          {[
            ['Records', data.totalRecords],
            ['Check-ins', s.checkIns],
            ['Check-outs', s.checkOuts],
            ['Full days', s.fullDay],
            ['Avg hours', s.averageWorkHours?.toFixed(2)],
          ].map(([l, v]) => (
            <Card key={l} className="p-4"><div className="text-xs text-slate-500">{l}</div><div className="text-xl font-semibold">{v}</div></Card>
          ))}
        </div>
      )}

      <Table columns={columns} rows={(data?.items || []).map((r, i) => ({ ...r, _key: `${r.employeeId}-${r.date}-${i}` }))} loading={loading} empty="No matching records." />

      {data && data.totalPages > 1 && (
        <div className="mt-4 flex items-center justify-center gap-3 text-sm">
          <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Prev</Button>
          <span>Page {data.page} of {data.totalPages}</span>
          <Button variant="secondary" disabled={page >= data.totalPages} onClick={() => setPage(page + 1)}>Next</Button>
        </div>
      )}
    </div>
  )
}
