import { useState } from 'react'
import { workshifts } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Button, Table, Modal, Field, Input, ErrorText, Badge } from '../components/ui'

const empty = {
  shiftName: '', description: '', startTime: '09:00', endTime: '17:00',
  lateMinutes: 15, earlyMinutes: 15, breakMinutes: 0, isBreakPaid: true,
  workMinutes: 480, requireCheckIn: true, requireCheckOut: true,
  isOvernight: false, overtimeStartMinutes: 30, minHoursForFullDay: 4,
  maxRegularHours: 10, roundingMinutes: 0, workDays: '', isActive: true,
}

const num = (v) => (v === '' || v === null ? 0 : Number(v))

export default function WorkShifts() {
  const { data, loading, error, reload } = useAsync(() => workshifts.list(), [])
  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(empty)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const list = data || []

  function openCreate() { setForm(empty); setFormError(''); setEditing({}) }
  function openEdit(s) {
    setForm({ ...empty, ...s })
    setFormError(''); setEditing(s)
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true); setFormError('')
    const body = {
      ...form,
      lateMinutes: num(form.lateMinutes), earlyMinutes: num(form.earlyMinutes),
      breakMinutes: num(form.breakMinutes), workMinutes: num(form.workMinutes),
      overtimeStartMinutes: num(form.overtimeStartMinutes),
      minHoursForFullDay: num(form.minHoursForFullDay), maxRegularHours: num(form.maxRegularHours),
      roundingMinutes: num(form.roundingMinutes),
      workDays: form.workDays || null,
      description: form.description || null,
    }
    try {
      if (editing.shiftId) await workshifts.update(editing.shiftId, body)
      else await workshifts.create(body)
      setEditing(null); reload()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not save the shift'))
    } finally {
      setSaving(false)
    }
  }

  async function remove(s) {
    if (!confirm(`Delete shift "${s.shiftName}"?`)) return
    try { await workshifts.remove(s.shiftId); reload() }
    catch (err) { alert(apiErrorMessage(err)) }
  }

  const columns = [
    { key: 'shiftName', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.shiftName}</span> },
    { key: 'time', header: 'Hours', render: (r) => `${r.startTime} – ${r.endTime}${r.isOvernight ? ' (overnight)' : ''}` },
    { key: 'workMinutes', header: 'Work', render: (r) => `${(r.workMinutes / 60).toFixed(1)}h` },
    { key: 'lateMinutes', header: 'Grace', render: (r) => `${r.lateMinutes}m late / ${r.earlyMinutes}m early` },
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

  const nf = (k) => ({ value: form[k], onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  const cf = (k) => ({ checked: form[k], onChange: (e) => setForm({ ...form, [k]: e.target.checked }) })

  return (
    <div>
      <PageHeader title="Work Shifts" actions={<Button onClick={openCreate}>New shift</Button>} />
      {error && <ErrorText>{error}</ErrorText>}
      <Table columns={columns} rows={list.map((r) => ({ ...r, _key: r.shiftId }))} loading={loading} empty="No shifts yet." />

      {editing && (
        <Modal title={editing.shiftId ? 'Edit shift' : 'New shift'} onClose={() => setEditing(null)} wide>
          <form onSubmit={save} className="space-y-4">
            <ErrorText>{formError}</ErrorText>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Name" required>
                <Input {...nf('shiftName')} required />
              </Field>
              <Field label="Description">
                <Input {...nf('description')} />
              </Field>
            </div>
            <div className="grid grid-cols-4 gap-3">
              <Field label="Start" required><Input type="time" {...nf('startTime')} required /></Field>
              <Field label="End" required><Input type="time" {...nf('endTime')} required /></Field>
              <Field label="Work minutes"><Input type="number" {...nf('workMinutes')} /></Field>
              <Field label="Rounding min"><Input type="number" {...nf('roundingMinutes')} /></Field>
            </div>
            <div className="grid grid-cols-4 gap-3">
              <Field label="Late grace (m)"><Input type="number" {...nf('lateMinutes')} /></Field>
              <Field label="Early grace (m)"><Input type="number" {...nf('earlyMinutes')} /></Field>
              <Field label="Break (m)"><Input type="number" {...nf('breakMinutes')} /></Field>
              <Field label="OT after (m)"><Input type="number" {...nf('overtimeStartMinutes')} /></Field>
            </div>
            <div className="grid grid-cols-3 gap-3">
              <Field label="Min hrs full day"><Input type="number" step="0.5" {...nf('minHoursForFullDay')} /></Field>
              <Field label="Max regular hrs"><Input type="number" step="0.5" {...nf('maxRegularHours')} /></Field>
              <Field label="Work days" hint='e.g. "0,1,2,3,4,6"'><Input {...nf('workDays')} /></Field>
            </div>
            <div className="flex flex-wrap gap-4 text-sm text-slate-700">
              <label className="flex items-center gap-2"><input type="checkbox" {...cf('requireCheckIn')} /> Require check-in</label>
              <label className="flex items-center gap-2"><input type="checkbox" {...cf('requireCheckOut')} /> Require check-out</label>
              <label className="flex items-center gap-2"><input type="checkbox" {...cf('isBreakPaid')} /> Break paid</label>
              <label className="flex items-center gap-2"><input type="checkbox" {...cf('isOvernight')} /> Overnight</label>
              <label className="flex items-center gap-2"><input type="checkbox" {...cf('isActive')} /> Active</label>
            </div>
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
