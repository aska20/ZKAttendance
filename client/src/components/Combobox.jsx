import { useEffect, useMemo, useRef, useState } from 'react'

/**
 * A real searchable dropdown: click to open, scrollable list, click again (or
 * outside) to close, type to filter. Not the native <datalist>.
 *
 * options: [{ value, label, hint? }]
 * value:   the selected option's value ('' = nothing)
 * onChange(value)
 */
export default function Combobox({ options, value, onChange, placeholder = 'Select…', allowClear = true }) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const rootRef = useRef(null)
  const listRef = useRef(null)

  const selected = options.find((o) => o.value === value) || null

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return options
    return options.filter(
      (o) => o.label.toLowerCase().includes(q) || (o.hint || '').toLowerCase().includes(q),
    )
  }, [options, query])

  // close on outside click
  useEffect(() => {
    if (!open) return
    function onDoc(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    return () => document.removeEventListener('mousedown', onDoc)
  }, [open])

  useEffect(() => {
    if (open) {
      setQuery('')
      setActive(0)
    }
  }, [open])

  useEffect(() => {
    const el = listRef.current?.children[active]
    if (el) el.scrollIntoView({ block: 'nearest' })
  }, [active])

  function choose(opt) {
    onChange(opt ? opt.value : '')
    setOpen(false)
  }

  function onKeyDown(e) {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter')) {
      setOpen(true)
      e.preventDefault()
      return
    }
    if (!open) return
    if (e.key === 'ArrowDown') { setActive((a) => Math.min(a + 1, filtered.length - 1)); e.preventDefault() }
    else if (e.key === 'ArrowUp') { setActive((a) => Math.max(a - 1, 0)); e.preventDefault() }
    else if (e.key === 'Enter') { if (filtered[active]) choose(filtered[active]); e.preventDefault() }
    else if (e.key === 'Escape') { setOpen(false) }
  }

  return (
    <div ref={rootRef} className="relative">
      <div
        className="flex items-center rounded-md border border-slate-300 bg-white focus-within:border-sky-500 focus-within:ring-1 focus-within:ring-sky-500"
        onKeyDown={onKeyDown}
      >
        <input
          className="w-full rounded-md bg-transparent px-3 py-2 outline-none"
          placeholder={placeholder}
          value={open ? query : (selected?.label ?? '')}
          onChange={(e) => { setQuery(e.target.value); if (!open) setOpen(true) }}
          onFocus={() => setOpen(true)}
          onClick={() => setOpen(true)}
        />
        {allowClear && selected && !open && (
          <button
            type="button"
            className="px-2 text-slate-400 hover:text-slate-600"
            onClick={() => onChange('')}
            title="Clear"
          >
            ×
          </button>
        )}
        <button
          type="button"
          className="px-2 text-slate-400"
          onClick={() => setOpen((o) => !o)}
          tabIndex={-1}
        >
          {open ? '▲' : '▼'}
        </button>
      </div>

      {open && (
        <ul
          ref={listRef}
          className="absolute z-30 mt-1 max-h-60 w-full overflow-y-auto rounded-md border border-slate-200 bg-white py-1 shadow-lg"
        >
          {allowClear && (
            <li>
              <button
                type="button"
                className={`block w-full px-3 py-1.5 text-left text-sm ${!value ? 'bg-sky-50 text-sky-700' : 'text-slate-500 hover:bg-slate-50'}`}
                onClick={() => choose(null)}
              >
                {placeholder}
              </button>
            </li>
          )}
          {filtered.length === 0 && (
            <li className="px-3 py-2 text-sm text-slate-400">No match</li>
          )}
          {filtered.map((o, i) => (
            <li key={o.value}>
              <button
                type="button"
                className={`block w-full px-3 py-1.5 text-left text-sm ${
                  i === active ? 'bg-sky-50' : ''
                } ${o.value === value ? 'font-medium text-sky-700' : 'text-slate-700 hover:bg-slate-50'}`}
                onMouseEnter={() => setActive(i)}
                onClick={() => choose(o)}
              >
                {o.label}
                {o.hint && <span className="ml-2 text-xs text-slate-400">{o.hint}</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
