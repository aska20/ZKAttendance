import { useEffect, useState, useMemo, useCallback } from 'react'
import { Link } from 'react-router-dom'
import { getDashboardSummary } from '../api/dashboard'
import { employees as empApi } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { hm } from '../lib/dates'
import DateToggle from '../components/DateToggle'
import {
  FiUsers,
  FiUserCheck,
  FiUserX,
  FiClock,
  FiRefreshCw,
  FiAlertTriangle,
  FiArrowRight,
  FiSearch,
  FiCheckCircle,
  FiLogOut,
  FiLogIn,
  FiZap,
} from 'react-icons/fi'

// Color palette helper for fallback employee avatar badges
const AVATAR_COLORS = [
  'bg-emerald-100 text-emerald-700 ring-emerald-600/20',
  'bg-sky-100 text-sky-700 ring-sky-600/20',
  'bg-violet-100 text-violet-700 ring-violet-600/20',
  'bg-amber-100 text-amber-700 ring-amber-600/20',
  'bg-rose-100 text-rose-700 ring-rose-600/20',
  'bg-indigo-100 text-indigo-700 ring-indigo-600/20',
  'bg-teal-100 text-teal-700 ring-teal-600/20',
]

function getAvatarColor(name = '') {
  let hash = 0
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash)
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length]
}

