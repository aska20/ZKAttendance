import { useEffect, useMemo, useState } from 'react'
import { employees as api, departments as deptApi } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { useAuth } from '../context/AuthContext'
import { PageHeader, Card, Button, Table, Modal, Field, Input, Select, Badge, ErrorText } from '../components/ui'
import { useFeedback } from '../components/feedback'

const emptyForm = {
  employeeName: '', biometricUserId: '', departmentId: '',
  phoneNumber: '', title: '', email: '', hireDate: '', isActive: true,
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
  }
}

export default function Employees() {
  const fb = useFeedback()
  const { isAdmin } = useAuth()

  const [employees, setEmployees] = useState([])
  const [departments, setDepartments] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(emptyForm)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const [loginFor, setLoginFor] = useState(null)
  const [loginForm, setLoginForm] = useState({ username: '', password: '' })
  const [loginError, setLoginError] = useState('')

  const deptName = useMemo(() => {
    const map = new Map(departments.map((d) => [d.departmentId, d.departmentName]))
    return (id) => map.get(id) || '—'
  }, [departments])

  async function refresh() {
    setLoading(true); setError('')
    try {
      const [emps, depts] = await Promise.all([api.list(), deptApi.list()])
      setEmployees(emps)
      setDepartments(depts)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }
  useEffect(() => { refresh() }, [])

  function openCreate() { setEditing({}); setForm(emptyForm); setFormError('') }
  function openEdit(emp) {
    setEditing(emp)
    setForm({
      employeeName: emp.employeeName || '', biometricUserId: emp.biometricUserId || '',
      departmentId: emp.departmentId ? String(emp.departmentId) : '',
      phoneNumber: emp.phoneNumber || '', title: emp.title || '', email: emp.email || '',
      hireDate: emp.hireDate ? emp.hireDate.slice(0, 10) : '', isActive: emp.isActive,
    })
    setFormError('')
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true); setFormError('')
    try {
      if (editing.employeeId) {
        await api.update(editing.employeeId, toPayload(form))
        fb.success('Employee updated')
      } else {
        await api.create(toPayload(form))
        fb.success(isAdmin ? 'Employee added' : 'Sent to an admin for approval')
      }
      setEditing(null)
      refresh()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not save the employee'))
    } finally {
      setSaving(false)
    }
  }

  async function onDeactivate(emp) {
    const ok = await fb.confirm({ title: 'Deactivate employee', message: `Deactivate ${emp.employeeName}? Their history is kept.`, confirmText: 'Deactivate' })
    if (!ok) return
    try { await api.deactivate(emp.employeeId); fb.success('Deactivated'); refresh() }
    catch (err) { fb.error(apiErrorMessage(err)) }
  }

  async function onDelete(emp) {
    const ok = await fb.confirm({
      title: 'Delete employee',
      message: `Permanently delete ${emp.employeeName}? Only works if they have no attendance records.`,
      confirmText: 'Delete', danger: true,
    })
    if (!ok) return
    try { await api.remove(emp.employeeId); fb.success('Deleted'); refresh() }
    catch (err) { fb.error(apiErrorMessage(err)) }
  }

  function openLogin(emp) {
    setLoginFor(emp)
    setLoginForm({ username: (emp.email || emp.employeeName || '').split('@')[0].toLowerCase().replace(/\s+/g, ''), password: '' })
    setLoginError('')
  }
  async function saveLogin(e) {
    e.preventDefault()
    setLoginError('')
    try {
      await api.createLogin(loginFor.employeeId, { username: loginForm.username.trim(), password: loginForm.password, email: loginFor.email })
      fb.success(`Login "${loginForm.username}" created for ${loginFor.employeeName}`)
      setLoginFor(null)
      refresh()
    } catch (err) {
      setLoginError(apiErrorMessage(err, 'Could not create the login'))
    }
  }

  const statusBadge = (emp) => {
    if (emp.approvalStatus === 'Pending') return <Badge tone="amber">Pending approval</Badge>
    if (emp.approvalStatus === 'Rejected') return <Badge tone="red">Rejected</Badge>
    return <Badge tone={emp.isActive ? 'green' : 'slate'}>{emp.isActive ? 'Active' : 'Inactive'}</Badge>
  }

  const columns = [
    { key: 'id', header: 'ID', render: (r) => <span className="text-slate-400 tabular-nums">{r.employeeId}</span> },
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.employeeName}</span> },
    { key: 'bio', header: 'Biometric ID', render: (r) => r.biometricUserId },
    { key: 'dept', header: 'Department', render: (r) => deptName(r.departmentId) },
    { key: 'title', header: 'Title', render: (r) => r.title || '—' },
    { key: 'login', header: 'Login', render: (r) => r.hasLogin ? <Badge tone="sky">Yes</Badge> : <span className="text-slate-300">—</span> },
    { key: 'status', header: 'Status', render: statusBadge },
    {
      key: 'actions', header: '', render: (r) => (
        <div className="text-right whitespace-nowrap text-xs">
          <button onClick={() => openEdit(r)} className="text-sky-600 hover:underline">Edit</button>
          {!r.hasLogin && r.approvalStatus === 'Approved' && (
            <button onClick={() => openLogin(r)} className="ml-3 text-sky-600 hover:underline">Give login</button>
          )}
          {r.isActive && (
            <button onClick={() => onDeactivate(r)} className="ml-3 text-amber-600 hover:underline">Deactivate</button>
          )}
          <button onClick={() => onDelete(r)} className="ml-3 text-red-600 hover:underline">Delete</button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader title="Employees" actions={<Button onClick={openCreate}>New employee</Button>} />

      {!isAdmin && (
        <Card className="mb-4 p-3 text-sm text-amber-800 ring-amber-200">
          Employees you add are sent to an admin for approval before they become active.
        </Card>
      )}
      {error && <ErrorText>{error}</ErrorText>}

      <Table
        columns={columns}
        rows={employees.map((r) => ({ ...r, _key: r.employeeId }))}
        loading={loading}
        empty="No employees yet."
      />

      {editing && (
        <Modal title={editing.employeeId ? 'Edit employee' : 'New employee'} onClose={() => setEditing(null)}>
          <form onSubmit={save} className="space-y-4">
            <ErrorText>{formError}</ErrorText>
            <Field label="Name" required>
              <Input value={form.employeeName} onChange={(e) => setForm({ ...form, employeeName: e.target.value })} required />
            </Field>
            <Field label="Biometric ID" hint="The number the ZKTeco device knows them by. Blank = auto-assign.">
              <Input value={form.biometricUserId} onChange={(e) => setForm({ ...form, biometricUserId: e.target.value })} />
            </Field>
            <Field label="Department">
              <Select value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
                <option value="">None</option>
                {departments.map((d) => <option key={d.departmentId} value={d.departmentId}>{d.departmentName}</option>)}
              </Select>
            </Field>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Title"><Input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} /></Field>
              <Field label="Phone"><Input value={form.phoneNumber} onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })} /></Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Email"><Input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></Field>
              <Field label="Hire date (AD)"><Input type="date" value={form.hireDate} onChange={(e) => setForm({ ...form, hireDate: e.target.value })} /></Field>
            </div>
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
              Active
            </label>
            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
            </div>
          </form>
        </Modal>
      )}

      {loginFor && (
        <Modal title={`Login for ${loginFor.employeeName}`} onClose={() => setLoginFor(null)}>
          <form onSubmit={saveLogin} className="space-y-4">
            <p className="text-sm text-slate-500">
              Creates an <b>Employee</b> role account linked to this person. They will be able to
              sign in and see only their own attendance.
            </p>
            <ErrorText>{loginError}</ErrorText>
            <Field label="Username" required>
              <Input value={loginForm.username} onChange={(e) => setLoginForm({ ...loginForm, username: e.target.value })} required minLength={3} />
            </Field>
            <Field label="Password" required hint="At least 8 characters">
              <Input type="password" value={loginForm.password} onChange={(e) => setLoginForm({ ...loginForm, password: e.target.value })} required minLength={8} />
            </Field>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="ghost" onClick={() => setLoginFor(null)}>Cancel</Button>
              <Button type="submit">Create login</Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
