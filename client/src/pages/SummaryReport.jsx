import { Fragment, useEffect, useMemo, useState } from 'react'
import { overview, departments as deptApi } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { ymd } from '../lib/dates'
import { PageHeader, Card, Field, Button, ErrorText } from '../components/ui'
import Combobox from '../components/Combobox'
import EmployeeCalendarModal from '../components/EmployeeCalendarModal'

const iso = (d) => ymd(d)

// period: { type: 'week'|'month'|'year', anchor: Date }
function rangeFor({ type, anchor }) {
  const a = new Date(anchor)
  if (type === 'week') {
    const s = new Date(a); s.setDate(a.getDate() - a.getDay())
    const e = new Date(s); e.setDate(s.getDate() + 6)
    return { from: iso(s), to: iso(e), label: `Week of ${iso(s)}` }
  }
  if (type === 'year') {
    return { from: `${a.getFullYear()}-01-01`, to: `${a.getFullYear()}-12-31`, label: String(a.getFullYear()) }
  }
  const s = new Date(a.getFullYear(), a.getMonth(), 1)
  const e = new Date(a.getFullYear(), a.getMonth() + 1, 0)
  return { from: iso(s), to: iso(e), label: s.toLocaleString('default', { month: 'long', year: 'numeric' }) }
}

function shift({ type, anchor }, dir) {
  const a = new Date(anchor)
  if (type === 'week') a.setDate(a.getDate() + dir * 7)
  else if (type === 'year') a.setFullYear(a.getFullYear() + dir)
  else a.setMonth(a.getMonth() + dir)
  return { type, anchor: a }
}

export default function SummaryReport() {
  const [period, setPeriod] = useState({ type: 'month', anchor: new Date() })
  const [departmentId, setDepartmentId] = useState('')
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [calendarFor, setCalendarFor] = useState(null)

  const { data: deptList } = useAsync(() => deptApi.list(), [])
  const deptOptions = (deptList || []).map((d) => ({ value: String(d.departmentId), label: d.departmentName }))

  const r = useMemo(() => rangeFor(period), [period])

  useEffect(() => {
    setLoading(true); setError('')
    const params = { from: r.from, to: r.to }
    if (departmentId) params.departmentId = departmentId
    overview.summary(params)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
  }, [r.from, r.to, departmentId])

  function exportCsv() {
    if (!data) return
    const head = ['Department', 'Employee', 'Biometric ID', 'Present days', 'Absent days', 'Total hours', 'Earliest in', 'Latest out', 'Avg in', 'Avg out']
    const lines = [head]
    for (const dept of data.departments)
      for (const e of dept.employees)
        lines.push([dept.departmentName, e.employeeName, e.biometricUserId, e.presentDays, e.absentDays, e.totalHours, e.earliestIn || '', e.latestOut || '', e.avgIn || '', e.avgOut || ''])
    const csv = lines.map((x) => x.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(',')).join('\n')
    const a = document.createElement('a')
    a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
    a.download = `summary_${r.from}_to_${r.to}.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  }

  return (
    <div>
      <PageHeader
        title="Summary Report"
        subtitle={data ? `${data.fromBs} to ${data.toBs} BS · ${data.workingDays} working days · ${data.employeeCount} employees` : undefined}
        actions={<Button variant="secondary" onClick={exportCsv} disabled={!data}>⭳ Export CSV</Button>}
      />

      <Card className="mb-4 p-4">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
          <Field label="Period">
            <div className="flex gap-1">
              {['week', 'month', 'year'].map((t) => (
                <button
                  key={t}
                  onClick={() => setPeriod({ type: t, anchor: new Date() })}
                  className={`flex-1 rounded-md px-3 py-1.5 text-sm font-medium capitalize ${period.type === t ? 'bg-sky-600 text-white' : 'text-slate-600 hover:bg-slate-100'}`}
                >
                  {t}
                </button>
              ))}
            </div>
          </Field>
          <Field label={r.label}>
            <div className="flex gap-2">
              <Button variant="secondary" className="flex-1" onClick={() => setPeriod((p) => shift(p, -1))}>← Prev</Button>
              <Button variant="secondary" className="flex-1" onClick={() => setPeriod((p) => shift(p, 1))}>Next →</Button>
            </div>
          </Field>
          <Field label="Department">
            <Combobox options={deptOptions} value={departmentId} onChange={setDepartmentId} placeholder="All departments" />
          </Field>
        </div>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}
      {loading && <p className="text-slate-500">Loading…</p>}

      {data && !loading && (
        <Card className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left text-slate-500">
                <th className="px-4 py-3 font-medium">Employee</th>
                <th className="px-3 py-3 text-center font-medium text-green-600">Present</th>
                <th className="px-3 py-3 text-center font-medium text-red-500">Absent</th>
                <th className="px-3 py-3 text-center font-medium">Hours</th>
                <th className="px-3 py-3 text-center font-medium">Earliest in</th>
                <th className="px-3 py-3 text-center font-medium">Latest out</th>
                <th className="px-3 py-3 text-center font-medium">Avg in</th>
                <th className="px-3 py-3 text-center font-medium">Avg out</th>
              </tr>
            </thead>
            <tbody>
              {data.departments.map((dept) => (
                <Fragment key={`d${dept.departmentId ?? 'none'}`}>
                  <tr className="bg-slate-50">
                    <td colSpan={8} className="px-4 py-1.5 text-xs font-semibold uppercase tracking-wide text-slate-500">
                      {dept.departmentName} · {dept.employees.length}
                    </td>
                  </tr>
                  {dept.employees.map((e) => (
                    <tr key={e.employeeId} className="border-b border-slate-50 last:border-0">
                      <td className="px-4 py-2">
                        <button onClick={() => setCalendarFor(e)} className="font-medium text-slate-800 hover:text-sky-600 hover:underline">
                          {e.employeeName}
                        </button>
                        <span className="ml-2 text-xs text-slate-400">#{e.biometricUserId}</span>
                      </td>
                      <td className="px-3 py-2 text-center font-medium text-green-600">{e.presentDays}</td>
                      <td className="px-3 py-2 text-center font-medium text-red-500">{e.absentDays}</td>
                      <td className="px-3 py-2 text-center">{e.totalHours}</td>
                      <td className="px-3 py-2 text-center">{e.earliestIn || '—'}</td>
                      <td className="px-3 py-2 text-center">{e.latestOut || '—'}</td>
                      <td className="px-3 py-2 text-center text-slate-500">{e.avgIn || '—'}</td>
                      <td className="px-3 py-2 text-center text-slate-500">{e.avgOut || '—'}</td>
                    </tr>
                  ))}
                </Fragment>
              ))}
              {data.departments.length === 0 && (
                <tr><td colSpan={8} className="px-4 py-6 text-center text-slate-500">No employees.</td></tr>
              )}
            </tbody>
          </table>
        </Card>
      )}

      {calendarFor && (
        <EmployeeCalendarModal
          employeeId={calendarFor.employeeId}
          employeeName={calendarFor.employeeName}
          onClose={() => setCalendarFor(null)}
        />
      )}
    </div>
  )
}
