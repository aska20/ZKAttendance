import { useState } from 'react'
import { reports, branches as branchApi, devices as deviceApi, employees as employeeApi } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Table, Field, Input, Select, Button, ErrorText, Badge } from '../components/ui'

const iso = (d) => d.toISOString().slice(0, 10)

export default function RangeReport() {
  const { data: opts } = useAsync(
    () => Promise.all([branchApi.list(), deviceApi.list(), employeeApi.list()])
      .then(([b, d, e]) => ({ branches: b, devices: d, employees: e })),
    [],
  )

  const [f, setF] = useState(() => {
    const today = new Date()
    const weekAgo = new Date(today.getTime() - 7 * 864e5)
    return { from: iso(weekAgo), to: iso(today), branchId: '', deviceId: '', employeeId: '' }
  })
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  async function run(e) {
    e.preventDefault()
    setLoading(true); setError('')
    const params = Object.fromEntries(Object.entries(f).filter(([, v]) => v !== ''))
    try {
      setData(await reports.range(params))
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }

  const set = (k) => (e) => setF({ ...f, [k]: e.target.value })
  const report = data?.report

  const columns = [
    { key: 'employeeName', header: 'Employee', render: (r) => <span className="font-medium text-slate-800">{r.employeeName}</span> },
    { key: 'biometricUserId', header: 'ID' },
    { key: 'department', header: 'Department', render: (r) => r.department || '—' },
    { key: 'firstCheckIn', header: 'First in', render: (r) => r.firstCheckIn ? new Date(r.firstCheckIn).toLocaleString() : '—' },
    { key: 'lastCheckOut', header: 'Last out', render: (r) => r.lastCheckOut ? new Date(r.lastCheckOut).toLocaleString() : '—' },
    { key: 'totalWorkHoursFormatted', header: 'Hours' },
    { key: 'status', header: 'Status', render: (r) => <Badge tone={r.status === 'Present' ? 'green' : r.status === 'Absent' ? 'red' : 'amber'}>{r.status}</Badge> },
  ]

  return (
    <div>
      <PageHeader title="Range Report" subtitle={data ? `${data.fromBs} → ${data.toBs} BS` : undefined} />

      <Card className="mb-4 p-4">
        <form onSubmit={run} className="grid grid-cols-2 gap-3 md:grid-cols-5">
          <Field label="From"><Input type="date" value={f.from} onChange={set('from')} /></Field>
          <Field label="To"><Input type="date" value={f.to} onChange={set('to')} /></Field>
          <Field label="Branch">
            <Select value={f.branchId} onChange={set('branchId')}>
              <option value="">All</option>
              {(opts?.branches || []).map((b) => <option key={b.branchId} value={b.branchId}>{b.branchName}</option>)}
            </Select>
          </Field>
          <Field label="Device">
            <Select value={f.deviceId} onChange={set('deviceId')}>
              <option value="">All</option>
              {(opts?.devices || []).map((d) => <option key={d.deviceId} value={d.deviceId}>{d.deviceName}</option>)}
            </Select>
          </Field>
          <Field label="Employee">
            <Select value={f.employeeId} onChange={set('employeeId')}>
              <option value="">All</option>
              {(opts?.employees || []).map((e) => <option key={e.employeeId} value={e.employeeId}>{e.employeeName}</option>)}
            </Select>
          </Field>
          <div className="flex items-end"><Button type="submit">Run</Button></div>
        </form>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}

      {report && (
        <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
          {[
            ['Total', report.totalEmployees],
            ['Present', report.presentCount],
            ['Absent', report.absentCount],
            ['Rate', `${report.attendanceRate}%`],
          ].map(([l, v]) => (
            <Card key={l} className="p-4"><div className="text-xs text-slate-500">{l}</div><div className="text-xl font-semibold">{v}</div></Card>
          ))}
        </div>
      )}

      <Table
        columns={columns}
        rows={(report?.items || []).map((r, i) => ({ ...r, _key: `${r.employeeId}-${i}` }))}
        loading={loading}
        empty="Run the report to see results."
      />
    </div>
  )
}
