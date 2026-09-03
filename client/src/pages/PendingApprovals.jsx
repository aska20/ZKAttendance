import { employees } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { ymd } from '../lib/dates'
import { PageHeader, Table, Button, ErrorText } from '../components/ui'
import { useFeedback } from '../components/feedback'

export default function PendingApprovals() {
  const fb = useFeedback()
  const { data, loading, error, reload } = useAsync(() => employees.pending(), [])
  const rows = data || []

  async function approve(e) {
    try {
      await employees.approve(e.employeeId)
      fb.success(`${e.employeeName} approved`)
      reload()
    } catch (err) { fb.error(apiErrorMessage(err)) }
  }

  async function reject(e) {
    const reason = await fb.promptText({ title: `Reject ${e.employeeName}`, label: 'Reason (optional)' })
    if (reason === null) return
    try {
      await employees.reject(e.employeeId, reason)
      fb.success(`${e.employeeName} rejected`)
      reload()
    } catch (err) { fb.error(apiErrorMessage(err)) }
  }

  const columns = [
    { key: 'employeeName', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.employeeName}</span> },
    { key: 'biometricUserId', header: 'Biometric ID' },
    { key: 'title', header: 'Title', render: (r) => r.title || '—' },
    { key: 'requestedBy', header: 'Requested by', render: (r) => r.requestedBy || '—' },
    { key: 'createdDate', header: 'When', render: (r) => ymd(r.createdDate) },
    {
      key: 'actions', header: '', render: (r) => (
        <div className="text-right whitespace-nowrap">
          <Button onClick={() => approve(r)}>Approve</Button>
          <Button variant="ghost" className="ml-2 text-red-600" onClick={() => reject(r)}>Reject</Button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader title="Pending Approvals" subtitle="Employees added by HR, awaiting an admin decision" />
      {error && <ErrorText>{error}</ErrorText>}
      <Table
        columns={columns}
        rows={rows.map((r) => ({ ...r, _key: r.employeeId }))}
        loading={loading}
        empty="Nothing waiting for approval."
      />
    </div>
  )
}
