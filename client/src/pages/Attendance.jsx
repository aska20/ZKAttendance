import { useEffect, useMemo, useRef, useState, useCallback } from 'react'
import { overview, holidays as holidayApi, departments as deptApi, employees as empApi } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { ymd, hm } from '../lib/dates'
import { PageHeader, Card, Field, Input, Select, Button, ErrorText } from '../components/ui'
import DayDetailModal from '../components/DayDetailModal'

const isoDay = (d) => ymd(d)

/** yyyy-mm-dd → dd/mm/yy */
function fmtDate(iso) {
  if (!iso) return ''
  const [y, m, d] = iso.split('-')
  return `${d}/${m}/${y.slice(2)}`
}

// ─── Status dot cell ──────────────────────────────────────────────────────────
function Cell({ cell, onClick }) {
  if (!cell) {
    return <td className="border-l border-slate-100 px-2 py-3 text-center text-slate-200">–</td>
  }

  if (cell.status === 'Holiday') {
    return (
      <td className="border-l border-slate-100 bg-sky-50/50 px-2 py-3 text-center">
        <span className="inline-block h-3 w-3 rounded-full bg-sky-300" title="Holiday" />
      </td>
    )
  }

  if (cell.status === 'Absent') {
    return (
      <td className="border-l border-slate-100 px-2 py-3 text-center">
        <button onClick={onClick} title="Absent — click for detail">
          <span className="inline-block h-3 w-3 rounded-full bg-red-500 transition hover:scale-125" />
        </button>
      </td>
    )
  }

  // Present
  return (
    <td className="border-l border-slate-100 px-2 py-2 text-center">
      <button onClick={onClick} className="group inline-flex flex-col items-center gap-0.5" title="Present — click for detail">
        <span className="inline-block h-3 w-3 rounded-full bg-green-500 transition group-hover:scale-125" />
        <span className="text-[9px] leading-none text-slate-400 group-hover:text-slate-600">
          {hm(cell.firstIn)}{cell.lastOut ? '–' + hm(cell.lastOut) : ''}
        </span>
      </button>
    </td>
  )
}

