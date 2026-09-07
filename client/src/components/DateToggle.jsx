import { useCalendar } from '../context/CalendarContext'

export default function DateToggle({ className = '' }) {
  const { mode, setMode } = useCalendar()

  return (
    <div className={`inline-flex items-center rounded-lg bg-slate-100 p-0.5 text-xs font-semibold ring-1 ring-slate-200 ${className}`}>
      <button
        type="button"
        onClick={() => setMode('AD')}
        className={`rounded px-2.5 py-1 transition ${
          mode === 'AD'
            ? 'bg-sky-600 text-white shadow-xs'
            : 'text-slate-600 hover:text-slate-900'
        }`}
      >
        AD
      </button>
      <button
        type="button"
        onClick={() => setMode('BS')}
        className={`rounded px-2.5 py-1 transition ${
          mode === 'BS'
            ? 'bg-sky-600 text-white shadow-xs'
            : 'text-slate-600 hover:text-slate-900'
        }`}
      >
        BS
      </button>
    </div>
  )
}
