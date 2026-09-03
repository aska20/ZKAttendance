import { useEffect, useState } from 'react'
import { errorLog } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Table, Button, Badge, ErrorText } from '../components/ui'
import { dateTime } from '../lib/dates'

export default function ErrorLog() {
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    setLoading(true); setError('')
    errorLog.list({ page })
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
  }, [page])

  const columns = [
    { key: 'ErrorDateTime', header: 'When', render: (r) => dateTime(r.errorDateTime) },
    { key: 'device', header: 'Device', render: (r) => <div>{r.device || `#${r.deviceId}`}<div className="text-xs text-slate-400">{r.deviceIp}</div></div> },
    { key: 'severity', header: 'Severity', render: (r) => <Badge tone={r.severity === 'High' ? 'red' : r.severity === 'Medium' ? 'amber' : 'slate'}>{r.severity}</Badge> },
    { key: 'errorMessage', header: 'Message', render: (r) => <span className="text-slate-700">{r.errorMessage}</span> },
  ]

  return (
    <div>
      <PageHeader title="Error Log" subtitle="Device connection and sync failures" />

      {error && <ErrorText>{error}</ErrorText>}

      <Table
        columns={columns}
        rows={(data?.items || []).map((r) => ({ ...r, _key: r.errorId }))}
        loading={loading}
        empty="No errors logged."
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
