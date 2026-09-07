import { useMemo, useState } from 'react'
import { useCalendar } from '../../context/CalendarContext'
import AttendanceCell from './AttendanceCell'



/**
 * The attendance grid table.
 * Props:
 *   data        — API response object (days, departments, employeeCount…)
 *   onFocusDay  — (dateIso: string) => void  — narrows date range to one day
 *   onCellClick — ({ employeeId, employeeName, date, dateBs }) => void
 */
export default function AttendanceTable({ data, onFocusDay, onCellClick }) {
  const { isBs } = useCalendar()
  const [empSort, setEmpSort] = useState('asc')

  const rows = useMemo(() => {
    const flat = data.departments.flatMap((d) => d.employees)
    return [...flat].sort((a, b) => {
      const cmp = a.employeeName.localeCompare(b.employeeName)
      return empSort === 'asc' ? cmp : -cmp
    })
  }, [data, empSort])

  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm">
      <table className="min-w-full text-sm">
        <thead>
          <tr className="border-b border-slate-200">
            {/* Sticky Employee ID */}
            <th className="sticky left-0 z-20 bg-slate-50 px-4 py-3 text-left text-xs font-semibold text-slate-500 whitespace-nowrap w-[115px] min-w-[115px] max-w-[115px]">
              Employee ID
            </th>

            {/* Sticky Employee Name — sortable */}
            <th className="sticky left-[115px] z-20 bg-slate-50 px-4 py-3 text-left text-xs font-semibold text-slate-700 whitespace-nowrap border-r border-slate-200 shadow-[3px_0_6px_-2px_rgba(0,0,0,0.07)]">
              <button
                onClick={() => setEmpSort((s) => (s === 'asc' ? 'desc' : 'asc'))}
                className="inline-flex items-center gap-1 transition-colors hover:text-slate-900"
                title={`Sort ${empSort === 'asc' ? 'Z→A' : 'A→Z'}`}
              >
                <span>Employee Name</span>
                <span className="text-[10px] text-sky-600">{empSort === 'asc' ? '▲' : '▼'}</span>
              </button>
            </th>

            <th className="px-3 py-3 text-center text-xs font-medium text-slate-500 whitespace-nowrap">Working Days</th>
            <th className="px-3 py-3 text-center text-xs font-medium text-slate-500 whitespace-nowrap">Present</th>
            <th className="px-3 py-3 text-center text-xs font-medium text-slate-500 whitespace-nowrap">Absent</th>

            {/* Date columns — click to zoom into that day */}
            {data.days.map((d) => (
              <th
                key={d.dateIso}
                className={`border-l border-slate-100 px-2.5 py-2 text-center text-xs font-medium whitespace-nowrap ${d.isHoliday ? 'bg-sky-50 text-sky-600' : 'text-slate-500'}`}
              >
                <button
                  onClick={() => onFocusDay(d.dateIso)}
                  className="block w-full hover:opacity-80"
                  title="Show only this day"
                >
                  <div className="font-semibold text-slate-700">{d.weekday}</div>
                  <div className="text-xs font-medium text-slate-800">
                    {isBs ? d.dateBs : d.dateIso}
                  </div>
                </button>
                {/* Holiday label (read-only, no toggle) */}
                {d.isHoliday && d.holidayName && (
                  <div className="mt-0.5 truncate text-[9px] text-sky-500 font-medium max-w-[50px] mx-auto" title={d.holidayName}>
                    {d.holidayName === 'Weekly off' ? 'off' : d.holidayName}
                  </div>
                )}
              </th>
            ))}
          </tr>
        </thead>

        <tbody className="divide-y divide-slate-100">
          {rows.map((e) => (
            <tr key={e.employeeId} className="hover:bg-slate-50/70 transition-colors group">
              {/* Sticky Employee ID */}
              <td className="sticky left-0 z-10 bg-white group-hover:bg-slate-50/90 px-4 py-2.5 font-mono text-sm text-slate-600 whitespace-nowrap w-[115px] min-w-[115px] max-w-[115px] transition-colors">
                {e.biometricUserId || '—'}
              </td>

              {/* Sticky Employee Name (no avatar icon) */}
              <td className="sticky left-[115px] z-10 bg-white group-hover:bg-slate-50/90 px-4 py-2.5 border-r border-slate-200 shadow-[3px_0_6px_-2px_rgba(0,0,0,0.07)] transition-colors">
                <div className="font-semibold text-slate-800 whitespace-nowrap">{e.employeeName}</div>
                {e.branchName && <div className="text-xs text-slate-400 truncate">{e.branchName}</div>}
              </td>

              <td className="px-3 py-2.5 text-center text-sm font-medium text-slate-700">{e.totalDays}</td>
              <td className="px-3 py-2.5 text-center text-sm font-semibold text-emerald-600">{e.presentDays}</td>
              <td className="px-3 py-2.5 text-center text-sm font-semibold text-rose-500">{e.absentDays}</td>
              {data.days.map((d) => (
                <AttendanceCell
                  key={d.dateIso}
                  cell={e.cells[d.dateIso]}
                  day={d}
                  onClick={() =>
                    onCellClick({
                      employeeId: e.employeeId,
                      employeeName: e.employeeName,
                      date: d.dateIso,
                      dateBs: d.dateBs,
                    })
                  }
                />
              ))}
            </tr>
          ))}

          {rows.length === 0 && (
            <tr>
              <td colSpan={5 + (data.days?.length ?? 0)} className="px-4 py-8 text-center text-slate-400">
                No employees found.
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
