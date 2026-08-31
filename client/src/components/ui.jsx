// Small shared UI primitives so the pages stay short and consistent.

export function PageHeader({ title, subtitle, actions }) {
  return (
    <div className="mb-6 flex items-start justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold text-slate-900">{title}</h1>
        {subtitle && <p className="mt-0.5 text-sm text-slate-500">{subtitle}</p>}
      </div>
      {actions && <div className="flex gap-2">{actions}</div>}
    </div>
  )
}

export function Button({ variant = 'primary', className = '', ...props }) {
  const styles = {
    primary: 'bg-sky-600 text-white hover:bg-sky-700',
    secondary: 'bg-white text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50',
    danger: 'bg-red-600 text-white hover:bg-red-700',
    ghost: 'text-slate-600 hover:bg-slate-100',
  }[variant]
  return (
    <button
      className={`rounded-md px-3 py-2 text-sm font-medium transition disabled:opacity-60 ${styles} ${className}`}
      {...props}
    />
  )
}

export function Card({ className = '', children }) {
  return (
    <div className={`rounded-xl bg-white shadow-sm ring-1 ring-slate-200 ${className}`}>
      {children}
    </div>
  )
}

export function Field({ label, hint, required, children }) {
  return (
    <label className="block text-sm">
      <span className="mb-1 block font-medium text-slate-700">
        {label}
        {required && <span className="text-red-500"> *</span>}
      </span>
      {children}
      {hint && <span className="mt-1 block text-xs text-slate-400">{hint}</span>}
    </label>
  )
}

export function Input(props) {
  return <input {...props} className={`input ${props.className || ''}`} />
}

export function Select(props) {
  return <select {...props} className={`input ${props.className || ''}`} />
}

export function Badge({ tone = 'slate', children }) {
  const tones = {
    green: 'bg-green-100 text-green-700',
    slate: 'bg-slate-100 text-slate-500',
    amber: 'bg-amber-100 text-amber-700',
    sky: 'bg-sky-100 text-sky-700',
    red: 'bg-red-100 text-red-700',
  }[tone]
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${tones}`}>{children}</span>
}

export function ErrorText({ children }) {
  if (!children) return null
  return (
    <div className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 ring-1 ring-red-200">
      {children}
    </div>
  )
}

export function Modal({ title, onClose, children, wide }) {
  return (
    <div
      className="fixed inset-0 z-20 flex items-center justify-center bg-slate-900/40 p-4"
      onClick={onClose}
    >
      <div
        className={`w-full ${wide ? 'max-w-2xl' : 'max-w-md'} space-y-4 rounded-xl bg-white p-6 shadow-lg`}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-semibold text-slate-900">{title}</h2>
        {children}
      </div>
    </div>
  )
}

export function Table({ columns, rows, empty = 'Nothing here yet.', loading }) {
  return (
    <Card className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="text-left text-slate-500">
          <tr className="border-b border-slate-100">
            {columns.map((c) => (
              <th key={c.key} className="px-5 py-2 font-medium whitespace-nowrap">
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {loading ? (
            <tr>
              <td colSpan={columns.length} className="px-5 py-4 text-slate-500">
                Loading…
              </td>
            </tr>
          ) : rows.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="px-5 py-4 text-slate-500">
                {empty}
              </td>
            </tr>
          ) : (
            rows.map((row, i) => (
              <tr key={row._key ?? i} className="border-b border-slate-50 last:border-0">
                {columns.map((c) => (
                  <td key={c.key} className="px-5 py-2 align-top">
                    {c.render ? c.render(row) : row[c.key]}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </Card>
  )
}
