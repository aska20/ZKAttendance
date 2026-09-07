import { useEffect, useMemo, useRef, useState } from 'react'

export default function EmployeeSelect({ employees = [], value, onChange }) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const ref = useRef(null)

  useEffect(() => {
    function handler(e) {
      if (ref.current && !ref.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  const filtered = useMemo(() => {
    const q = query.toLowerCase()
    return employees
      .filter(
        (e) =>
          e.employeeName.toLowerCase().includes(q) ||
          String(e.biometricUserId).includes(q),
      )
      .slice(0, 50)
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
      <div className="flex items-center rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm shadow-sm focus-within:border-slate-400 focus-within:ring-1 focus-within:ring-slate-300">
        <input
          className="flex-1 bg-transparent outline-none placeholder:text-slate-400"
          placeholder={selected ? selected.employeeName : 'All employees'}
          value={query}
          onChange={(e) => { setQuery(e.target.value); setOpen(true) }}
          onFocus={() => setOpen(true)}
        />
        {(selected || query) && (
          <button onClick={clear} className="ml-1 text-slate-400 hover:text-slate-600" title="Clear">
            ×
          </button>
        )}
        <span className="ml-1 text-slate-300">▾</span>
      </div>

      {open && (
        <ul className="absolute z-50 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-slate-200 bg-white py-1 shadow-lg">
          <li>
            <button className="w-full px-3 py-2 text-left text-sm text-slate-500 hover:bg-slate-50" onClick={clear}>
              All employees
            </button>
          </li>
          {filtered.length === 0 && (
            <li className="px-3 py-2 text-sm text-slate-400">No match</li>
          )}
          {filtered.map((e) => (
            <li key={e.employeeId}>
              <button
                className={`w-full px-3 py-2 text-left text-sm hover:bg-slate-50 ${String(e.employeeId) === String(value) ? 'bg-slate-100 font-medium' : ''}`}
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
