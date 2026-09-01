import { useEffect, useMemo, useState } from 'react'
import { overview, holidays as holidayApi, departments as deptApi, employees as empApi } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { ymd, hm } from '../lib/dates'
import { PageHeader, Card, Field, Input, Select, Button, ErrorText } from '../components/ui'
import DayDetailModal from '../components/DayDetailModal'

const isoDay = (d) => ymd(d)

function Cell({ cell, onClick }) {
  if (!cell) return <td className="border-l border-slate-100 px-2 py-3 text-center text-slate-300">–</td>
  if (cell.status === 'Holiday') {
    return <td className="border-l border-slate-100 bg-sky-50/40 px-2 py-3 text-center text-[11px] text-sky-500">Holiday</td>
  }
  if (cell.status === 'Absent') {
    return (
      <td className="border-l border-slate-100 px-2 py-3 text-center">
        <button onClick={onClick} className="rounded-full bg-red-50 px-2 py-0.5 text-[11px] font-medium text-red-600 ring-1 ring-red-200 hover:bg-red-100">
          Absent
        </button>
      </td>
    )
  }
  return (
    <td className="border-l border-slate-100 px-2 py-2 text-center">
      <button onClick={onClick} className="group inline-flex flex-col items-center" title="View every punch">
        <span className="text-green-600">✓</span>
        <span className="text-[10px] leading-tight text-slate-400 group-hover:text-slate-600">
          {hm(cell.firstIn)}{cell.lastOut ? '–' + hm(cell.lastOut) : ''}
        </span>
        {cell.punchCount > 2 && <span className="text-[10px] text-amber-500">{cell.punchCount}×</span>}
      </button>
    </td>
  )
}

