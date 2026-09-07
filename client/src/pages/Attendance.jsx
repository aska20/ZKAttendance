import { useEffect, useMemo, useRef, useState } from 'react'
import * as XLSX from 'xlsx'
import { jsPDF } from 'jspdf'
import autoTable from 'jspdf-autotable'
import { overview, departments as deptApi, employees as empApi } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { ymd, hm } from '../lib/dates'
import { useCalendar } from '../context/CalendarContext'
import { PageHeader, ErrorText } from '../components/ui'
import { FaStar } from 'react-icons/fa'
import DateToggle from '../components/DateToggle'
import DayDetailModal from '../components/DayDetailModal'
import AttendanceFilters from '../components/attendance/AttendanceFilters'
import AttendanceTable from '../components/attendance/AttendanceTable'

const isoDay = (d) => ymd(d)

export default function Attendance() {
  const { isBs } = useCalendar()
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

  const [exportOpen, setExportOpen] = useState(false)
  const exportRef = useRef(null)

  const { data: deptList } = useAsync(() => deptApi.list(), [])
  const { data: allEmployees } = useAsync(() => empApi.list(), [])

  // Close export dropdown on outside click
  useEffect(() => {
    function handleClickOutside(e) {
      if (exportRef.current && !exportRef.current.contains(e.target)) {
        setExportOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  // Auto-fetch with 300 ms debounce whenever filters change
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

  function exportXlsx() {
    if (!data) return
    const head = ['Employee ID', 'Employee', 'Branch', 'Working Days', 'Present', 'Absent',
      ...data.days.map((d) => `${d.weekday} ${isBs ? d.dateBs : d.dateIso}`)]
    const rows = [head]
    for (const dept of data.departments) {
      for (const e of dept.employees) {
        rows.push([
          e.biometricUserId || '—', e.employeeName, e.branchName || '',
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
    const ws = XLSX.utils.aoa_to_sheet(rows)
    const wb = XLSX.utils.book_new()
    XLSX.utils.book_append_sheet(wb, ws, 'Attendance')
    XLSX.writeFile(wb, `attendance_${range.from}_to_${range.to}.xlsx`)
  }

  function exportPdf() {
    if (!data) return
    const doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' })

    // Header title & info
    doc.setFontSize(14)
    doc.setTextColor(15, 23, 42)
    doc.text('Employee Attendance Report', 14, 13)

    doc.setFontSize(8.5)
    doc.setTextColor(100, 116, 139)
    doc.text(`Period: ${range.from} to ${range.to} (${isBs ? 'BS' : 'AD'})  |  Generated: ${new Date().toLocaleDateString()}`, 14, 18)

    const head = [
      [
        'Employee ID',
        'Employee',
        'Branch',
        'Work Days',
        'Present',
        'Absent',
        ...data.days.map((d) => `${d.weekday}\n${isBs ? d.dateBs : d.dateIso}`),
      ],
    ]

    const body = []
    for (const dept of data.departments) {
      for (const e of dept.employees) {
        body.push([
          e.biometricUserId || '—',
          e.employeeName,
          e.branchName || '',
          String(e.totalDays),
          String(e.presentDays),
          String(e.absentDays),
          ...data.days.map((d) => {
            const c = e.cells[d.dateIso]
            if (!c) return '—'
            if (c.status === 'Holiday') return 'Holiday'
            if (c.status === 'Absent') return 'Absent'
            const time = hm(c.firstIn)
            return time ? `Present\n${time}` : 'Present'
          }),
        ])
      }
    }

    autoTable(doc, {
      head,
      body,
      startY: 22,
      styles: {
        fontSize: 6.5,
        cellPadding: 1.5,
        halign: 'center',
        valign: 'middle',
      },
      columnStyles: {
        0: { halign: 'left', fontStyle: 'bold', cellWidth: 26 },
        1: { halign: 'center', cellWidth: 18 },
        2: { halign: 'left', cellWidth: 20 },
        3: { halign: 'center', cellWidth: 14 },
        4: { halign: 'center', cellWidth: 14, textColor: [22, 101, 52] },
        5: { halign: 'center', cellWidth: 14, textColor: [185, 28, 28] },
      },
      headStyles: {
        fillColor: [15, 23, 42],
        textColor: [255, 255, 255],
        fontSize: 6.5,
        halign: 'center',
        valign: 'middle',
      },
      alternateRowStyles: {
        fillColor: [248, 250, 252],
      },
      margin: { top: 22, left: 10, right: 10, bottom: 10 },
    })

    doc.save(`attendance_${range.from}_to_${range.to}.pdf`)
  }

  return (
    <div>
      <PageHeader
        title="Attendance"
        actions={
          <div className="flex items-center gap-5">
            {/* Export Dropdown with .xlsx and .pdf */}
            <div className="relative" ref={exportRef}>
              <button
                type="button"
                onClick={() => setExportOpen((o) => !o)}
                disabled={!data}
                className="inline-flex items-center gap-1.5 rounded-lg bg-green-600 px-3 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-green-700 disabled:cursor-not-allowed disabled:opacity-40"
              >
                <span>Export</span>
                <span className="text-xs">▾</span>
              </button>

              {exportOpen && (
                <div className="absolute right-0 z-50 mt-1 w-28 rounded-md border border-slate-200 bg-white py-1 shadow-md">
                  <button
                    type="button"
                    onClick={() => {
                      setExportOpen(false)
                      exportXlsx()
                    }}
                    className="block w-full px-3 py-1.5 text-left text-sm font-medium text-slate-700 hover:bg-slate-100 hover:text-green-700 transition"
                  >
                    .xlsx
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setExportOpen(false)
                      exportPdf()
                    }}
                    className="block w-full px-3 py-1.5 text-left text-sm font-medium text-slate-700 hover:bg-slate-100 hover:text-rose-700 transition"
                  >
                    .pdf
                  </button>
                </div>
              )}
            </div>

            {/* AD / BS Mode Toggle with clear visual gap */}
            <DateToggle />
          </div>
        }
      />

      <AttendanceFilters
        range={range}
        onRangeChange={setRange}
        departmentId={departmentId}
        onDeptChange={setDepartmentId}
        employeeId={employeeId}
        onEmpChange={setEmployeeId}
        deptList={deptList || []}
        employees={allEmployees || []}
      />

      {error && <ErrorText>{error}</ErrorText>}
      {loading && <p className="py-2 text-sm text-slate-400">Loading…</p>}

      {data && !loading && (
        <>
          <AttendanceLegend />
          <AttendanceTable
            data={data}
            onFocusDay={(dateIso) => setRange({ from: dateIso, to: dateIso })}
            onCellClick={setDetail}
          />
        </>
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

function AttendanceLegend() {
  return (
    <div className="mb-3.5 flex flex-wrap items-center gap-2 sm:gap-2.5 text-xs select-none">
      <span className="font-semibold text-slate-800 mr-1 text-sm">Note:</span>

      {/* Present */}
      <span className="inline-flex items-center gap-1.5 rounded-md bg-emerald-50 px-2.5 py-1 font-medium text-emerald-700 ring-1 ring-emerald-200/80">
        <svg
          className="h-4 w-4 text-emerald-600 shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="m8.5 12.5 2.5 2.5 4.5-5" />
        </svg>
        <span>---&gt;</span>
        <span>Present</span>
      </span>

      {/* Absent */}
      <span className="inline-flex items-center gap-1.5 rounded-md bg-rose-50 px-2.5 py-1 font-medium text-rose-600 ring-1 ring-rose-200/80">
        <svg
          className="h-4 w-4 text-rose-500 shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="m15 9-6 6m0-6 6 6" />
        </svg>
        <span>---&gt;</span>
        <span>Absent</span>
      </span>

      {/* Holiday */}
      <span className="inline-flex items-center gap-1.5 rounded-md bg-amber-50 px-2.5 py-1 font-medium text-amber-700 ring-1 ring-amber-200/80">
        <FaStar className="h-3.5 w-3.5 text-amber-500 shrink-0" />
        <span>---&gt;</span>
        <span>Holiday</span>
      </span>

      {/* Half Day */}
      <span className="inline-flex items-center gap-1.5 rounded-md bg-amber-50 px-2.5 py-1 font-medium text-amber-700 ring-1 ring-amber-200/80">
        <svg
          className="h-4 w-4 text-amber-500 shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3 3" />
        </svg>
        <span>---&gt;</span>
        <span>Half Day</span>
      </span>

      {/* Day Off */}
      <span className="inline-flex items-center gap-1.5 rounded-md bg-sky-50 px-2.5 py-1 font-medium text-sky-700 ring-1 ring-sky-200/80">
        <svg
          className="h-4 w-4 text-sky-500 shrink-0"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <rect width="18" height="18" x="3" y="4" rx="2" />
          <path d="M16 2v4M8 2v4M3 10h18" />
        </svg>
        <span>---&gt;</span>
        <span>Day Off</span>
      </span>
    </div>
  )
}
