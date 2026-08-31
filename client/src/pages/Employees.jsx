import { useEffect, useMemo, useState } from 'react'
import {
  listEmployees,
  listDepartments,
  createEmployee,
  updateEmployee,
  deactivateEmployee,
} from '../api/employees'
import { apiErrorMessage } from '../lib/errors'

const emptyForm = {
  employeeName: '',
  biometricUserId: '',
  departmentId: '',
  phoneNumber: '',
  title: '',
  hireDate: '',
  isActive: true,
}

function toPayload(form) {
  return {
    employeeName: form.employeeName.trim(),
    biometricUserId: form.biometricUserId.trim() || null,
    departmentId: form.departmentId ? Number(form.departmentId) : null,
    phoneNumber: form.phoneNumber.trim() || null,
    title: form.title.trim() || null,
    hireDate: form.hireDate || null,
    isActive: form.isActive,
  }
}

export default function Employees() {
  const [employees, setEmployees] = useState([])
  const [departments, setDepartments] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [editing, setEditing] = useState(null) // employee object, or {} for new, or null
  const [form, setForm] = useState(emptyForm)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const deptName = useMemo(() => {
    const map = new Map(departments.map((d) => [d.departmentId, d.departmentName]))
    return (id) => map.get(id) || '—'
  }, [departments])

  async function refresh() {
    setLoading(true)
    setError('')
    try {
      const [emps, depts] = await Promise.all([listEmployees(), listDepartments()])
      setEmployees(emps)
      setDepartments(depts)
    } catch (err) {
      setError(apiErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    refresh()
  }, [])

  function openCreate() {
    setEditing({})
    setForm(emptyForm)
    setFormError('')
  }

  function openEdit(emp) {
    setEditing(emp)
    setForm({
      employeeName: emp.employeeName || '',
      biometricUserId: emp.biometricUserId || '',
      departmentId: emp.departmentId ? String(emp.departmentId) : '',
      phoneNumber: emp.phoneNumber || '',
      title: emp.title || '',
      hireDate: emp.hireDate ? emp.hireDate.slice(0, 10) : '',
      isActive: emp.isActive,
    })
    setFormError('')
  }

  function closeForm() {
    setEditing(null)
  }

  async function onSubmit(e) {
    e.preventDefault()
    setSaving(true)
    setFormError('')
    try {
      if (editing.employeeId) {
        await updateEmployee(editing.employeeId, toPayload(form))
      } else {
        await createEmployee(toPayload(form))
      }
      closeForm()
      await refresh()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not save the employee'))
    } finally {
      setSaving(false)
    }
  }

  async function onDeactivate(emp) {
    if (!confirm(`Deactivate ${emp.employeeName}?`)) return
    try {
      await deactivateEmployee(emp.employeeId)
      await refresh()
    } catch (err) {
      setError(apiErrorMessage(err))
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold text-slate-900">Employees</h1>
        <button
          onClick={openCreate}
          className="rounded-md bg-sky-600 px-3 py-2 text-sm font-medium text-white hover:bg-sky-700"
        >
          New employee
        </button>
      </div>

      {error && <p className="text-red-600">{error}</p>}

      <div className="overflow-hidden rounded-xl bg-white shadow-sm ring-1 ring-slate-200">
        <table className="w-full text-sm">
          <thead className="text-left text-slate-500">
            <tr className="border-b border-slate-100">
              <th className="px-5 py-2 font-medium">Name</th>
              <th className="px-5 py-2 font-medium">Biometric ID</th>
              <th className="px-5 py-2 font-medium">Department</th>
              <th className="px-5 py-2 font-medium">Title</th>
              <th className="px-5 py-2 font-medium">Status</th>
              <th className="px-5 py-2" />
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={6} className="px-5 py-4 text-slate-500">
                  Loading…
                </td>
              </tr>
            ) : employees.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-5 py-4 text-slate-500">
                  No employees yet.
                </td>
              </tr>
            ) : (
              employees.map((emp) => (
                <tr key={emp.employeeId} className="border-b border-slate-50 last:border-0">
                  <td className="px-5 py-2 font-medium text-slate-800">{emp.employeeName}</td>
                  <td className="px-5 py-2">{emp.biometricUserId}</td>
                  <td className="px-5 py-2">{deptName(emp.departmentId)}</td>
                  <td className="px-5 py-2">{emp.title || '—'}</td>
                  <td className="px-5 py-2">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        emp.isActive
                          ? 'bg-green-100 text-green-700'
                          : 'bg-slate-100 text-slate-500'
                      }`}
                    >
                      {emp.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="px-5 py-2 text-right whitespace-nowrap">
                    <button
                      onClick={() => openEdit(emp)}
                      className="text-sky-600 hover:underline"
                    >
                      Edit
                    </button>
                    {emp.isActive && (
                      <button
                        onClick={() => onDeactivate(emp)}
                        className="ml-3 text-red-600 hover:underline"
                      >
                        Deactivate
                      </button>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {editing && (
        <div className="fixed inset-0 z-10 flex items-center justify-center bg-slate-900/40 p-4">
          <form
            onSubmit={onSubmit}
            className="w-full max-w-md space-y-4 rounded-xl bg-white p-6 shadow-lg"
          >
            <h2 className="text-lg font-semibold text-slate-900">
              {editing.employeeId ? 'Edit employee' : 'New employee'}
            </h2>

            {formError && (
              <div className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 ring-1 ring-red-200">
                {formError}
              </div>
            )}

            <Field label="Name" required>
              <input
                value={form.employeeName}
                onChange={(e) => setForm({ ...form, employeeName: e.target.value })}
                required
                className="input"
              />
            </Field>

            <Field label="Biometric ID" hint="Leave blank to auto-assign the next free number">
              <input
                value={form.biometricUserId}
                onChange={(e) => setForm({ ...form, biometricUserId: e.target.value })}
                className="input"
              />
            </Field>

            <Field label="Department">
              <select
                value={form.departmentId}
                onChange={(e) => setForm({ ...form, departmentId: e.target.value })}
                className="input"
              >
                <option value="">—</option>
                {departments.map((d) => (
                  <option key={d.departmentId} value={d.departmentId}>
                    {d.departmentName}
                  </option>
                ))}
              </select>
            </Field>

            <div className="grid grid-cols-2 gap-3">
              <Field label="Title">
                <input
                  value={form.title}
                  onChange={(e) => setForm({ ...form, title: e.target.value })}
                  className="input"
                />
              </Field>
              <Field label="Phone">
                <input
                  value={form.phoneNumber}
                  onChange={(e) => setForm({ ...form, phoneNumber: e.target.value })}
                  className="input"
                />
              </Field>
            </div>

            <Field label="Hire date (AD)">
              <input
                type="date"
                value={form.hireDate}
                onChange={(e) => setForm({ ...form, hireDate: e.target.value })}
                className="input"
              />
            </Field>

            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              />
              Active
            </label>

            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={closeForm}
                className="rounded-md px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={saving}
                className="rounded-md bg-sky-600 px-3 py-2 text-sm font-medium text-white hover:bg-sky-700 disabled:opacity-60"
              >
                {saving ? 'Saving…' : 'Save'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  )
}

function Field({ label, hint, required, children }) {
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