export default function Attendance() {
  const today = useMemo(() => new Date(), [])
  const [range, setRange] = useState(() => ({
    from: isoDay(new Date(today.getTime() - 6 * 864e5)),
    to: isoDay(today),
  }))
  const [applied, setApplied] = useState(range)
  const [departmentId, setDepartmentId] = useState('')
  const [search, setSearch] = useState('')
  const [appliedFilters, setAppliedFilters] = useState({ departmentId: '', search: '' })

  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [detail, setDetail] = useState(null)
  const [busyDay, setBusyDay] = useState(null)

  const { data: deptList } = useAsync(() => deptApi.list(), [])
  const { data: allEmployees } = useAsync(() => empApi.list(), [])

  function reload() {
    setLoading(true); setError('')
    const params = { from: applied.from, to: applied.to }
    if (appliedFilters.departmentId) params.departmentId = appliedFilters.departmentId
    if (appliedFilters.search) params.search = appliedFilters.search
    overview.get(params)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
  }

  useEffect(reload, [applied, appliedFilters]) // eslint-disable-line react-hooks/exhaustive-deps

  function apply(e) {
    e.preventDefault()
    setApplied(range)
    setAppliedFilters({ departmentId, search: search.trim() })
  }

  // Click a day header → zoom the range to just that day.
  function focusDay(dateIso) {
    setRange({ from: dateIso, to: dateIso })
    setApplied({ from: dateIso, to: dateIso })
  }

  // Toggle a day between working-day and holiday.
  async function toggleHoliday(day) {
    setBusyDay(day.dateIso)
    try {
      if (day.holidayName === 'Weekly off') return // weekly off is fixed
      const isMarked = day.isHoliday
      if (isMarked) {
        await holidayApi.removeOnDate(day.dateIso)
      } else {
        const name = prompt(`Name this holiday (${day.dateBs} BS):`, 'Holiday')
        if (name == null) return
        await holidayApi.create({ holidayName: name || 'Holiday', date: day.dateIso })
      }
      reload()
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusyDay(null)
    }
  }

  function exportCsv() {
    if (!data) return
    const head = ['Department', 'Employee', 'Biometric ID', 'Branch', 'Working days', 'Present', 'Absent',
      ...data.days.map((d) => `${d.weekday} ${d.dateBs} (${d.dateIso})`)]
    const lines = [head]
    for (const dept of data.departments) {
      for (const e of dept.employees) {
        lines.push([
          dept.departmentName, e.employeeName, e.biometricUserId, e.branchName || '',
          e.totalDays, e.presentDays, e.absentDays,
          ...data.days.map((d) => {
            const c = e.cells[d.dateIso]
            if (!c) return ''
            if (c.status === 'Holiday') return 'Holiday'
            if (c.status === 'Absent') return 'Absent'
            return `Present ${hm(c.firstIn)}${c.lastOut ? '-' + hm(c.lastOut) : ''}`.trim()
          }),
        ])
      }
    }
    const csv = lines.map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(',')).join('\n')
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' })
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = `attendance_${applied.from}_to_${applied.to}.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  }

  return (
    <div>
      <PageHeader
        title="Attendance"
        subtitle={data ? `${data.fromBs} → ${data.toBs} BS  ·  ${data.workingDayCount} working days (Sat + holidays excluded)  ·  ${data.employeeCount} employees` : undefined}
        actions={<Button variant="secondary" onClick={exportCsv} disabled={!data}>⭳ Export CSV</Button>}
      />

      <Card className="mb-4 p-4">
        <form onSubmit={apply} className="grid grid-cols-2 gap-3 md:grid-cols-5">
          <Field label="Start (AD)"><Input type="date" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} /></Field>
          <Field label="End (AD)"><Input type="date" value={range.to} onChange={(e) => setRange({ ...range, to: e.target.value })} /></Field>
          <Field label="Department">
            <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">All departments</option>
              {(deptList || []).map((d) => <option key={d.departmentId} value={d.departmentId}>{d.departmentName}</option>)}
            </Select>
          </Field>
          <Field label="Employee" hint="Type to filter — all names listed">
            <Input
              list="all-employee-names"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="name or biometric id"
            />
            <datalist id="all-employee-names">
              {(allEmployees || []).map((e) => (
                <option key={e.employeeId} value={e.employeeName}>{`#${e.biometricUserId}`}</option>
              ))}
            </datalist>
          </Field>
          <div className="flex items-end gap-2">
            <Button type="submit">Apply</Button>
            <Button type="button" variant="ghost" onClick={() => { setSearch(''); setDepartmentId(''); setAppliedFilters({ departmentId: '', search: '' }) }}>Clear</Button>
          </div>
        </form>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}
      {loading && <p className="text-slate-500">Loading…</p>}

      {data && !loading && (
        <Card className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left text-slate-500">
                <th className="sticky left-0 z-10 bg-white px-4 py-3 font-medium">Employee</th>
                <th className="px-3 py-3 text-center font-medium">Working<br />days</th>
                <th className="px-3 py-3 text-center font-medium text-green-600">Present</th>
                <th className="px-3 py-3 text-center font-medium text-red-500">Absent</th>
                {data.days.map((d) => (
                  <th key={d.dateIso} className={`border-l border-slate-100 px-2 py-2 text-center text-xs font-medium ${d.isHoliday ? 'text-sky-500' : ''}`}>
                    <button onClick={() => focusDay(d.dateIso)} className="block w-full hover:underline" title="Show only this day">
                      <div>{d.weekday}</div>
                      <div className="font-normal text-slate-500">{d.dateBs}</div>
                      <div className="font-normal text-slate-400">{d.dateIso}</div>
                    </button>
                    <button
                      onClick={() => toggleHoliday(d)}
                      disabled={busyDay === d.dateIso || d.holidayName === 'Weekly off'}
                      className="mt-1 text-[10px] text-slate-400 hover:text-sky-600 disabled:opacity-40"
                      title={d.holidayName === 'Weekly off' ? 'Weekly off (fixed)' : d.isHoliday ? 'Unmark holiday' : 'Mark as holiday'}
                    >
                      {d.holidayName === 'Weekly off' ? 'off' : d.isHoliday ? '✕ holiday' : '+ holiday'}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.departments.map((dept) => (
                <DeptSection key={dept.departmentId ?? 'none'} dept={dept} days={data.days} onCell={setDetail} />
              ))}
              {data.departments.length === 0 && (
                <tr><td colSpan={4 + data.days.length} className="px-4 py-6 text-center text-slate-500">No employees match.</td></tr>
              )}
            </tbody>
          </table>
        </Card>
      )}

      {detail && (
        <DayDetailModal
          employeeId={detail.employeeId}
          employeeName={detail.employeeName}
          date={detail.date}
          dateBs={detail.dateBs}
          onClose={() => setDetail(null)}
        />
      )}
    </div>
  )
}

function DeptSection({ dept, days, onCell }) {
  return (
    <>
      <tr className="bg-slate-50">
        <td colSpan={4 + days.length} className="px-4 py-1.5 text-xs font-semibold uppercase tracking-wide text-slate-500">
          {dept.departmentName} · {dept.employees.length}
        </td>
      </tr>
      {dept.employees.map((e) => (
        <tr key={e.employeeId} className="border-b border-slate-50 last:border-0">
          <td className="sticky left-0 z-10 bg-white px-4 py-2">
            <div className="font-medium text-slate-800">{e.employeeName}</div>
            <div className="text-xs text-slate-400">#{e.biometricUserId}{e.branchName ? ` · ${e.branchName}` : ''}</div>
          </td>
          <td className="px-3 py-2 text-center">{e.totalDays}</td>
          <td className="px-3 py-2 text-center font-medium text-green-600">{e.presentDays}</td>
          <td className="px-3 py-2 text-center font-medium text-red-500">{e.absentDays}</td>
          {days.map((d) => (
            <Cell
              key={d.dateIso}
              cell={e.cells[d.dateIso]}
              onClick={() => onCell({ employeeId: e.employeeId, employeeName: e.employeeName, date: d.dateIso, dateBs: d.dateBs })}
            />
          ))}
        </tr>
      ))}
    </>
  )
}
