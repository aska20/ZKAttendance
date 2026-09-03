import { useState } from 'react'
import { devices, branches } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { useAuth } from '../context/AuthContext'
import { PageHeader, Card, Button, Table, Modal, Field, Input, Select, ErrorText, Badge } from '../components/ui'
import { useFeedback } from '../components/feedback'

const empty = {
  deviceName: '', deviceIP: '', devicePort: 4370, serialNumber: '',
  deviceModel: '', branchId: '', role: 'Slave', commPassword: 0, isActive: true,
}

export default function Devices() {
  const { isAdmin } = useAuth()
  const fb = useFeedback()
  const { data, loading, error, reload } = useAsync(
    () => Promise.all([devices.list(), branches.list()]).then(([d, b]) => ({ devices: d, branches: b })),
    [],
  )
  const [editing, setEditing] = useState(null)
  const [form, setForm] = useState(empty)
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)
  const [busyId, setBusyId] = useState(null)

  const list = data?.devices || []
  const branchList = data?.branches || []
  const branchName = (id) => branchList.find((b) => b.branchId === id)?.branchName || '—'

  function openCreate() { setForm(empty); setFormError(''); setEditing({}) }
  function openEdit(d) {
    setForm({
      deviceName: d.deviceName || '', deviceIP: d.deviceIP || '', devicePort: d.devicePort ?? 4370,
      serialNumber: d.serialNumber || '', deviceModel: d.deviceModel || '',
      branchId: d.branchId ? String(d.branchId) : '', role: d.role || 'Slave',
      commPassword: d.commPassword ?? 0, isActive: d.isActive,
    })
    setFormError(''); setEditing(d)
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true); setFormError('')
    const body = {
      ...form,
      devicePort: Number(form.devicePort),
      branchId: Number(form.branchId),
      commPassword: Number(form.commPassword) || 0,
    }
    try {
      if (editing.deviceId) await devices.update(editing.deviceId, body)
      else await devices.create(body)
      setEditing(null); reload()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not save the device'))
    } finally {
      setSaving(false)
    }
  }

  async function act(d, fn, label) {
    setBusyId(d.deviceId)
    try {
      const res = await fn(d.deviceId)
      fb.success(`${d.deviceName}: ${res.message ?? label + ' done'}`)
      reload()
    } catch (err) {
      fb.error(`${d.deviceName}: ${apiErrorMessage(err)}`)
    } finally {
      setBusyId(null)
    }
  }

  const columns = [
    { key: 'deviceName', header: 'Name', render: (r) => <span className="font-medium text-slate-800">{r.deviceName}</span> },
    ...(isAdmin ? [{ key: 'deviceIP', header: 'Address', render: (r) => `${r.deviceIP}:${r.devicePort}` }] : []),
    { key: 'branchId', header: 'Branch', render: (r) => branchName(r.branchId) },
    { key: 'role', header: 'Role', render: (r) => <Badge tone={r.role === 'Master' ? 'sky' : 'slate'}>{r.role}</Badge> },
    {
      key: 'state', header: 'State', render: (r) => (
        <div className="flex gap-1">
          <Badge tone={r.isActive ? 'green' : 'slate'}>{r.isActive ? 'Active' : 'Inactive'}</Badge>
          <Badge tone={r.isOnline ? 'green' : 'amber'}>{r.isOnline ? 'Online' : 'Offline'}</Badge>
        </div>
      ),
    },
    ...(isAdmin ? [{
      key: 'actions', header: '', render: (r) => (
        <div className="text-right whitespace-nowrap text-xs">
          <button disabled={busyId === r.deviceId} onClick={() => act(r, devices.testConnection, 'Test')} className="text-slate-600 hover:underline">Test</button>
          <button disabled={busyId === r.deviceId} onClick={() => act(r, devices.sync, 'Sync')} className="ml-2 text-slate-600 hover:underline">Sync</button>
          <button onClick={() => openEdit(r)} className="ml-2 text-sky-600 hover:underline">Edit</button>
          {r.isActive && (
            <button disabled={busyId === r.deviceId} onClick={() => act(r, devices.deactivate, 'Deactivate')} className="ml-2 text-red-600 hover:underline">Deactivate</button>
          )}
        </div>
      ),
    }] : []),
  ]

  const nf = (k) => ({ value: form[k], onChange: (e) => setForm({ ...form, [k]: e.target.value }) })

  return (
    <div>
      <PageHeader
        title="Devices"
        actions={isAdmin ? <Button onClick={openCreate}>New device</Button> : null}
      />
      {!isAdmin && (
        <Card className="mb-4 p-3 text-sm text-slate-600">
          Network details and device settings are managed by an admin. You can see which
          devices exist and whether they are online.
        </Card>
      )}
      {error && <ErrorText>{error}</ErrorText>}
      <Table columns={columns} rows={list.map((r) => ({ ...r, _key: r.deviceId }))} loading={loading} empty="No devices registered." />

      {editing && (
        <Modal title={editing.deviceId ? 'Edit device' : 'New device'} onClose={() => setEditing(null)}>
          <form onSubmit={save} className="space-y-4">
            <ErrorText>{formError}</ErrorText>
            <Field label="Name" required><Input {...nf('deviceName')} required /></Field>
            <div className="grid grid-cols-2 gap-3">
              <Field label="IP address" required><Input {...nf('deviceIP')} required /></Field>
              <Field label="Port"><Input type="number" {...nf('devicePort')} /></Field>
            </div>
            <div className="grid grid-cols-3 gap-3">
              <Field label="Serial number"><Input {...nf('serialNumber')} /></Field>
              <Field label="Model"><Input {...nf('deviceModel')} /></Field>
              <Field label="Comm password" hint="Blank = none">
                <Input
                  type="text"
                  inputMode="numeric"
                  value={form.commPassword === 0 ? '' : form.commPassword}
                  onChange={(e) =>
                    setForm({ ...form, commPassword: e.target.value.replace(/\D/g, '') })
                  }
                  placeholder="0"
                />
              </Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Branch" required>
                <Select {...nf('branchId')} required>
                  <option value="">Select branch</option>
                  {branchList.map((b) => <option key={b.branchId} value={b.branchId}>{b.branchName}</option>)}
                </Select>
              </Field>
              <Field label="Role" hint="Only one active Master allowed">
                <Select {...nf('role')}>
                  <option value="Slave">Slave</option>
                  <option value="Master">Master</option>
                </Select>
              </Field>
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
    </div>
  )
}
