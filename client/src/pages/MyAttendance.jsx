import { useEffect, useState } from 'react'
import { attendance } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Table, Field, Select, ErrorText, Badge } from '../components/ui'
import { ymd, hm } from '../lib/dates'
import { useCalendar } from '../context/CalendarContext'
import DateToggle from '../components/DateToggle'

const QUICK = [
  ['last30days', 'Last 30 days'], ['last7days', 'Last 7 days'],
  ['thismonth', 'This month'], ['lastmonth', 'Last month'],
  ['thisweek', 'This week'],
]

export default function MyAttendance() {
  const { isBs } = useCalendar()
  const [quickFilter, setQuickFilter] = useState('last30days')
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    setLoading(true); setError('')
    attendance.my({ quickFilter })
      .then(setData)
      .catch((e) => setError(apiErrorMessage(e)))
      .finally(() => setLoading(false))
  }, [quickFilter])

  const columns = [
    {
      key: 'date',
      header: 'Date',
      render: (r) => (
        <span className="font-medium text-slate-800">
          {isBs ? `${r.nepaliDate} BS` : `${ymd(r.date)} AD`}
        </span>
      ),
    },
    { key: 'in', header: 'In', render: (r) => r.checkInTime ? hm(r.checkInTime) : '—' },
    { key: 'out', header: 'Out', render: (r) => r.checkOutTime ? hm(r.checkOutTime) : '—' },
    { key: 'hours', header: 'Hours', render: (r) => r.workingHours?.toFixed(2) ?? '0' },
    { key: 'status', header: 'Status', render: (r) => <Badge tone={r.status === 'Full Day' ? 'green' : 'amber'}>{r.status}</Badge> },
  ]

  return (
    <div>
      <PageHeader title="My Attendance" actions={<DateToggle />} />

      {error && <ErrorText>{error}</ErrorText>}

      {data && !data.linked && (
        <Card className="mb-4 p-4 text-sm text-amber-800 ring-amber-200">
          Your account is not linked to an employee record yet, so there is no attendance to
          show. Ask an administrator to link it from the Employees screen.
        </Card>
      )}

      <Card className="mb-4 p-4">
        <div className="flex items-end gap-3">
          <Field label="Period">
            <Select value={quickFilter} onChange={(e) => setQuickFilter(e.target.value)}>
              {QUICK.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
          </Field>
          {data?.linked && (
            <div className="text-sm text-slate-500">
              <span className="font-medium text-slate-800">{data.totalDays}</span> days ·{' '}
              <span className="font-medium text-slate-800">{data.totalHours?.toFixed(1)}</span> hours
            </div>
          )}
        </div>
      </Card>

      <Table
        columns={columns}
        rows={(data?.items || []).map((r, i) => ({ ...r, _key: `${r.date}-${i}` }))}
        loading={loading}
        empty="No attendance in this period."
      />
    </div>
  )
}
