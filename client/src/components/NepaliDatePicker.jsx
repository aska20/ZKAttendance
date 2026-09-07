/**
 * NepaliDatePicker
 *
 * A date-picker that respects the global CalendarContext mode:
 *  - AD mode  → native <input type="date"> capped at today
 *  - BS mode  → custom Bikram Sambat picker grid capped at today (BS)
 *
 * Props:
 *   value    string   ISO-8601 AD date (yyyy-mm-dd)  — always stored/emitted as AD internally
 *   onChange (isoAd: string) => void
 *   className string (optional, applied to wrapper)
 */

import { useEffect, useMemo, useRef, useState } from 'react'
import { MdCalendarMonth } from 'react-icons/md'
import { useCalendar } from '../context/CalendarContext'
import {
  adToBs,
  getDaysInBsMonth,
  BS_MONTH_DAYS,
  MONTH_NAMES_EN,
} from '../lib/nepaliCalendar'

// ── helpers ───────────────────────────────────────────────────────────────────

const pad = (n) => String(n).padStart(2, '0')

/**
 * Convert BS {year, month, day} → AD ISO string (yyyy-mm-dd).
 * Reference: 2023-04-14 AD = 2080 Baisakh 1 BS
 */
const REF_AD_MS = Date.UTC(2023, 3, 14) // 2023-04-14

function totalBsDays(year, month, day) {
  // Accumulate days from 2070-01-01 to the given BS date
  let total = 0
  for (let y = 2070; y < year; y++) {
    const months = BS_MONTH_DAYS[y]
    if (months) total += months.reduce((a, b) => a + b, 0)
  }
  const months = BS_MONTH_DAYS[year]
  if (months) {
    for (let m = 1; m < month; m++) total += months[m - 1]
  }
  total += day
  return total
}

const REF_TOTAL_BS = totalBsDays(2080, 1, 1)

export function bsToAdIso(bsYear, bsMonth, bsDay) {
  const diff = totalBsDays(bsYear, bsMonth, bsDay) - REF_TOTAL_BS
  const adMs = REF_AD_MS + diff * 86400000
  const ad = new Date(adMs)
  return `${ad.getUTCFullYear()}-${pad(ad.getUTCMonth() + 1)}-${pad(ad.getUTCDate())}`
}

const WEEKDAYS = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa']

// ── BS Picker Grid ─────────────────────────────────────────────────────────────

