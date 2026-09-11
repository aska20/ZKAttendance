import { useCallback, useEffect, useState } from 'react'
import { workShifts as api, employees as empApi } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Button, ErrorText, Badge, Field, Input, Modal, Select } from '../components/ui'
import { useFeedback } from '../components/feedback'
import { useAuth } from '../context/AuthContext'

const emptyShift = {
  shiftName: '',
  startTime: '10:00',
  endTime: '18:00',
  lateMinutes: 15,
  earlyMinutes: 15,
  breakMinutes: 0,
  description: '',
  isActive: true,
}

export default function WorkShifts() {
  const fb = useFeedback()
  const { role } = useAuth()
  const isAdmin = role === 'Admin'

  const [data, setData] = useState(null)
  const [error, setError] = useState('')
  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(emptyShift)
  const [saving, setSaving] = useState(false)

  const [assignFor, setAssignFor] = useState(null)
  const [people, setPeople] = useState([])
  const [picked, setPicked] = useState(new Set())

  const load = useCallback(() => {
    setError('')
    api
      .list(true)
      .then(setData)
      .catch((e) => {
        setError(apiErrorMessage(e))
        setData({ shifts: [], unassignedEmployees: 0 })
      })
  }, [])

  useEffect(load, [load])

  function openNew() {
    setForm(emptyShift)
    setEditing({ shiftId: 0 })
  }

  function openEdit(s) {
    setForm({
      shiftName: s.shiftName,
      startTime: s.startTime,
      endTime: s.endTime,
      lateMinutes: s.lateMinutes,
      earlyMinutes: s.earlyMinutes,
      breakMinutes: s.breakMinutes,
      description: s.description || '',
      isActive: s.isActive,
    })
    setEditing(s)
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true)
    try {
      if (editing.shiftId) await api.update(editing.shiftId, form)
      else await api.create(form)
      fb.success(editing.shiftId ? 'Shift updated' : 'Shift created')
      setEditing(null)
      load()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function remove(s) {
    const ok = await fb.confirm({
      title: `Delete ${s.shiftName}`,
      message:
        s.employeeCount > 0
          ? `${s.employeeCount} employee(s) are on this shift. It will be deactivated instead of deleted.`
          : 'This shift will be deleted.',
      confirmText: s.employeeCount > 0 ? 'Deactivate' : 'Delete',
      danger: true,
    })
    if (!ok) return
    try {
      const res = await api.remove(s.shiftId)
      fb.success(res.message)
      load()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    }
  }

  async function openAssign(s) {
    setAssignFor(s)
    setPicked(new Set())
    try {
      const list = await empApi.list()
      setPeople(Array.isArray(list) ? list : list?.items || [])
    } catch {
      setPeople([])
    }
  }

  async function saveAssign() {
    if (picked.size === 0) return
    try {
      const res = await api.assign([...picked], assignFor.shiftId || null)
      fb.success(res.message)
      setAssignFor(null)
      load()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    }
  }

  const shifts = data?.shifts ?? []

  return (
    <div className="max-w-6xl">
      <PageHeader
        title="Work Shifts"
        subtitle="Start and end times, and who is on each"
        actions={isAdmin && <Button onClick={openNew}>New shift</Button>}
      />

      {error && <ErrorText>{error}</ErrorText>}

      {data?.unassignedEmployees > 0 && (
        <p className="mb-4 rounded-lg bg-sky-50 px-3 py-2 text-sm text-sky-900 ring-1 ring-sky-200">
          {data.unassignedEmployees} employee(s) are on no shift. They follow the office hours set in
          Settings.
        </p>
      )}

      {!data ? (
        <p className="py-16 text-center text-sm text-slate-400">Loading...</p>
      ) : shifts.length === 0 ? (
        <Card className="py-16 text-center text-sm text-slate-400">
          No shifts yet. Employees follow the office hours in Settings until you add one.
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          {shifts.map((s) => (
            <Card key={s.shiftId} className="flex flex-col p-5">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <div className="truncate text-base font-semibold text-slate-900">{s.shiftName}</div>
                  <div className="mt-0.5 text-sm tabular-nums text-slate-600">
                    {s.startTime} to {s.endTime}
                    {s.isOvernight && <span className="ml-1.5 text-xs text-violet-600">overnight</span>}
                  </div>
                </div>
                {s.isActive ? <Badge tone="green">Active</Badge> : <Badge tone="slate">Inactive</Badge>}
              </div>

              <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-2 text-xs">
                <Row label="Work hours" value={`${s.workHours}h`} />
                <Row label="Break" value={s.breakMinutes ? `${s.breakMinutes} min` : 'none'} />
                <Row label="Late after" value={`${s.lateMinutes} min`} />
                <Row label="On this shift" value={`${s.employeeCount}`} />
              </dl>

              {s.description && <p className="mt-3 text-xs text-slate-500">{s.description}</p>}

              {isAdmin && (
                <div className="mt-auto flex flex-wrap gap-2 border-t border-slate-100 pt-3 text-xs">
                  <button onClick={() => openAssign(s)} className="font-medium text-sky-600 hover:underline">
                    Assign employees
                  </button>
                  <button onClick={() => openEdit(s)} className="font-medium text-slate-600 hover:underline">
                    Edit
                  </button>
                  <button onClick={() => remove(s)} className="font-medium text-rose-600 hover:underline">
                    Delete
                  </button>
                </div>
              )}
            </Card>
          ))}
        </div>
      )}

      <p className="mt-4 text-xs text-slate-400">
        A shift&apos;s end time decides when that person&apos;s day closes, and so when a no-show
        becomes absent.
      </p>

      {/* Create / edit */}
      {editing && (
        <Modal title={editing.shiftId ? 'Edit shift' : 'New shift'} onClose={() => setEditing(null)}>
          <form onSubmit={save} className="space-y-4">
            <Field label="Name" required>
              <Input
                value={form.shiftName}
                onChange={(e) => setForm({ ...form, shiftName: e.target.value })}
                placeholder="Morning"
                required
              />
            </Field>

            <div className="grid grid-cols-2 gap-4">
              <Field label="Start time" required>
                <input
                  type="time"
                  value={form.startTime}
                  onChange={(e) => setForm({ ...form, startTime: e.target.value })}
                  className="input w-full tabular-nums"
                  required
                />
              </Field>
              <Field label="End time" required>
                <input
                  type="time"
                  value={form.endTime}
                  onChange={(e) => setForm({ ...form, endTime: e.target.value })}
                  className="input w-full tabular-nums"
                  required
                />
              </Field>
            </div>

            <div className="grid grid-cols-3 gap-4">
              <Field label="Late after" hint="minutes">
                <Input
                  type="number"
                  value={form.lateMinutes}
                  onChange={(e) => setForm({ ...form, lateMinutes: Number(e.target.value) })}
                />
              </Field>
              <Field label="Early leave" hint="minutes">
                <Input
                  type="number"
                  value={form.earlyMinutes}
                  onChange={(e) => setForm({ ...form, earlyMinutes: Number(e.target.value) })}
                />
              </Field>
              <Field label="Break" hint="minutes">
                <Input
                  type="number"
                  value={form.breakMinutes}
                  onChange={(e) => setForm({ ...form, breakMinutes: Number(e.target.value) })}
                />
              </Field>
            </div>

            <Field label="Note" hint="Optional">
              <Input
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
              />
            </Field>

            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                className="h-4 w-4 rounded border-slate-300"
              />
              Active
            </label>

            <div className="flex justify-end gap-2 border-t border-slate-100 pt-4">
              <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
                Cancel
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? 'Saving...' : 'Save'}
              </Button>
            </div>
          </form>
        </Modal>
      )}

      {/* Assign */}
      {assignFor && (
        <Modal title={`Assign to ${assignFor.shiftName}`} onClose={() => setAssignFor(null)} wide>
          <p className="mb-3 text-sm text-slate-500">
            Select employees to put on this shift. Anyone already on another shift is moved.
          </p>

          <div className="max-h-80 overflow-y-auto rounded-lg ring-1 ring-slate-200">
            {people.length === 0 ? (
              <p className="py-8 text-center text-sm text-slate-400">No employees found.</p>
            ) : (
              <ul className="divide-y divide-slate-100">
                {people.map((p) => (
                  <li key={p.employeeId}>
                    <label className="flex cursor-pointer items-center gap-3 px-3 py-2 hover:bg-slate-50">
                      <input
                        type="checkbox"
                        checked={picked.has(p.employeeId)}
                        onChange={() =>
                          setPicked((prev) => {
                            const next = new Set(prev)
                            next.has(p.employeeId) ? next.delete(p.employeeId) : next.add(p.employeeId)
                            return next
                          })
                        }
                        className="h-4 w-4 rounded border-slate-300"
                      />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-medium text-slate-800">
                          {p.employeeName}
                        </span>
                        <span className="block truncate text-xs text-slate-400">
                          {p.departmentName || 'No department'}
                        </span>
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
          </div>

          <div className="mt-4 flex items-center justify-between gap-2 border-t border-slate-100 pt-4">
            <span className="text-xs text-slate-500">{picked.size} selected</span>
            <span className="flex gap-2">
              <Button variant="ghost" onClick={() => setAssignFor(null)}>
                Cancel
              </Button>
              <Button onClick={saveAssign} disabled={picked.size === 0}>
                Assign
              </Button>
            </span>
          </div>
        </Modal>
      )}
    </div>
  )
}

function Row({ label, value }) {
  return (
    <div>
      <dt className="text-slate-400">{label}</dt>
      <dd className="font-medium tabular-nums text-slate-800">{value}</dd>
    </div>
  )
}
