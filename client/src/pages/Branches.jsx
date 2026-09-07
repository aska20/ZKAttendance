import { useState } from 'react'
import { FaEdit } from 'react-icons/fa'
import { RiDeleteBin5Line } from 'react-icons/ri'
import { branches } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Button, Table, Modal, Field, Input, ErrorText, Badge } from '../components/ui'
import { useFeedback } from '../components/feedback'

const empty = {
  branchCode: '', branchName: '', city: '', address: '',
  contactPerson: '', contactPhone: '', isActive: true,
}

export default function Branches() {
  const fb = useFeedback()
  const { data, loading, error, reload } = useAsync(() => branches.list(), [])
  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(empty)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const list = data || []

  function openCreate() { setForm(empty); setFormError(''); setEditing({}) }
  function openEdit(b) {
    setForm({
      branchCode: b.branchCode || '', branchName: b.branchName || '', city: b.city || '',
      address: b.address || '', contactPerson: b.contactPerson || '',
      contactPhone: b.contactPhone || '', isActive: b.isActive,
    })
    setFormError(''); setEditing(b)
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true); setFormError('')
    try {
      if (editing.branchId) await branches.update(editing.branchId, form)
      else await branches.create(form)
      setEditing(null); reload()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not save the branch'))
    } finally {
      setSaving(false)
    }
  }

  async function remove(b) {
    const ok = await fb.confirm({ title: 'Delete branch', message: `Delete "${b.branchName}"?`, confirmText: 'Delete', danger: true })
    if (!ok) return
    try { await branches.remove(b.branchId); fb.success('Branch deleted'); reload() }
    catch (err) { fb.error(apiErrorMessage(err)) }
  }

  const columns = [
    { key: 'branchName', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.branchName}</span> },
    { key: 'branchCode', header: 'Code' },
    { key: 'city', header: 'City', render: (r) => r.city || '—' },
    { key: 'contactPerson', header: 'Contact', render: (r) => r.contactPerson || '—' },
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
            title="Edit branch"
            className="rounded p-1.5 text-slate-500 hover:bg-sky-50 hover:text-sky-600 transition-colors cursor-pointer"
          >
            <FaEdit className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={() => remove(r)}
            title="Delete branch"
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
      <PageHeader title="Branches" actions={<Button onClick={openCreate}>New branch</Button>} />
      {error && <ErrorText>{error}</ErrorText>}
      <Table columns={columns} rows={list.map((r) => ({ ...r, _key: r.branchId }))} loading={loading} empty="No branches yet." />

      {editing && (
        <Modal title={editing.branchId ? 'Edit branch' : 'New branch'} onClose={() => setEditing(null)}>
          <form onSubmit={save} className="space-y-4">
            <ErrorText>{formError}</ErrorText>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Field label="Code" required>
                <Input value={form.branchCode} onChange={(e) => setForm({ ...form, branchCode: e.target.value })} required />
              </Field>
              <Field label="Name" required>
                <Input value={form.branchName} onChange={(e) => setForm({ ...form, branchName: e.target.value })} required />
              </Field>
            </div>
            <Field label="City">
              <Input value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} />
            </Field>
            <Field label="Address">
              <Input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
            </Field>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Field label="Contact person">
                <Input value={form.contactPerson} onChange={(e) => setForm({ ...form, contactPerson: e.target.value })} />
              </Field>
              <Field label="Contact phone">
                <Input value={form.contactPhone} onChange={(e) => setForm({ ...form, contactPhone: e.target.value })} />
              </Field>
            </div>
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
