import { useEffect, useState } from 'react'
import { employees as api, departments as deptApi } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { useAuth } from '../context/AuthContext'
import { PageHeader, Card, Button, Table, Modal, Field, Input, Select, Badge, ErrorText } from '../components/ui'
import { useFeedback } from '../components/feedback'
import { useCalendar } from '../context/CalendarContext'
import DateToggle from '../components/DateToggle'
import { adToBs } from '../lib/nepaliCalendar'

const emptyForm = {
  employeeName: '',
  biometricUserId: '',
  departmentId: '',
  phoneNumber: '',
  title: '',
  email: '',
  hireDate: '',
  isActive: true,
  linkDeviceIds: [],
}

function toPayload(f) {
  return {
    employeeName: f.employeeName.trim(),
    biometricUserId: f.biometricUserId.trim() || null,
    departmentId: f.departmentId ? Number(f.departmentId) : null,
    phoneNumber: f.phoneNumber.trim() || null,
    title: f.title.trim() || null,
    email: f.email.trim() || null,
    hireDate: f.hireDate || null,
    isActive: f.isActive,
    linkDeviceIds: f.linkDeviceIds?.length ? f.linkDeviceIds : null,
  }
}

export default function UnregisteredIds() {
  const fb = useFeedback()
  const { isAdmin } = useAuth()
  const { isBs } = useCalendar()

  const [unregistered, setUnregistered] = useState([])
  const [departments, setDepartments] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [target, setTarget] = useState(null)
  const [form, setForm] = useState(emptyForm)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  async function loadData() {
    setLoading(true)
    setError('')
    try {
      const [unreg, depts] = await Promise.all([
        api.unregistered(),
        deptApi.list(),
      ])
      setUnregistered(unreg || [])
      setDepartments(depts || [])
    } catch (err) {
      setError(apiErrorMessage(err, 'Failed to load unregistered IDs'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData()
  }, [])

  function openCreate(row) {
    setTarget(row)
    setForm({
      ...emptyForm,
      biometricUserId: row.biometricUserId,
      linkDeviceIds: row.deviceIds || [],
    })
    setFormError('')
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true)
    setFormError('')
    try {
      await api.create(toPayload(form))
      fb.success(isAdmin ? `Employee created for Biometric ID ${form.biometricUserId}` : 'Sent to admin for approval')
      setTarget(null)
      loadData()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not create the employee'))
    } finally {
      setSaving(false)
    }
  }

  const columns = [
    {
      key: 'biometricUserId',
      header: 'Biometric ID',
      render: (r) => (
        <span className="font-semibold text-slate-800">
          #{r.biometricUserId}
        </span>
      ),
    },
    {
      key: 'punchCount',
      header: 'Punches Recorded',
      render: (r) => (
        <Badge tone="sky">
          {r.punchCount} punch{r.punchCount === 1 ? '' : 'es'}
        </Badge>
      ),
    },
    {
      key: 'devices',
      header: 'Seen on Device(s)',
      render: (r) => (
        <div className="flex flex-wrap gap-1">
          {r.deviceIds && r.deviceIds.length > 0 ? (
            r.deviceIds.map((id) => (
              <span key={id} className="rounded bg-slate-100 px-2 py-0.5 text-xs text-slate-700">
                Device #{id}
              </span>
            ))
          ) : (
            <span className="text-slate-400">—</span>
          )}
        </div>
      ),
    },
    {
      key: 'lastSeen',
      header: 'Last Seen',
      render: (r) => {
        if (!r.lastSeen) return '—'
        const d = new Date(r.lastSeen)
        if (isNaN(d.getTime())) return String(r.lastSeen)
        const time = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
        const isoDate = d.toISOString().slice(0, 10)
        const bs = adToBs(isoDate)
        return (
          <span className="text-slate-600 font-medium">
            {isBs && bs ? `${bs.dateBs} BS, ${time}` : `${isoDate} AD, ${time}`}
          </span>
        )
      },
    },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <div className="text-right">
          <Button size="sm" onClick={() => openCreate(r)}>
            + Create Employee
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Unregistered Biometric IDs"
        subtitle="Biometric IDs found in device scan logs that do not belong to any employee record yet"
        actions={
          <div className="flex items-center gap-2">
            <Button variant="secondary" onClick={loadData} disabled={loading}>↻ Refresh</Button>
            <DateToggle />
          </div>
        }
      />

      <Card className="mb-6 border-l-4 border-l-sky-500 p-4 text-sm text-slate-700">
        <p className="font-medium text-slate-900">How unregistered IDs work:</p>
        <p className="mt-1 text-slate-600">
          When people scan on a device before HR sets up their profile, punches are safely saved with their Biometric ID.
          Click <strong>Create Employee</strong> to assign a name and department. All past punches will be linked to that employee.
        </p>
      </Card>

      {error && <ErrorText>{error}</ErrorText>}

      <Table
        columns={columns}
        rows={unregistered.map((r) => ({ ...r, _key: r.biometricUserId }))}
        loading={loading}
        empty="No unregistered biometric IDs found. All device scans belong to registered employees!"
      />

      {target && (
        <Modal title={`Create Employee for Biometric ID #${form.biometricUserId}`} onClose={() => setTarget(null)}>
          <form onSubmit={save} className="space-y-4">
            <ErrorText>{formError}</ErrorText>

            <Field label="Employee Name" required>
              <Input
                value={form.employeeName}
                onChange={(e) => setForm({ ...form, employeeName: e.target.value })}
                placeholder="Full Name"
                autoFocus
                required
              />
            </Field>

            <Field label="Biometric ID (Device User ID)" required>
              <Input
                value={form.biometricUserId}
                onChange={(e) => setForm({ ...form, biometricUserId: e.target.value })}
                disabled
              />
            </Field>

            <Field label="Department">
              <Select
                value={form.departmentId}
                onChange={(e) => setForm({ ...form, departmentId: e.target.value })}
              >
                <option value="">(No department)</option>
                {departments.map((d) => (
                  <option key={d.departmentId} value={d.departmentId}>
                    {d.departmentName}
                  </option>
                ))}
              </Select>
            </Field>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Field label="Job Title">
                <Input
                  value={form.title}
                  onChange={(e) => setForm({ ...form, title: e.target.value })}
                  placeholder="e.g. Officer"
                />
              </Field>
              <Field label="Phone Number">
                <Input
                  value={form.phoneNumber}
                  onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })}
                  placeholder="e.g. 9800000000"
                />
              </Field>
            </div>

            <Field label="Email">
              <Input
                type="email"
                value={form.email}
                onChange={(e) => setForm({ ...form, email: e.target.value })}
                placeholder="email@example.com"
              />
            </Field>

            <div className="sticky bottom-0 -mx-4 sm:-mx-6 -mb-4 sm:-mb-6 px-4 sm:px-6 py-3 bg-white/95 backdrop-blur-xs flex items-center justify-end gap-2 border-t border-slate-100 z-10">
              <Button type="button" variant="ghost" onClick={() => setTarget(null)}>
                Cancel
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Creating…' : 'Create & Link Employee'}
              </Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
