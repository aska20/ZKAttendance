import { useState } from 'react'
import { devices, branches } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { useAuth } from '../context/AuthContext'
import { PageHeader, Card, Button, Table, Modal, Field, Input, Select, ErrorText, Badge } from '../components/ui'
import { useFeedback } from '../components/feedback'
import { FaEdit } from 'react-icons/fa'
import { FiWifi, FiRefreshCw, FiPower } from 'react-icons/fi'

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
  const [busyState, setBusyState] = useState(null)

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

  async function act(d, fn, label, actionType) {
    setBusyState({ id: d.deviceId, action: actionType })
    try {
      const res = await fn(d.deviceId)
      fb.success(`${d.deviceName}: ${res.message ?? label + ' done'}`)
      reload()
    } catch (err) {
      fb.error(`${d.deviceName}: ${apiErrorMessage(err)}`)
    } finally {
      setBusyState(null)
    }
  }

  async function deactivateDevice(d) {
    const ok = await fb.confirm({
      title: 'Deactivate device',
      message: `Are you sure you want to deactivate "${d.deviceName}"?`,
      confirmText: 'Deactivate',
      danger: true,
    })
    if (!ok) return
    act(d, devices.deactivate, 'Deactivate', 'deactivate')
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
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (r) => {
        const isBusy = busyState?.id === r.deviceId
        const isSyncing = isBusy && busyState?.action === 'sync'
        const isTesting = isBusy && busyState?.action === 'test'

        return (
          <div className="flex items-center justify-end gap-1">
            <button
              type="button"
              disabled={isBusy}
              onClick={() => act(r, devices.testConnection, 'Test', 'test')}
              title={isTesting ? 'Testing connection…' : 'Test connection'}
              className={`rounded p-1.5 transition-colors cursor-pointer ${
                isTesting
                  ? 'bg-amber-50 text-amber-600 cursor-wait'
                  : 'text-slate-500 hover:bg-amber-50 hover:text-amber-600 disabled:opacity-40 disabled:cursor-not-allowed'
              }`}
            >
              <FiWifi className={`h-4 w-4 ${isTesting ? 'animate-pulse' : ''}`} />
            </button>
            <button
              type="button"
              disabled={isBusy}
              onClick={() => act(r, devices.sync, 'Sync', 'sync')}
              title={isSyncing ? 'Syncing device…' : 'Sync device'}
              className={`inline-flex items-center gap-1.5 rounded p-1.5 transition-colors cursor-pointer ${
                isSyncing
                  ? 'bg-sky-50 text-sky-600 ring-1 ring-sky-200 cursor-wait px-2'
                  : 'text-slate-500 hover:bg-sky-50 hover:text-sky-600 disabled:opacity-40 disabled:cursor-not-allowed'
              }`}
            >
              <FiRefreshCw className={`h-4 w-4 ${isSyncing ? 'animate-spin text-sky-600' : ''}`} />
              {isSyncing && <span className="text-xs font-medium">Syncing…</span>}
            </button>
            <button
              type="button"
              disabled={isBusy}
              onClick={() => openEdit(r)}
              title="Edit device"
              className="rounded p-1.5 text-slate-500 hover:bg-sky-50 hover:text-sky-600 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
            >
              <FaEdit className="h-4 w-4" />
            </button>
            {r.isActive && (
              <button
                type="button"
                disabled={isBusy}
                onClick={() => deactivateDevice(r)}
                title="Deactivate device"
                className="rounded p-1.5 text-red-500 hover:bg-red-50 hover:text-red-700 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <FiPower className="h-4 w-4" />
              </button>
            )}
          </div>
        )
      },
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
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <Field label="IP address" required><Input {...nf('deviceIP')} required /></Field>
              <Field label="Port"><Input type="number" {...nf('devicePort')} /></Field>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
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
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
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
