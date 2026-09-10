import { useState } from 'react'
import { account } from '../api/resources'
import { useAsync } from '../hooks/useAsync'
import { apiErrorMessage } from '../lib/errors'
import { dmy } from '../lib/dates'
import { PageHeader, Card, Field, Input, Button, ErrorText } from '../components/ui'

export default function Profile() {
  const { data, loading, error } = useAsync(() => account.profile(), [])
  const [pw, setPw] = useState({ currentPassword: '', newPassword: '', confirm: '' })
  const [msg, setMsg] = useState('')
  const [pwError, setPwError] = useState('')
  const [saving, setSaving] = useState(false)

  async function changePassword(e) {
    e.preventDefault()
    setMsg(''); setPwError('')
    if (pw.newPassword !== pw.confirm) {
      setPwError('The new password and confirmation do not match.')
      return
    }
    setSaving(true)
    try {
      await account.changePassword({ currentPassword: pw.currentPassword, newPassword: pw.newPassword })
      setMsg('Password updated.')
      setPw({ currentPassword: '', newPassword: '', confirm: '' })
    } catch (err) {
      setPwError(apiErrorMessage(err, 'Could not change the password'))
    } finally {
      setSaving(false)
    }
  }

  const row = (label, value) => (
    <div className="flex justify-between border-b border-slate-100 py-2 text-sm last:border-0">
      <span className="text-slate-500">{label}</span>
      <span className="font-medium text-slate-800">{value ?? '—'}</span>
    </div>
  )

  return (
    <div className="max-w-5xl">
      <PageHeader title="Profile & Security" />
      {error && <ErrorText>{error}</ErrorText>}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:items-start">
      <Card className="p-5">
        {loading ? (
          <p className="text-sm text-slate-500">Loading…</p>
        ) : data ? (
          <>
            {row('Username', data.username)}
            {row('Email', data.email)}
            {row('Role', data.role)}
            {row('Employee', data.employeeName)}
            {row('Department', data.departmentName)}
            {row('Last login', data.lastLoginDate ? new Date(data.lastLoginDate).toLocaleString() : 'Never')}
            {row('Account created', data.createdDate ? dmy(data.createdDate) : '—')}
          </>
        ) : null}
      </Card>

      <Card className="p-5">
        <h2 className="mb-3 text-sm font-semibold text-slate-800">Change password</h2>
        <form onSubmit={changePassword} className="space-y-3">
          <ErrorText>{pwError}</ErrorText>
          {msg && <div className="rounded-md bg-green-50 px-3 py-2 text-sm text-green-700 ring-1 ring-green-200">{msg}</div>}
          <Field label="Current password" required>
            <Input type="password" value={pw.currentPassword} onChange={(e) => setPw({ ...pw, currentPassword: e.target.value })} required />
          </Field>
          <Field label="New password" required hint="At least 8 characters">
            <Input type="password" value={pw.newPassword} onChange={(e) => setPw({ ...pw, newPassword: e.target.value })} required minLength={8} />
          </Field>
          <Field label="Confirm new password" required>
            <Input type="password" value={pw.confirm} onChange={(e) => setPw({ ...pw, confirm: e.target.value })} required />
          </Field>
          <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Update password'}</Button>
        </form>
      </Card>
      </div>
    </div>
  )
}
