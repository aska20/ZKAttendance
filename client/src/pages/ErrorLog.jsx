import { useEffect, useState } from 'react'
import { errorLog } from '../api/resources'
import { useAuth } from '../context/AuthContext'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Table, Button, Badge, ErrorText } from '../components/ui'
import { dateTime, ymd } from '../lib/dates'

const TABS = [
  ['false', 'Open'],
  ['true', 'Resolved'],
  ['', 'All'],
]

export default function ErrorLog() {
  const { isAdmin } = useAuth()
  const [tab, setTab] = useState('false')
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  function load() {
    setLoading(true); setError('')
    const params = { page }
    if (tab !== '') params.resolved = tab
    errorLog.list(params)
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
  }

  useEffect(load, [tab, page]) // eslint-disable-line react-hooks/exhaustive-deps

  async function resolve(id) {
    const note = prompt('Resolution note (optional):') ?? ''
    try { await errorLog.resolve(id, note); load() }
    catch (e) { setError(apiErrorMessage(e)) }
  }

  async function clearResolved() {
    if (!confirm('Delete all resolved errors?')) return
    try { await errorLog.clearResolved(); load() }
    catch (e) { setError(apiErrorMessage(e)) }
  }

  const columns = [
    { key: 'ErrorDateTime', header: 'When', render: (r) => dateTime(r.errorDateTime) },
    { key: 'device', header: 'Device', render: (r) => <div>{r.device || `#${r.deviceId}`}<div className="text-xs text-slate-400">{r.deviceIp}</div></div> },
    { key: 'severity', header: 'Severity', render: (r) => <Badge tone={r.severity === 'High' ? 'red' : r.severity === 'Medium' ? 'amber' : 'slate'}>{r.severity}</Badge> },
    { key: 'errorMessage', header: 'Message', render: (r) => <span className="text-slate-700">{r.errorMessage}</span> },
    {
      key: 'status', header: 'Status', render: (r) => r.isResolved
        ? <span className="text-xs text-slate-400">resolved {r.resolvedDateTime ? ymd(r.resolvedDateTime) : ''}</span>
        : <button onClick={() => resolve(r.errorId)} className="text-sky-600 hover:underline">Mark resolved</button>,
    },
  ]

  return (
    <div>
      <PageHeader
        title="Error Log"
        subtitle="Device connection & sync failures — bad IP, wrong port, wrong Comm Key, offline device"
        actions={isAdmin && <Button variant="secondary" onClick={clearResolved}>Clear resolved</Button>}
      />

      {error && <ErrorText>{error}</ErrorText>}

      <Card className="mb-4 p-3">
        <div className="flex gap-2 text-sm">
          {TABS.map(([v, l]) => (
            <button
              key={v}
              onClick={() => { setTab(v); setPage(1) }}
              className={`rounded-md px-3 py-1.5 font-medium ${tab === v ? 'bg-sky-600 text-white' : 'text-slate-600 hover:bg-slate-100'}`}
            >
              {l}
            </button>
          ))}
        </div>
      </Card>

      <Table
        columns={columns}
        rows={(data?.items || []).map((r) => ({ ...r, _key: r.errorId }))}
        loading={loading}
        empty="No errors logged. 🎉"
      />

      {data && data.totalPages > 1 && (
        <div className="mt-4 flex items-center justify-center gap-3 text-sm">
          <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Prev</Button>
          <span>Page {data.page} of {data.totalPages}</span>
          <Button variant="secondary" disabled={page >= data.totalPages} onClick={() => setPage(page + 1)}>Next</Button>
        </div>
      )}
    </div>
  )
}