// ─── Searchable employee dropdown ───────────────────────────────────────────
function EmployeeSelect({ employees = [], value, onChange }) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const ref = useRef(null)

  // Close when clicking outside
  useEffect(() => {
    function handler(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  const filtered = useMemo(() => {
    const q = query.toLowerCase()
    return employees.filter(
      (e) =>
        e.employeeName.toLowerCase().includes(q) ||
        String(e.biometricUserId).includes(q)
    ).slice(0, 50)
  }, [employees, query])

  const selected = employees.find((e) => String(e.employeeId) === String(value))

  function select(emp) {
    onChange(String(emp.employeeId))
    setQuery('')
    setOpen(false)
  }

  function clear() {
    onChange('')
    setQuery('')
    setOpen(false)
  }

  return (
    <div ref={ref} className="relative">
      <div
        className="flex items-center rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm shadow-sm focus-within:border-slate-400 focus-within:ring-1 focus-within:ring-slate-300"
      >
        <input
          className="flex-1 bg-transparent outline-none placeholder:text-slate-400"
          placeholder={selected ? selected.employeeName : 'All employees'}
          value={query}
          onChange={(e) => { setQuery(e.target.value); setOpen(true) }}
          onFocus={() => setOpen(true)}
        />
        {(selected || query) && (
          <button onClick={clear} className="ml-1 text-slate-400 hover:text-slate-600" title="Clear">
            ✕
          </button>
        )}
        <span className="ml-1 text-slate-300">▾</span>
      </div>

      {open && (
        <ul className="absolute z-50 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg">
          {/* All employees option */}
          <li>
            <button
              className="w-full px-3 py-2 text-left text-sm text-slate-500 hover:bg-slate-50"
              onClick={clear}
            >
              All employees
            </button>
          </li>
          {filtered.length === 0 && (
            <li className="px-3 py-2 text-sm text-slate-400">No match</li>
          )}
          {filtered.map((e) => (
            <li key={e.employeeId}>
              <button
                className={`w-full px-3 py-2 text-left text-sm hover:bg-slate-50 ${
                  String(e.employeeId) === String(value) ? 'bg-slate-100 font-medium' : ''
                }`}
                onClick={() => select(e)}
              >
                <span className="text-slate-800">{e.employeeName}</span>
                <span className="ml-2 text-xs text-slate-400">#{e.biometricUserId}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

// ─── Main page ────────────────────────────────────────────────────────────────
export default function Attendance() {
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
  const [busyDay, setBusyDay] = useState(null)

  const { data: deptList } = useAsync(() => deptApi.list(), [])
  const { data: allEmployees } = useAsync(() => empApi.list(), [])

  const tableRef = useRef(null)

  // ── Auto-fetch on any filter change (debounced 300 ms) ──
  useEffect(() => {
    const id = setTimeout(() => {
      setLoading(true)
      setError('')
      const params = { from: range.from, to: range.to }
      if (departmentId) params.departmentId = departmentId
      if (employeeId) params.employeeId = employeeId
      overview.get(params)
        .then(setData)
        .catch((e) => setError(apiErrorMessage(e)))
        .finally(() => setLoading(false))
    }, 300)
    return () => clearTimeout(id)
  }, [range.from, range.to, departmentId, employeeId]) // eslint-disable-line react-hooks/exhaustive-deps

  function focusDay(dateIso) {
    setRange({ from: dateIso, to: dateIso })
  }

  async function toggleHoliday(day) {
    setBusyDay(day.dateIso)
    try {
      if (day.holidayName === 'Weekly off') return
      if (day.isHoliday) {
        await holidayApi.removeOnDate(day.dateIso)
      } else {
        const name = prompt(`Name this holiday (${day.dateBs} BS):`, 'Holiday')
        if (name == null) return
        await holidayApi.create({ holidayName: name || 'Holiday', date: day.dateIso })
      }
      // re-trigger fetch
      setRange((r) => ({ ...r }))
    } catch (e) {
      setError(apiErrorMessage(e))
    } finally {
      setBusyDay(null)
    }
  }

  function exportCsv() {
    if (!data) return
    const head = ['Employee', 'Biometric ID', 'Branch', 'Working days', 'Present', 'Absent',
      ...data.days.map((d) => `${d.weekday} ${fmtDate(d.dateIso)}`)]
    const lines = [head]
    for (const dept of data.departments) {
      for (const e of dept.employees) {
        lines.push([
          e.employeeName, e.biometricUserId, e.branchName || '',
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

  // Flatten all employees from all departments (no dept grouping rows)
  const allRows = data ? data.departments.flatMap((dept) => dept.employees) : []

  return (
    <div>
      <PageHeader
        title="Attendance"
        subtitle={
          data
            ? `${data.fromBs} → ${data.toBs} BS · ${data.workingDayCount} working days · ${data.employeeCount} employees`
            : undefined
        }
        actions={<Button variant="secondary" onClick={exportCsv} disabled={!data}>⭳ Export CSV</Button>}
      />

      {/* ── Filters ── */}
      <Card className="mb-4 p-4">
        <div className="grid grid-cols-2 gap-3 md:grid-cols-5">
          <Field label="Start (AD)">
            <Input type="date" value={range.from} onChange={(e) => setRange((r) => ({ ...r, from: e.target.value }))} />
          </Field>
          <Field label="End (AD)">
            <Input type="date" value={range.to} onChange={(e) => setRange((r) => ({ ...r, to: e.target.value }))} />
          </Field>
          <Field label="Department">
            <Select value={departmentId} onChange={(e) => setDepartmentId(e.target.value)}>
              <option value="">All departments</option>
              {(deptList || []).map((d) => (
                <option key={d.departmentId} value={d.departmentId}>{d.departmentName}</option>
              ))}
            </Select>
          </Field>
          <Field label="Employee">
            <EmployeeSelect
              employees={allEmployees || []}
              value={employeeId}
              onChange={setEmployeeId}
            />
          </Field>
          <div className="flex items-end">
            <Button
              type="button"
              variant="ghost"
              onClick={() => { setEmployeeId(''); setDepartmentId('') }}
            >
              Clear
            </Button>
          </div>
        </div>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}
      {loading && <p className="py-2 text-sm text-slate-400">Loading…</p>}

      {/* ── Table ── */}
      {data && !loading && (
        <div ref={tableRef} className="overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200">
                {/* Static columns */}
                <th className="sticky left-0 z-10 bg-white px-4 py-3 text-left text-xs font-medium text-slate-500 whitespace-nowrap">
                  Employee
                </th>
                <th className="px-3 py-3 text-center text-xs font-medium text-slate-500 whitespace-nowrap">
                  Working Days
                </th>
                <th className="px-3 py-3 text-center text-xs font-medium text-slate-500 whitespace-nowrap">
                  Present
                </th>
                <th className="px-3 py-3 text-center text-xs font-medium text-slate-500 whitespace-nowrap">
                  Absent
                </th>

                {/* Date columns */}
                {data.days.map((d) => (
                  <th
                    key={d.dateIso}
                    className={`border-l border-slate-100 px-3 py-2 text-center text-xs font-medium whitespace-nowrap ${
                      d.isHoliday ? 'bg-sky-50 text-sky-500' : 'text-slate-500'
                    }`}
                  >
                    <button
                      onClick={() => focusDay(d.dateIso)}
                      className="block w-full"
                      title="Show only this day"
                    >
                      <div>{d.weekday}</div>
                      <div className="text-[11px] font-normal text-slate-400">{fmtDate(d.dateIso)}</div>
                    </button>
                    <button
                      onClick={() => toggleHoliday(d)}
                      disabled={busyDay === d.dateIso || d.holidayName === 'Weekly off'}
                      className="mt-0.5 text-[10px] text-slate-300 hover:text-sky-500 disabled:opacity-30"
                      title={
                        d.holidayName === 'Weekly off'
                          ? 'Weekly off (fixed)'
                          : d.isHoliday ? 'Unmark holiday' : 'Mark as holiday'
                      }
                    >
                      {d.holidayName === 'Weekly off' ? 'off' : d.isHoliday ? '✕' : '+'}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>

            <tbody className="divide-y divide-slate-100">
              {allRows.map((e) => (
                <tr key={e.employeeId} className="hover:bg-slate-50">
                  {/* Sticky employee name */}
                  <td className="sticky left-0 z-10 bg-white px-4 py-2.5 hover:bg-slate-50">
                    <div className="font-medium text-slate-800 whitespace-nowrap">{e.employeeName}</div>
                    <div className="text-xs text-slate-400">
                      #{e.biometricUserId}{e.branchName ? ` · ${e.branchName}` : ''}
                    </div>
                  </td>
                  {/* Working days — plain black */}
                  <td className="px-3 py-2.5 text-center text-sm font-medium text-slate-700">{e.totalDays}</td>
                  {/* Present — green */}
                  <td className="px-3 py-2.5 text-center text-sm font-semibold text-green-600">{e.presentDays}</td>
                  {/* Absent — red */}
                  <td className="px-3 py-2.5 text-center text-sm font-semibold text-red-500">{e.absentDays}</td>
                  {/* Date cells */}
                  {data.days.map((d) => (
                    <Cell
                      key={d.dateIso}
                      cell={e.cells[d.dateIso]}
                      onClick={() => setDetail({
                        employeeId: e.employeeId,
                        employeeName: e.employeeName,
                        date: d.dateIso,
                        dateBs: d.dateBs,
                      })}
                    />
                  ))}
                </tr>
              ))}

              {allRows.length === 0 && (
                <tr>
                  <td colSpan={4 + (data.days?.length ?? 0)} className="px-4 py-8 text-center text-slate-400">
                    No employees found.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
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