function BsPickerGrid({ selected, onSelect, maxBs }) {
  // selected / maxBs: { year, month, day } in BS
  const initYear = selected?.year ?? maxBs.year
  const initMonth = selected?.month ?? maxBs.month

  const [viewYear, setViewYear] = useState(initYear)
  const [viewMonth, setViewMonth] = useState(initMonth)

  // Sync when selected changes externally
  useEffect(() => {
    if (selected) {
      setViewYear(selected.year)
      setViewMonth(selected.month)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected?.year, selected?.month])

  const daysInMonth = getDaysInBsMonth(viewYear, viewMonth)

  // Available BS years: from 2070 up to maxBs.year
  const years = useMemo(() => {
    const list = []
    for (let y = 2070; y <= maxBs.year; y++) {
      list.push(y)
    }
    return list
  }, [maxBs.year])

  // Month options for viewYear: if viewYear === maxBs.year, cap at maxBs.month
  const months = useMemo(() => {
    const maxM = viewYear === maxBs.year ? maxBs.month : 12
    return MONTH_NAMES_EN.slice(0, maxM).map((name, idx) => ({
      name,
      month: idx + 1,
    }))
  }, [viewYear, maxBs.year, maxBs.month])

  // First weekday of viewYear/viewMonth
  const firstAdIso = bsToAdIso(viewYear, viewMonth, 1)
  const startWeekday = new Date(firstAdIso + 'T00:00:00').getDay() // 0=Sun

  function prevMonth() {
    if (viewMonth === 1) {
      if (viewYear > 2070) {
        setViewYear((y) => y - 1)
        setViewMonth(12)
      }
    } else {
      setViewMonth((m) => m - 1)
    }
  }

  function nextMonth() {
    if (viewYear === maxBs.year && viewMonth >= maxBs.month) return
    if (viewMonth === 12) {
      if (viewYear < maxBs.year) {
        setViewYear((y) => y + 1)
        setViewMonth(1)
      }
    } else {
      setViewMonth((m) => m + 1)
    }
  }

  const isPrevDisabled = viewYear === 2070 && viewMonth === 1
  const isNextDisabled = viewYear === maxBs.year && viewMonth >= maxBs.month

  const cells = useMemo(() => {
    const arr = []
    for (let i = 0; i < startWeekday; i++) arr.push(null)
    for (let d = 1; d <= daysInMonth; d++) arr.push(d)
    return arr
  }, [startWeekday, daysInMonth])

  function isDayDisabled(d) {
    if (viewYear > maxBs.year) return true
    if (viewYear === maxBs.year && viewMonth > maxBs.month) return true
    if (viewYear === maxBs.year && viewMonth === maxBs.month && d > maxBs.day) return true
    return false
  }

  function isDaySelected(d) {
    return selected &&
      selected.year === viewYear &&
      selected.month === viewMonth &&
      selected.day === d
  }

  const isToday = (d) =>
    viewYear === maxBs.year && viewMonth === maxBs.month && d === maxBs.day

  return (
    <div className="w-72 max-w-[calc(100vw-2.5rem)] rounded-xl border border-slate-200 bg-white p-3 shadow-xl ring-1 ring-slate-900/5">
      {/* Month / Year nav with quick jump dropdowns */}
      <div className="mb-2 flex items-center justify-between gap-1">
        <button
          type="button"
          onClick={prevMonth}
          disabled={isPrevDisabled}
          className="rounded p-1 text-slate-500 hover:bg-slate-100 hover:text-slate-800 disabled:opacity-30 transition shrink-0"
          title="Previous month"
        >
          ‹
        </button>

        <div className="flex items-center gap-1.5 min-w-0">
          <select
            value={viewMonth}
            onChange={(e) => setViewMonth(Number(e.target.value))}
            className="rounded border border-slate-200 bg-white px-1.5 py-0.5 text-xs font-semibold text-slate-800 outline-none focus:border-sky-500 cursor-pointer"
          >
            {months.map((m) => (
              <option key={m.month} value={m.month}>
                {m.name}
              </option>
            ))}
          </select>

          <select
            value={viewYear}
            onChange={(e) => {
              const ny = Number(e.target.value)
              setViewYear(ny)
              if (ny === maxBs.year && viewMonth > maxBs.month) {
                setViewMonth(maxBs.month)
              }
            }}
            className="rounded border border-slate-200 bg-white px-1.5 py-0.5 text-xs font-semibold text-slate-800 outline-none focus:border-sky-500 cursor-pointer"
          >
            {years.map((y) => (
              <option key={y} value={y}>
                {y} BS
              </option>
            ))}
          </select>
        </div>

        <button
          type="button"
          onClick={nextMonth}
          disabled={isNextDisabled}
          className="rounded p-1 text-slate-500 hover:bg-slate-100 hover:text-slate-800 disabled:opacity-30 transition shrink-0"
          title="Next month"
        >
          ›
        </button>
      </div>

      {/* Weekday headers */}
      <div className="mb-1 grid grid-cols-7 text-center text-[10px] font-medium text-slate-400">
        {WEEKDAYS.map((w) => <div key={w}>{w}</div>)}
      </div>

      {/* Day grid */}
      <div className="grid grid-cols-7 gap-px">
        {cells.map((d, i) => {
          if (d === null) return <div key={`empty-${i}`} />
          const disabled = isDayDisabled(d)
          const sel = isDaySelected(d)
          const today = isToday(d)
          return (
            <button
              key={d}
              type="button"
              disabled={disabled}
              onClick={() => onSelect(viewYear, viewMonth, d)}
              className={[
                'rounded-md py-1 text-xs font-medium transition',
                sel ? 'bg-sky-600 text-white shadow-sm font-semibold' : '',
                !sel && today ? 'ring-1 ring-sky-400 text-sky-700 font-semibold' : '',
                !sel && !disabled ? 'hover:bg-sky-50 text-slate-700' : '',
                disabled ? 'text-slate-300 cursor-not-allowed' : '',
              ].join(' ')}
            >
              {d}
            </button>
          )
        })}
      </div>
    </div>
  )
}

// ── Main Component ─────────────────────────────────────────────────────────────

export default function NepaliDatePicker({ value, onChange, className = '' }) {
  const { isBs } = useCalendar()
  const [open, setOpen] = useState(false)
  const [placement, setPlacement] = useState({ v: 'down', h: 'left' })
  const wrapRef = useRef(null)
  const adInputRef = useRef(null)

  const todayIso = useMemo(() => new Date().toISOString().slice(0, 10), [])

  // Close on outside click
  useEffect(() => {
    function handler(e) {
      if (wrapRef.current && !wrapRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  // Measure available space and determine best popup direction
  useEffect(() => {
    if (!open || !wrapRef.current) return

    function checkPosition() {
      if (!wrapRef.current) return
      const rect = wrapRef.current.getBoundingClientRect()
      const spaceBelow = window.innerHeight - rect.bottom
      const spaceAbove = rect.top
      const pickerHeight = 310

      // If not enough room below, but more room above, flip up
      const v = spaceBelow < pickerHeight && spaceAbove > spaceBelow ? 'up' : 'down'

      // Check horizontal overflow (picker width is ~288px)
      const h = rect.left + 290 > window.innerWidth && rect.right >= 280 ? 'right' : 'left'

      setPlacement({ v, h })
    }

    checkPosition()
    window.addEventListener('scroll', checkPosition, true)
    window.addEventListener('resize', checkPosition)
    return () => {
      window.removeEventListener('scroll', checkPosition, true)
      window.removeEventListener('resize', checkPosition)
    }
  }, [open])

  // BS representations
  const selectedBs = useMemo(() => (value ? adToBs(value) : null), [value])
  const todayBs = useMemo(() => adToBs(todayIso), [todayIso])

  // Display label for the trigger button
  const displayValue = useMemo(() => {
    if (!value) return 'Select date'
    if (isBs && selectedBs) return `${selectedBs.dateBs} BS`
    return `${value} AD`
  }, [value, isBs, selectedBs])

  function handleBsSelect(year, month, day) {
    const adIso = bsToAdIso(year, month, day)
    onChange(adIso)
    setOpen(false)
  }

  // ── AD mode: styled button over native input ────────────────────
  if (!isBs) {
    return (
      <div className={`relative ${className}`}>
        <button
          type="button"
          onClick={() => {
            try {
              adInputRef.current?.showPicker?.()
            } catch {
              adInputRef.current?.focus()
            }
          }}
          className="input flex w-full items-center justify-between text-left cursor-pointer"
        >
          <span className={value ? 'text-slate-800' : 'text-slate-400 text-sm'}>
            {value ? `${value} AD` : 'Select date'}
          </span>
          <MdCalendarMonth className="ml-2 text-slate-400 h-4.5 w-4.5 shrink-0" />
        </button>
        <input
          ref={adInputRef}
          type="date"
          value={value || ''}
          max={todayIso}
          onChange={(e) => onChange(e.target.value)}
          className="absolute inset-0 h-full w-full opacity-0 cursor-pointer"
          tabIndex={-1}
        />
      </div>
    )
  }

  // ── BS mode: custom dropdown picker with collision flip ───────────────────
  return (
    <div ref={wrapRef} className={`relative ${className}`}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="input flex w-full items-center justify-between text-left cursor-pointer"
      >
        <span className={value ? 'text-slate-800 font-medium' : 'text-slate-400 text-sm'}>
          {displayValue}
        </span>
        <MdCalendarMonth className="ml-2 text-slate-400 h-4.5 w-4.5 shrink-0" />
      </button>

      {open && (
        <div
          className={`absolute z-50 ${
            placement.v === 'up' ? 'bottom-full mb-1.5' : 'top-full mt-1.5'
          } ${placement.h === 'right' ? 'right-0' : 'left-0'}`}
        >
          <BsPickerGrid
            selected={selectedBs}
            onSelect={handleBsSelect}
            maxBs={todayBs}
          />
        </div>
      )}
    </div>
  )
}
