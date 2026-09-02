import { useEffect, useMemo, useRef, useState } from 'react'
import { overview, holidays as holidayApi, departments as deptApi, employees as empApi } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { ymd, hm } from '../lib/dates'
import { PageHeader, Card, Field, Input, Button, ErrorText } from '../components/ui'
import Combobox from '../components/Combobox'
import DayDetailModal from '../components/DayDetailModal'
import { useFeedback } from '../components/feedback'

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
      <button onClick={onClick} className="group inline-flex flex-col items-center" title="View check-in / check-out detail">
        <span className="text-lg font-bold leading-none text-green-600">✓</span>
        <span className="mt-0.5 text-[10px] leading-tight text-slate-500 group-hover:text-slate-700">
          {hm(cell.firstIn)}{cell.lastOut ? '–' + hm(cell.lastOut) : ''}
        </span>
      </button>
    </td>
  )
}

export default function Attendance() {
  const fb = useFeedback()
  const today = useMemo(() => new Date(), [])
  const [range, setRange] = useState(() => ({
    from: isoDay(new Date(today.getTime() - 6 * 864e5)),
    to: isoDay(today),
  }))
  const [departmentId, setDepartmentId] = useState('')
  const [employeeId, setEmployeeId] = useState('')

  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [detail, setDetail] = useState(null)

  const { data: deptList } = useAsync(() => deptApi.list(), [])
  const { data: allEmployees } = useAsync(() => empApi.list(), [])

  const deptOptions = (deptList || []).map((d) => ({ value: String(d.departmentId), label: d.departmentName }))
  const empOptions = (allEmployees || []).map((e) => ({
    value: String(e.employeeId), label: e.employeeName, hint: `#${e.biometricUserId}`,
  }))

  // fetch(): loads the pivot. `silent` skips the loading flag so background
  // refreshes (e.g. after a holiday toggle) don't blank the table.
  const timer = useRef(null)
  function fetchPivot({ silent = false } = {}) {
    if (!silent) setLoading(true)
    setError('')
    const params = { from: range.from, to: range.to }
    if (departmentId) params.departmentId = departmentId
    if (employeeId) params.employeeId = employeeId
    return overview.get(params)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => { if (!silent) setLoading(false) })
  }

  // Auto-load when a filter changes, debounced so dragging the date picker
  // doesn't fire a request on every keystroke.
  useEffect(() => {
    clearTimeout(timer.current)
    timer.current = setTimeout(() => fetchPivot(), 250)
    return () => clearTimeout(timer.current)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [range.from, range.to, departmentId, employeeId])

  function focusDay(dateIso) {
    setRange({ from: dateIso, to: dateIso })
  }

  // Recompute the grid locally so a holiday toggle shows instantly, then
  // reconcile with the server in the background.
  function patchHoliday(dateIso, makeHoliday, name) {
    setData((d) => {
      if (!d) return d
      const days = d.days.map((day) =>
        day.dateIso === dateIso
          ? { ...day, isHoliday: makeHoliday, holidayName: makeHoliday ? (name || 'Holiday') : null }
          : day,
      )
      const workingDayCount = days.filter((x) => !x.isHoliday).length
      const departments = d.departments.map((dept) => ({
        ...dept,
        employees: dept.employees.map((e) => {
          const cell = e.cells[dateIso]
          if (!cell) return e
          let { presentDays, absentDays } = e
          const nextCells = { ...e.cells }
          if (makeHoliday) {
            if (cell.status === 'Present') presentDays -= 1
            else if (cell.status === 'Absent') absentDays -= 1
            nextCells[dateIso] = { status: 'Holiday', firstIn: null, lastOut: null, hours: 0, punchCount: 0 }
          } else {
            // We don't know present/absent without the punch data — the silent
            // refetch fixes it; show a neutral placeholder for the moment.
            nextCells[dateIso] = { status: 'Absent', firstIn: null, lastOut: null, hours: 0, punchCount: 0 }
            absentDays += 1
          }
          return { ...e, cells: nextCells, presentDays, absentDays, totalDays: workingDayCount }
        }),
      }))
      return { ...d, days, workingDayCount, departments }
    })
  }

  async function toggleHoliday(day) {
    if (day.holidayName === 'Weekly off') return

    if (day.isHoliday) {
      const ok = await fb.confirm({ title: 'Remove holiday', message: `Unmark ${day.dateBs} BS as a holiday?`, confirmText: 'Remove' })
      if (!ok) return
      patchHoliday(day.dateIso, false)                 // instant
      try {
        await holidayApi.removeOnDate(day.dateIso)
        fetchPivot({ silent: true })                    // reconcile
      } catch (e) {
        fb.error(apiErrorMessage(e))
        fetchPivot({ silent: true })
      }
      return
    }

    const name = await fb.promptText({ title: 'Mark as holiday', label: `Name for ${day.dateBs} BS`, defaultValue: 'Holiday' })
    if (name === null) return
    patchHoliday(day.dateIso, true, name || 'Holiday')  // instant
    try {
      await holidayApi.create({ holidayName: name || 'Holiday', date: day.dateIso })
      fetchPivot({ silent: true })                      // reconcile
    } catch (e) {
      fb.error(apiErrorMessage(e))
      fetchPivot({ silent: true })
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
    a.download = `attendance_${range.from}_to_${range.to}.csv`
    a.click()
    URL.revokeObjectURL(a.href)
  }

  return (
    <div>
      <PageHeader
        title="Attendance"
        subtitle={data ? `${data.fromBs} to ${data.toBs} BS. ${data.workingDayCount} working days, ${data.employeeCount} employees.` : undefined}
        actions={<Button variant="secondary" onClick={exportCsv} disabled={!data}>⭳ Export CSV</Button>}
      />

      <Card className="mb-4 p-4">
        <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
          <Field label="Start (AD)">
            <Input type="date" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} />
          </Field>
          <Field label="End (AD)">
            <Input type="date" value={range.to} onChange={(e) => setRange({ ...range, to: e.target.value })} />
          </Field>
          <Field label="Department">
            <Combobox options={deptOptions} value={departmentId} onChange={setDepartmentId} placeholder="All departments" />
          </Field>
          <Field label="Employee">
            <Combobox options={empOptions} value={employeeId} onChange={setEmployeeId} placeholder="All employees" />
          </Field>
          <div className="flex items-end">
            {loading
              ? <span className="text-sm text-slate-400">Loading…</span>
              : <Button variant="ghost" onClick={() => { setDepartmentId(''); setEmployeeId('') }}>Reset filters</Button>}
          </div>
        </div>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}

      {data && (
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
                      disabled={d.holidayName === 'Weekly off'}
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
