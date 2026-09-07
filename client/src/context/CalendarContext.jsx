import { createContext, useContext, useMemo, useState } from 'react'
import { adToBs } from '../lib/nepaliCalendar'
import { dmy, bsDmy, toDate } from '../lib/dates'

const CalendarContext = createContext(null)
const STORAGE_KEY = 'zk_calendar_mode' // 'BS' | 'AD'

export function CalendarProvider({ children }) {
  const [mode, setModeState] = useState(() => localStorage.getItem(STORAGE_KEY) || 'BS')

  const setMode = (newMode) => {
    const valid = newMode === 'AD' ? 'AD' : 'BS'
    setModeState(valid)
    localStorage.setItem(STORAGE_KEY, valid)
  }

  const toggleMode = () => setMode(mode === 'BS' ? 'AD' : 'BS')

  const value = useMemo(() => {
    const isBs = mode === 'BS'

    /**
     * The single formatter every screen uses. Always dd/mm/yyyy, in whichever
     * calendar the user has selected, with the system suffixed so a BS date is
     * never mistaken for an AD one.
     *
     *   formatDate('2026-09-07')                 → "22/05/2083 BS"
     *   formatDate('2026-09-07', '2083-05-22')   → same, without re-converting
     */
    const formatDate = (dateInput, precomputedBs) => {
      if (!dateInput && !precomputedBs) return '—'
      if (isBs) {
        if (precomputedBs) return `${bsDmy(precomputedBs)} BS`
        const bs = adToBs(dateInput)
        if (bs) return `${bs.dmy} BS`
      }
      const out = dmy(dateInput)
      return out ? `${out} AD` : '—'
    }

    /** dd/mm/yyyy with no "BS"/"AD" suffix — for tight table cells. */
    const formatDateShort = (dateInput, precomputedBs) => {
      if (isBs) {
        if (precomputedBs) return bsDmy(precomputedBs)
        const bs = adToBs(dateInput)
        if (bs) return bs.dmy
      }
      return dmy(dateInput) || '—'
    }

    /** Both systems at once, for headers: "22/05/2083 BS · 07/09/2026 AD". */
    const formatDateBoth = (dateInput, precomputedBs) => {
      const ad = dmy(dateInput)
      const bs = precomputedBs ? bsDmy(precomputedBs) : adToBs(dateInput)?.dmy
      if (!ad && !bs) return '—'
      return isBs ? `${bs} BS · ${ad} AD` : `${ad} AD · ${bs} BS`
    }

    return {
      mode,
      isBs,
      isAd: mode === 'AD',
      label: isBs ? 'BS' : 'AD',
      setMode,
      toggleMode,
      formatDate,
      formatDateShort,
      formatDateBoth,
      toDate,
    }
  }, [mode])

  return <CalendarContext.Provider value={value}>{children}</CalendarContext.Provider>
}

export function useCalendar() {
  const ctx = useContext(CalendarContext)
  if (!ctx) throw new Error('useCalendar must be used within <CalendarProvider>')
  return ctx
}
