import { useState } from 'react'
import { employees } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Table, Modal, Field, Input, Button, ErrorText } from '../components/ui'

export default function UnregisteredIds() {
  const { data, loading, error, reload } = useAsync(() => employees.unregistered(), [])
  const [target, setTarget] = useState(null) // biometric id string
  const [name, setName] = useState('')
  const [formError, setFormError] = useState('')
  const [saving, setSaving] = useState(false)

  const ids = data || []

  async function create(e) {
    e.preventDefault()
    setSaving(true); setFormError('')
    try {
      await employees.create({ employeeName: name.trim(), biometricUserId: target })
      setTarget(null); setName(''); reload()
    } catch (err) {
      setFormError(apiErrorMessage(err, 'Could not create the employee'))
    } finally {
      setSaving(false)
    }
  }

  const columns = [
    { key: 'id', header: 'Biometric ID', render: (r) => <span className="font-medium text-slate-800">{r.id}</span> },
    {
      key: 'actions', header: '', render: (r) => (
        <div className="text-right">
          <button onClick={() => { setTarget(r.id); setName(''); setFormError('') }} className="text-sky-600 hover:underline">
            Create employee
          </button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Unregistered Biometric IDs"
        subtitle="Biometric IDs seen in scans that no employee record claims"
      />
      {error && <ErrorText>{error}</ErrorText>}
      <Table
        columns={columns}
        rows={ids.map((id) => ({ id, _key: id }))}
        loading={loading}
        empty="No unregistered IDs."
      />

      {target && (
        <Modal title={`Create employee for ID ${target}`} onClose={() => setTarget(null)}>
          <form onSubmit={create} className="space-y-4">
            <ErrorText>{formError}</ErrorText>
            <Field label="Employee name" required>
              <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus required />
            </Field>
            <div className="flex justify-end gap-2">
              <Button type="button" variant="ghost" onClick={() => setTarget(null)}>Cancel</Button>
              <Button type="submit" disabled={saving}>{saving ? 'Creating…' : 'Create'}</Button>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