function getInitials(name = '') {
  const parts = name.trim().split(/\s+/)
  if (parts.length === 0 || !parts[0]) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

function EmployeeAvatar({ photo, name }) {
  const [imgError, setImgError] = useState(false)

  useEffect(() => {
    setImgError(false)
  }, [photo])

  if (photo && !imgError) {
    return (
      <img
        src={photo}
        alt={name}
        onError={() => setImgError(true)}
        className="h-9 w-9 rounded-full object-cover ring-2 ring-slate-100 shadow-xs shrink-0"
      />
    )
  }

  return (
    <div
      className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-bold ring-1 shadow-2xs ${getAvatarColor(name)}`}
    >
      {getInitials(name)}
    </div>
  )
}

export default function Dashboard() {
  const [data, setData] = useState(null)
  const [employees, setEmployees] = useState([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [punchFilter, setPunchFilter] = useState('all') // 'all' | 'checkin' | 'checkout'
  const [searchQuery, setSearchQuery] = useState('')

  const loadData = useCallback(async (isManualRefresh = false) => {
    if (isManualRefresh) setRefreshing(true)
    else setLoading(true)
    setError('')

    try {
      const [summaryRes, empRes] = await Promise.all([
        getDashboardSummary(),
        empApi.list().catch(() => []),
      ])
      setData(summaryRes)
      setEmployees(empRes || [])
    } catch (err) {
      if (err?.response?.status === 403) {
        setError('Your account is not allowed to view the dashboard (Admin/HR only).')
      } else {
        setError(apiErrorMessage(err, 'Could not reach the API'))
      }
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    loadData()
  }, [loadData])

  // Map employee biometric user id and employee id for instant lookup
  const empMap = useMemo(() => {
    const map = { byBioId: {}, byId: {} }
    for (const e of employees) {
      if (e.biometricUserId) map.byBioId[String(e.biometricUserId).trim()] = e
      if (e.employeeId) map.byId[e.employeeId] = e
    }
    return map
  }, [employees])

  // Filtered recent punch rows
  const filteredPunches = useMemo(() => {
    if (!data?.recentLogs) return []
    return data.recentLogs.filter((l) => {
      const emp =
        empMap.byBioId[String(l.biometricUserId)?.trim()] ||
        empMap.byId[l.employeeId]
      const empName = l.employeeName || emp?.employeeName || ''
      const bioId = String(l.biometricUserId || '')
      const type = (l.attendanceType || '').toLowerCase()

      // Punch type filter
      if (punchFilter === 'checkin' && !type.includes('in')) return false
      if (punchFilter === 'checkout' && !type.includes('out')) return false

      // Search query
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase().trim()
        const matchName = empName.toLowerCase().includes(q)
        const matchBio = bioId.toLowerCase().includes(q)
        const matchType = type.includes(q)
        if (!matchName && !matchBio && !matchType) return false
      }

      return true
    })
  }, [data?.recentLogs, empMap, punchFilter, searchQuery])

  if (loading && !data) {
    return (
      <div className="flex min-h-[400px] items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <div className="h-8 w-8 animate-spin rounded-full border-3 border-sky-600 border-t-transparent" />
          <span className="text-sm font-medium text-slate-500">Loading dashboard...</span>
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="rounded-xl border border-rose-200 bg-rose-50 p-6 text-rose-800 shadow-sm">
        <div className="flex items-center gap-3 font-semibold text-rose-900">
          <FiAlertTriangle className="h-5 w-5 text-rose-600 shrink-0" />
          Unable to Load Dashboard
        </div>
        <p className="mt-1.5 text-sm text-rose-700">{error}</p>
        <button
          onClick={() => loadData(true)}
          className="mt-4 inline-flex items-center gap-2 rounded-lg bg-rose-600 px-4 py-2 text-xs font-semibold text-white shadow-sm hover:bg-rose-700 transition cursor-pointer"
        >
          <FiRefreshCw className="h-3.5 w-3.5" />
          Try Again
        </button>
      </div>
    )
  }

  const { counters, today } = data || {}
  const totalEmployees = counters?.totalEmployees || 0
  const presentCount = today?.present || 0
  const absentCount = today?.absent || 0
  const unattributedCount = today?.unattributed || 0

  return (
    <div className="space-y-6 max-w-7xl mx-auto pb-10">
      {/* ── Top Header Bar (No live badge, no date below) ───────────── */}
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold tracking-tight text-slate-900">Dashboard</h1>

        <div className="flex items-center gap-2.5">
          <DateToggle />
          <button
            onClick={() => loadData(true)}
            disabled={refreshing}
            title="Refresh dashboard data"
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 shadow-2xs hover:bg-slate-50 hover:text-slate-900 transition cursor-pointer disabled:opacity-60"
          >
            <FiRefreshCw className={`h-3.5 w-3.5 text-slate-500 ${refreshing ? 'animate-spin' : ''}`} />
            <span>Refresh</span>
          </button>
        </div>
      </div>

      {/* ── Unregistered Biometric IDs Alert ────────────────────────── */}
      {data.unregisteredCount > 0 && (
        <div className="relative overflow-hidden rounded-xl border border-amber-300/80 bg-gradient-to-r from-amber-50 via-amber-50/70 to-orange-50 p-4 shadow-sm">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="flex items-start gap-3">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-amber-500 text-white shadow-sm">
                <FiAlertTriangle className="h-5 w-5" />
              </div>
              <div>
                <h3 className="font-semibold text-amber-950 text-sm">
                  {data.unregisteredCount} Unregistered Biometric ID{data.unregisteredCount > 1 ? 's' : ''} Detected
                </h3>
                <p className="mt-0.5 text-xs text-amber-800">
                  Punches are being logged from the device for IDs that haven&apos;t been assigned to an employee yet.
                </p>
              </div>
            </div>
            <Link
              to="/unregistered"
              className="inline-flex items-center justify-center gap-1.5 rounded-lg bg-amber-600 px-3.5 py-1.5 text-xs font-semibold text-white shadow-sm hover:bg-amber-700 transition shrink-0"
            >
              <span>Review IDs</span>
              <FiArrowRight className="h-3.5 w-3.5" />
            </Link>
          </div>
        </div>
      )}

      {/* ── Primary Clean KPI Cards (No extra text, no percentages) ──── */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {/* Total Employees */}
        <div className="relative overflow-hidden rounded-2xl bg-white p-5 shadow-sm ring-1 ring-slate-200/80 transition-all hover:shadow-md">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-slate-500">
              Employees
            </span>
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-indigo-50 text-indigo-600 ring-1 ring-indigo-500/15">
              <FiUsers className="h-5 w-5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-3xl font-bold tracking-tight text-slate-900">{totalEmployees}</span>
          </div>
        </div>

        {/* Present Today */}
        <div className="relative overflow-hidden rounded-2xl bg-white p-5 shadow-sm ring-1 ring-emerald-200/60 bg-gradient-to-br from-white via-white to-emerald-50/30 transition-all hover:shadow-md">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-emerald-800">
              Present Today
            </span>
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-emerald-50 text-emerald-600 ring-1 ring-emerald-500/20">
              <FiUserCheck className="h-5 w-5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-3xl font-bold tracking-tight text-emerald-600">{presentCount}</span>
          </div>
        </div>

        {/* Absent Today */}
        <div className="relative overflow-hidden rounded-2xl bg-white p-5 shadow-sm ring-1 ring-rose-200/60 bg-gradient-to-br from-white via-white to-rose-50/30 transition-all hover:shadow-md">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold uppercase tracking-wider text-rose-800">
              Absent Today
            </span>
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-rose-50 text-rose-600 ring-1 ring-rose-500/20">
              <FiUserX className="h-5 w-5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-3xl font-bold tracking-tight text-rose-600">{absentCount}</span>
          </div>
        </div>
      </div>

      {/* ── Today's Punch Activity Table ───────────────────────────── */}
      <div className="overflow-hidden rounded-2xl bg-white shadow-sm ring-1 ring-slate-200/80">
        <div className="border-b border-slate-100 p-5">
          <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-base font-semibold text-slate-900">Today&apos;s Punches</h2>
                <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600">
                  {filteredPunches.length} recorded
                </span>
              </div>
            </div>

            {/* Filter controls */}
            <div className="flex flex-wrap items-center gap-2.5">
              {/* Search input */}
              <div className="relative min-w-[200px]">
                <FiSearch className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400 h-3.5 w-3.5" />
                <input
                  type="text"
                  placeholder="Filter by name or ID..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="w-full rounded-lg border border-slate-200 bg-slate-50/50 py-1.5 pl-8 pr-3 text-xs text-slate-800 placeholder-slate-400 outline-none focus:border-sky-500 focus:bg-white focus:ring-1 focus:ring-sky-500 transition"
                />
              </div>

              {/* Punch type pills */}
              <div className="flex items-center rounded-lg border border-slate-200 bg-slate-50 p-0.5 text-xs font-medium text-slate-600">
                <button
                  onClick={() => setPunchFilter('all')}
                  className={`rounded-md px-2.5 py-1 transition cursor-pointer ${punchFilter === 'all' ? 'bg-white text-slate-900 shadow-2xs font-semibold' : 'hover:text-slate-900'}`}
                >
                  All
                </button>
                <button
                  onClick={() => setPunchFilter('checkin')}
                  className={`rounded-md px-2.5 py-1 transition cursor-pointer ${punchFilter === 'checkin' ? 'bg-white text-emerald-700 shadow-2xs font-semibold' : 'hover:text-slate-900'}`}
                >
                  Check-in
                </button>
                <button
                  onClick={() => setPunchFilter('checkout')}
                  className={`rounded-md px-2.5 py-1 transition cursor-pointer ${punchFilter === 'checkout' ? 'bg-white text-sky-700 shadow-2xs font-semibold' : 'hover:text-slate-900'}`}
                >
                  Check-out
                </button>
              </div>
            </div>
          </div>
        </div>

        {filteredPunches.length === 0 ? (
          <div className="flex flex-col items-center justify-center p-12 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-full bg-slate-100 text-slate-400">
              <FiClock className="h-6 w-6" />
            </div>
            <p className="mt-3 text-sm font-semibold text-slate-700">No punches match the criteria</p>
            <p className="mt-0.5 text-xs text-slate-400">
              {searchQuery ? 'Try clearing your search query or changing filters' : 'Attendance punches will automatically appear here'}
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-100 bg-slate-50/60 text-xs font-semibold uppercase tracking-wider text-slate-500">
                <tr>
                  <th className="px-5 py-3">Employee</th>
                  <th className="px-5 py-3">Biometric ID</th>
                  <th className="px-5 py-3">Scan Time</th>
                  <th className="px-5 py-3">Punch Type</th>
                  <th className="px-5 py-3 text-right">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {filteredPunches.map((l) => {
                  const emp =
                    empMap.byBioId[String(l.biometricUserId)?.trim()] ||
                    empMap.byId[l.employeeId]
                  const empName = l.employeeName || emp?.employeeName || 'Unregistered Person'
                  const department = l.department || emp?.department?.departmentName || emp?.departmentName || ''
                  const photo = emp?.photoUrl || emp?.PhotoUrl
                  const isUnregistered = !l.employeeId && !emp
                  const type = (l.attendanceType || 'Punch').toLowerCase()

                  return (
                    <tr key={l.logId} className="hover:bg-slate-50/80 transition-colors group">
                      {/* Employee info with photo if available, and no '--' */}
                      <td className="px-5 py-3">
                        <div className="flex items-center gap-3">
                          <EmployeeAvatar photo={photo} name={empName} />
                          <div className="min-w-0">
                            <div className="font-semibold text-slate-900 truncate">
                              {empName}
                            </div>
                            {department && department !== '—' && department !== '--' && (
                              <div className="text-xs text-slate-400 truncate">
                                {department}
                              </div>
                            )}
                          </div>
                        </div>
                      </td>

                      {/* Biometric ID */}
                      <td className="px-5 py-3">
                        <span className="inline-flex items-center rounded-md bg-slate-100 px-2 py-1 font-mono text-xs font-medium text-slate-700">
                          #{l.biometricUserId || '—'}
                        </span>
                      </td>

                      {/* Scan Time (Only time, no date below) */}
                      <td className="px-5 py-3 whitespace-nowrap">
                        <div className="flex items-center gap-1.5 font-medium text-slate-800 text-sm">
                          <FiClock className="h-3.5 w-3.5 text-slate-400" />
                          <span>{hm(l.punchTimeAd)}</span>
                        </div>
                      </td>

                      {/* Punch Type Badge */}
                      <td className="px-5 py-3 whitespace-nowrap">
                        {type.includes('in') && !type.includes('overtime') ? (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700 ring-1 ring-emerald-600/20">
                            <FiLogIn className="h-3 w-3 text-emerald-600" />
                            Check-in
                          </span>
                        ) : type.includes('out') && !type.includes('overtime') ? (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-sky-50 px-2.5 py-1 text-xs font-medium text-sky-700 ring-1 ring-sky-600/20">
                            <FiLogOut className="h-3 w-3 text-sky-600" />
                            Check-out
                          </span>
                        ) : type.includes('overtime') ? (
                          <span className="inline-flex items-center gap-1.5 rounded-full bg-purple-50 px-2.5 py-1 text-xs font-medium text-purple-700 ring-1 ring-purple-600/20">
                            <FiZap className="h-3 w-3 text-purple-600" />
                            {l.attendanceType}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-700">
                            {l.attendanceType || 'Scan'}
                          </span>
                        )}
                      </td>

                      {/* Status */}
                      <td className="px-5 py-3 text-right whitespace-nowrap">
                        {isUnregistered ? (
                          <Link
                            to="/unregistered"
                            className="inline-flex items-center gap-1 rounded-full bg-amber-50 px-2.5 py-0.5 text-xs font-medium text-amber-800 ring-1 ring-amber-600/20 hover:bg-amber-100 transition"
                          >
                            <span>Unlinked</span>
                            <FiArrowRight className="h-3 w-3" />
                          </Link>
                        ) : (
                          <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-600">
                            <FiCheckCircle className="h-3.5 w-3.5" />
                            Verified
                          </span>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
