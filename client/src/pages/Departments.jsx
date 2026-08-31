import { useState } from 'react'
import { departments } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Button, Table, Modal, Field, Input, Select, ErrorText, Badge } from '../components/ui'

const empty = { departmentName: '', departmentCode: '', parentDepartmentId: '', description: '', isActive: true }

export default function Departments() {
  const { data, loading, error, reload } = useAsync(() => departments.list(), [])
  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(empty)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const list = data || []

  function openCreate() {
    setForm(empty); setFormError(''); setEditing({})
  }
  function openEdit(d) {
    setForm({
      departmentName: d.departmentName || '',
      departmentCode: d.departmentCode || '',
      parentDepartmentId: d.parentDepartmentId ? String(d.parentDepartmentId) : '',
      description: d.description || '',
      isActive: d.isActive,
    })
    setFormError(''); setEditing(d)
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true); setFormError('')
    const body = {
      ...form,
      parentDepartmentId: form.parentDepartmentId ? Number(form.parentDepartmentId) : null,
    }
    try {
      if (editing.departmentId) await departments.update(editing.departmentId, body)
      else await departments.create(body)
      setEditing(null)
      reload()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not save the department'))
    } finally {
      setSaving(false)
    }
  }

  async function remove(d) {
    if (!confirm(`Delete department "${d.departmentName}"?`)) return
    try {
      await departments.remove(d.departmentId)
      reload()
    } catch (err) {
      alert(apiErrorMessage(err))
    }
  }

  const columns = [
    { key: 'departmentName', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.departmentName}</span> },
    { key: 'departmentCode', header: 'Code', render: (r) => r.departmentCode || '—' },
    { key: 'parent', header: 'Parent', render: (r) => list.find((x) => x.departmentId === r.parentDepartmentId)?.departmentName || '—' },
    { key: 'isActive', header: 'Status', render: (r) => <Badge tone={r.isActive ? 'green' : 'slate'}>{r.isActive ? 'Active' : 'Inactive'}</Badge> },
    {
      key: 'actions', header: '', render: (r) => (
        <div className="text-right whitespace-nowrap">
          <button onClick={() => openEdit(r)} className="text-sky-600 hover:underline">Edit</button>
          <button onClick={() => remove(r)} className="ml-3 text-red-600 hover:underline">Delete</button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Departments"
        actions={<Button onClick={openCreate}>New department</Button>}
      />
      {error && <ErrorText>{error}</ErrorText>}
      <Table columns={columns} rows={list.map((r) => ({ ...r, _key: r.departmentId }))} loading={loading} empty="No departments yet." />

      {editing && (
        <Modal title={editing.departmentId ? 'Edit department' : 'New department'} onClose={() => setEditing(null)}>
          <form onSubmit={save} className="space-y-4">
            <ErrorText>{formError}</ErrorText>
            <Field label="Name" required>
              <Input value={form.departmentName} onChange={(e) => setForm({ ...form, departmentName: e.target.value })} required />
            </Field>
            <Field label="Code">
              <Input value={form.departmentCode} onChange={(e) => setForm({ ...form, departmentCode: e.target.value })} />
            </Field>
            <Field label="Parent department">
              <Select value={form.parentDepartmentId} onChange={(e) => setForm({ ...form, parentDepartmentId: e.target.value })}>
                <option value="">— none —</option>
                {list.filter((d) => d.departmentId !== editing.departmentId).map((d) => (
                  <option key={d.departmentId} value={d.departmentId}>{d.departmentName}</option>
                ))}
              </Select>
            </Field>
            <Field label="Description">
              <Input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
            </Field>
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
    </div>
  )
}
