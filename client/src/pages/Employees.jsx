import { useEffect, useMemo, useState } from 'react'
import { employees as api, departments as deptApi, users as usersApi } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { useAuth } from '../context/AuthContext'
import { PageHeader, Card, Button, Table, Modal, Field, Input, Select, Badge, ErrorText } from '../components/ui'
import { useFeedback } from '../components/feedback'
import EmployeeCalendarModal from '../components/EmployeeCalendarModal'

const emptyForm = {
  employeeName: '', biometricUserId: '', departmentId: '',
  phoneNumber: '', title: '', email: '', hireDate: '', isActive: true,
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

export default function Employees() {
  const fb = useFeedback()
  const { isAdmin } = useAuth()

  const [employees, setEmployees] = useState([])
  const [departments, setDepartments] = useState([])
  const [unregistered, setUnregistered] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(emptyForm)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const [loginFor, setLoginFor] = useState(null)
  const [loginForm, setLoginForm] = useState({ username: '', password: '' })
  const [loginError, setLoginError] = useState('')
  const [calendarFor, setCalendarFor] = useState(null)

  const deptName = useMemo(() => {
    const map = new Map(departments.map((d) => [d.departmentId, d.departmentName]))
    return (id) => map.get(id) || '—'
  }, [departments])

  async function refresh() {
    setLoading(true); setError('')
    try {
      const [emps, depts, unreg] = await Promise.all([api.list(), deptApi.list(), api.unregistered()])
      setEmployees(emps)
      setDepartments(depts)
      setUnregistered(unreg)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }
  useEffect(() => { refresh() }, [])

  function openCreate(biometricUserId = '', linkDeviceIds = []) {
    setEditing({}); setForm({ ...emptyForm, biometricUserId, linkDeviceIds }); setFormError('')
  }
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
    setLoginForm({
      username: (emp.email || emp.employeeName || '').split('@')[0].toLowerCase().replace(/\s+/g, ''),
      password: '',
      role: 'Employee',
    })
    setLoginError('')
  }
  async function saveLogin(e) {
    e.preventDefault()
    setLoginError('')
    try {
      const res = await api.createLogin(loginFor.employeeId, {
        username: loginForm.username.trim(),
        password: loginForm.password,
        role: loginForm.role,
      })
      fb.success(res.adopted
        ? `Linked existing account "${res.username}" to ${loginFor.employeeName}`
        : `Login "${loginForm.username}" (${loginForm.role}) created for ${loginFor.employeeName}`)
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

  async function toggleLoginActive(r) {
    try {
      await usersApi.setActive(r.login.userId, !r.login.active)
      fb.success(r.login.active ? 'Login disabled' : 'Login enabled')
      refresh()
    } catch (err) { fb.error(apiErrorMessage(err)) }
  }
  async function changeLoginRole(r, role) {
    try {
      await usersApi.setRole(r.login.userId, role)
      fb.success(`Role set to ${role}`)
      refresh()
    } catch (err) { fb.error(apiErrorMessage(err)) }
  }

  const columns = [
    { key: 'id', header: 'ID', render: (r) => <span className="text-slate-400 tabular-nums">{r.employeeId}</span> },
    { key: 'name', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.employeeName}</span> },
    { key: 'bio', header: 'Biometric ID', render: (r) => r.biometricUserId },
    { key: 'dept', header: 'Department', render: (r) => deptName(r.departmentId) },
    { key: 'status', header: 'Status', render: statusBadge },
    {
      key: 'login', header: 'Login', render: (r) => {
        if (!r.login) {
          return (r.approvalStatus === 'Pending' || r.approvalStatus === 'Rejected')
            ? <span className="text-slate-300">—</span>
            : <button onClick={() => openLogin(r)} className="text-sky-600 hover:underline">Give login</button>
        }
        return (
          <div className="flex items-center gap-2">
            <span className="font-medium text-slate-700">{r.login.username}</span>
            <select
              value={r.login.role}
              onChange={(e) => changeLoginRole(r, e.target.value)}
              className="rounded border border-slate-200 bg-white px-1 py-0.5 text-xs"
            >
              <option>Employee</option><option>HR</option><option>Admin</option>
            </select>
            <button
              onClick={() => toggleLoginActive(r)}
              className={`text-xs ${r.login.active ? 'text-amber-600' : 'text-green-600'} hover:underline`}
            >
              {r.login.active ? 'disable' : 'enable'}
            </button>
          </div>
        )
      },
    },
    {
      key: 'actions', header: '', render: (r) => (
        <div className="text-right whitespace-nowrap text-xs">
          <button onClick={() => setCalendarFor(r)} className="text-sky-600 hover:underline">Calendar</button>
          <button onClick={() => openEdit(r)} className="ml-3 text-sky-600 hover:underline">Edit</button>
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
      <PageHeader title="Employees" actions={<Button onClick={() => openCreate()}>New employee</Button>} />

      {!isAdmin && (
        <Card className="mb-4 p-3 text-sm text-amber-800 ring-amber-200">
          Employees you add are sent to an admin for approval before they become active.
        </Card>
      )}

      {unregistered.length > 0 && (
        <Card className="mb-4 border-l-4 border-l-amber-400 p-4">
          <div className="text-sm font-medium text-slate-800">
            {unregistered.length} biometric ID{unregistered.length > 1 ? 's have' : ' has'} punched from a device but {unregistered.length > 1 ? 'are' : 'is'} not added here
          </div>
          <div className="mt-2 flex flex-wrap gap-2">
            {unregistered.map((u) => (
              <button
                key={u.biometricUserId}
                onClick={() => openCreate(u.biometricUserId, u.deviceIds)}
                className="rounded-full bg-amber-50 px-3 py-1 text-xs font-medium text-amber-700 ring-1 ring-amber-200 hover:bg-amber-100"
                title={`${u.punchCount} punch(es)`}
              >
                + Add {u.biometricUserId}
              </button>
            ))}
          </div>
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
            {form.linkDeviceIds?.length > 0 && (
              <div className="rounded-md bg-sky-50 px-3 py-2 text-xs text-sky-800 ring-1 ring-sky-200">
                Completing biometric ID <b>{form.biometricUserId}</b> — it will be linked to
                {' '}{form.linkDeviceIds.length} device{form.linkDeviceIds.length > 1 ? 's' : ''} so past and future punches attribute to this person.
              </div>
            )}
            <Field label="Name" required>
              <Input value={form.employeeName} onChange={(e) => setForm({ ...form, employeeName: e.target.value })} required />
            </Field>
            <Field label="Biometric ID" hint="The number the ZKTeco device knows them by. Blank = auto-assign.">
              <Input value={form.biometricUserId} onChange={(e) => setForm({ ...form, biometricUserId: e.target.value })} readOnly={form.linkDeviceIds?.length > 0} />
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
              Creates a login account linked 1:1 to this person.
            </p>
            <ErrorText>{loginError}</ErrorText>
            <Field label="Username" required>
              <Input value={loginForm.username} onChange={(e) => setLoginForm({ ...loginForm, username: e.target.value })} required minLength={3} />
            </Field>
            <Field label="Password" required hint="At least 8 characters">
              <Input type="password" value={loginForm.password} onChange={(e) => setLoginForm({ ...loginForm, password: e.target.value })} required minLength={8} />
            </Field>
            <Field label="Role" hint="Employee = sees only their own attendance. HR / Admin = manages the system.">
              <Select value={loginForm.role} onChange={(e) => setLoginForm({ ...loginForm, role: e.target.value })}>
                <option value="Employee">Employee</option>
                <option value="HR">HR</option>
                <option value="Admin">Admin</option>
              </Select>
            </Field>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="ghost" onClick={() => setLoginFor(null)}>Cancel</Button>
              <Button type="submit">Create login</Button>
            </div>
          </form>
        </Modal>
      )}

      {calendarFor && (
        <EmployeeCalendarModal
          employeeId={calendarFor.employeeId}
          employeeName={calendarFor.employeeName}
          onClose={() => setCalendarFor(null)}
        />
      )}
    </div>
  )
}
