import { useState } from 'react'
import { users } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Field, Input, Select, Button, ErrorText } from '../components/ui'

const empty = { username: '', email: '', password: '', role: 'Employee', biometricUserId: '' }

export default function RegisterUser() {
  const [form, setForm] = useState(empty)
  const [error, setError] = useState('')
  const [msg, setMsg] = useState('')
  const [saving, setSaving] = useState(false)

  const set = (k) => (e) => setForm({ ...form, [k]: e.target.value })

  async function submit(e) {
    e.preventDefault()
    setError(''); setMsg(''); setSaving(true)
    try {
      const body = { ...form, biometricUserId: form.biometricUserId.trim() || null }
      const res = await users.register(body)
      setMsg(`Account "${res.username}" created with role ${res.role}.`)
      setForm(empty)
    } catch (err) {
      setError(apiErrorMessage(err, 'Could not create the account'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="max-w-lg">
      <PageHeader title="Register User" subtitle="Admin-only. Creates a login account." />
      <Card className="p-5">
        <form onSubmit={submit} className="space-y-4">
          <ErrorText>{error}</ErrorText>
          {msg && <div className="rounded-md bg-green-50 px-3 py-2 text-sm text-green-700 ring-1 ring-green-200">{msg}</div>}
          <Field label="Username" required><Input value={form.username} onChange={set('username')} required /></Field>
          <Field label="Email" required><Input type="email" value={form.email} onChange={set('email')} required /></Field>
          <Field label="Password" required hint="At least 8 characters">
            <Input type="password" value={form.password} onChange={set('password')} required minLength={8} />
          </Field>
          <Field label="Role">
            <Select value={form.role} onChange={set('role')}>
              <option value="Employee">Employee</option>
              <option value="HR">HR</option>
              <option value="Admin">Admin</option>
            </Select>
          </Field>
          <Field label="Link to employee (biometric ID)" hint="Optional. Matches the account to an existing employee record.">
            <Input value={form.biometricUserId} onChange={set('biometricUserId')} />
          </Field>
          <Button type="submit" disabled={saving}>{saving ? 'Creating…' : 'Create account'}</Button>
        </form>
      </Card>
    </div>
  )
}
