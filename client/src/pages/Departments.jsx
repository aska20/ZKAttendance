import { useState } from 'react'
import { FaEdit } from 'react-icons/fa'
import { RiDeleteBin5Line } from 'react-icons/ri'
import { departments } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Button, Table, Modal, Field, Input, Select, ErrorText, Badge } from '../components/ui'
import { useFeedback } from '../components/feedback'

const empty = { departmentName: '', departmentCode: '', parentDepartmentId: '', description: '', isActive: true }

export default function Departments() {
  const fb = useFeedback()
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
    const ok = await fb.confirm({
      title: 'Delete department',
      message: `Delete "${d.departmentName}"?`,
      confirmText: 'Delete',
      danger: true,
    })
    if (!ok) return
    try {
      await departments.remove(d.departmentId)
      fb.success('Department deleted')
      reload()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    }
  }

  const columns = [
    { key: 'departmentName', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.departmentName}</span> },
    { key: 'departmentCode', header: 'Code', render: (r) => r.departmentCode || '—' },
    { key: 'parent', header: 'Parent', render: (r) => list.find((x) => x.departmentId === r.parentDepartmentId)?.departmentName || '—' },
    { key: 'isActive', header: 'Status', render: (r) => <Badge tone={r.isActive ? 'green' : 'slate'}>{r.isActive ? 'Active' : 'Inactive'}</Badge> },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (r) => (
        <div className="flex items-center justify-end gap-1">
          <button
            type="button"
            onClick={() => openEdit(r)}
            title="Edit department"
            className="rounded p-1.5 text-slate-500 hover:bg-sky-50 hover:text-sky-600 transition-colors cursor-pointer"
          >
            <FaEdit className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={() => remove(r)}
            title="Delete department"
            className="rounded p-1.5 text-red-500 hover:bg-red-50 hover:text-red-700 transition-colors cursor-pointer"
          >
            <RiDeleteBin5Line className="h-4 w-4 text-red-500" />
          </button>
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
                <option value="">None</option>
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
            <div className="sticky bottom-0 -mx-4 sm:-mx-6 -mb-4 sm:-mb-6 px-4 sm:px-6 py-3 bg-white/95 backdrop-blur-xs flex items-center justify-end gap-2 border-t border-slate-100 z-10">
              <Button type="button" variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
