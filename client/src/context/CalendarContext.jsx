import { createContext, useContext, useMemo, useState } from 'react'
import { adToBs } from '../lib/nepaliCalendar'

const CalendarContext = createContext(null)
const STORAGE_KEY = 'zk_calendar_mode' // 'BS' | 'AD'

const pad = (n) => String(n).padStart(2, '0')

export function CalendarProvider({ children }) {
  const [mode, setModeState] = useState(() => {
    return localStorage.getItem(STORAGE_KEY) || 'BS'
  })

  const setMode = (newMode) => {
    const valid = newMode === 'AD' ? 'AD' : 'BS'
    setModeState(valid)
    localStorage.setItem(STORAGE_KEY, valid)
  }

  const toggleMode = () => {
    setMode(mode === 'BS' ? 'AD' : 'BS')
  }

  const formatDate = useMemo(() => {
    return (dateInput) => {
      if (!dateInput) return '—'
      const d = dateInput instanceof Date ? dateInput : new Date(dateInput)
      if (Number.isNaN(d.getTime())) return String(dateInput)

      const bs = adToBs(d)
      if (mode === 'BS' && bs) {
        return `${bs.dateBs} BS`
      }
      const y = d.getFullYear()
      const m = pad(d.getMonth() + 1)
      const day = pad(d.getDate())
      return `${y}-${m}-${day} AD`
    }
  }, [mode])

  const value = useMemo(
    () => ({
      mode,
      isBs: mode === 'BS',
      isAd: mode === 'AD',
      setMode,
      toggleMode,
      formatDate,
    }),
    [mode, formatDate],
  )

  return <CalendarContext.Provider value={value}>{children}</CalendarContext.Provider>
}

export function useCalendar() {
  const ctx = useContext(CalendarContext)
  if (!ctx) throw new Error('useCalendar must be used within <CalendarProvider>')
  return ctx
}
