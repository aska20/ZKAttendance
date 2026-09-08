import { useEffect, useMemo, useState } from 'react'
import { settings as api } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { PageHeader, Card, Button, ErrorText } from '../components/ui'
import { useFeedback } from '../components/feedback'
import { useAuth } from '../context/AuthContext'

/** "10:00" plus n minutes, back as "10:15". */
function addMinutes(hhmm, minutes) {
  const [h, m] = String(hhmm || '0:00').split(':').map(Number)
  if (Number.isNaN(h) || Number.isNaN(m)) return '--:--'
  const total = (h * 60 + m + Number(minutes || 0) + 1440) % 1440
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`
}

export default function Settings() {
  const fb = useFeedback()
  const { role } = useAuth()
  const isAdmin = role === 'Admin'

  const [form, setForm] = useState(null)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    api
      .attendance()
      .then(setForm)
      .catch((e) => setError(apiErrorMessage(e)))
  }, [])

  const set = (key) => (value) => setForm((f) => ({ ...f, [key]: value }))

  // Shown live so the effect of a change is visible before saving.
  const preview = useMemo(() => {
    if (!form) return null
    return {
      onTimeUntil: addMinutes(form.officeStartTime, form.graceMinutes),
      approvalAfter: addMinutes(form.officeStartTime, form.approvalRequiredAfterMinutes),
      closesAt: addMinutes(form.officeEndTime, form.closeGraceMinutes),
    }
  }, [form])

  async function save(e) {
    e.preventDefault()
    setSaving(true)
    try {
      const saved = await api.saveAttendance(form)
      setForm(saved)
      fb.success('Settings saved')
    } catch (err) {
      fb.error(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  if (error) return <ErrorText>{error}</ErrorText>
  if (!form) return <p className="text-sm text-slate-400">Loading...</p>

  return (
    <div className="max-w-4xl">
      <PageHeader title="Settings" subtitle="Office hours and attendance rules" />

      <form onSubmit={save} className="space-y-4">
        {/* Live summary of what the current values mean */}
        {preview && (
          <Card className="overflow-hidden">
            <div className="grid grid-cols-1 divide-y divide-slate-100 sm:grid-cols-3 sm:divide-x sm:divide-y-0">
              <Rule
                tone="emerald"
                mark="P"
                title="On time"
                detail={`Until ${preview.onTimeUntil}`}
                note="Approved automatically"
              />
              <Rule
                tone="amber"
                mark="L"
                title="Late"
                detail={`${preview.onTimeUntil} to ${preview.approvalAfter}`}
                note={form.requireApprovalForLate ? 'Needs approval' : 'Counted, flagged late'}
              />
              <Rule
                tone="rose"
                mark="?"
                title="Pending"
                detail={`After ${preview.approvalAfter}`}
                note="Admin must approve"
              />
            </div>
          </Card>
        )}

        <Card className="p-5">
          <h2 className="text-sm font-semibold text-slate-900">Office hours</h2>
          <div className="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
            <TimeField
              label="Start time"
              value={form.officeStartTime}
              onChange={set('officeStartTime')}
              disabled={!isAdmin}
            />
            <TimeField
              label="End time"
              value={form.officeEndTime}
              onChange={set('officeEndTime')}
              disabled={!isAdmin}
            />
          </div>
        </Card>

        <Card className="p-5">
          <h2 className="text-sm font-semibold text-slate-900">Late arrivals</h2>
          <div className="mt-4 space-y-4">
            <NumberField
              label="Grace period"
              unit="minutes"
              value={form.graceMinutes}
              onChange={set('graceMinutes')}
              min={0}
              max={240}
              disabled={!isAdmin}
              hint={`On time until ${preview.onTimeUntil}`}
            />
            <NumberField
              label="Approval needed after"
              unit="minutes past start"
              value={form.approvalRequiredAfterMinutes}
              onChange={set('approvalRequiredAfterMinutes')}
              min={0}
              max={480}
              disabled={!isAdmin}
              hint={`Pending after ${preview.approvalAfter}`}
            />
            <Toggle
              label="Late arrivals also need approval"
              checked={form.requireApprovalForLate}
              onChange={set('requireApprovalForLate')}
              disabled={!isAdmin}
              hint={`Applies between ${preview.onTimeUntil} and ${preview.approvalAfter}`}
            />
          </div>
        </Card>

        <Card className="p-5">
          <h2 className="text-sm font-semibold text-slate-900">Marking absent</h2>
          <div className="mt-4 space-y-4">
            <NumberField
              label="Wait after end time"
              unit="minutes"
              value={form.closeGraceMinutes}
              onChange={set('closeGraceMinutes')}
              min={0}
              max={720}
              disabled={!isAdmin}
              hint={`Absent recorded from ${preview.closesAt}`}
            />
            <NumberField
              label="Half day below"
              unit="hours"
              value={form.halfDayUnderHours}
              onChange={set('halfDayUnderHours')}
              min={0}
              max={24}
              step={0.5}
              disabled={!isAdmin}
            />
          </div>
        </Card>

        {isAdmin ? (
          <div className="flex justify-end gap-2">
            <Button type="submit" disabled={saving}>
              {saving ? 'Saving...' : 'Save settings'}
            </Button>
          </div>
        ) : (
          <p className="text-xs text-slate-500">Only an admin can change these.</p>
        )}
      </form>
    </div>
  )
}

function Rule({ tone, mark, title, detail, note }) {
  const tones = {
    emerald: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
    amber: 'bg-amber-50 text-amber-700 ring-amber-200',
    rose: 'bg-rose-50 text-rose-700 ring-rose-200',
  }
  return (
    <div className="flex items-center gap-3 px-4 py-3.5">
      <span
        className={`inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-sm font-bold ring-1 ring-inset ${tones[tone]}`}
      >
        {mark}
      </span>
      <div className="min-w-0">
        <div className="text-sm font-semibold text-slate-900">{title}</div>
        <div className="text-xs tabular-nums text-slate-600">{detail}</div>
        <div className="text-xs text-slate-400">{note}</div>
      </div>
    </div>
  )
}

function TimeField({ label, value, onChange, disabled }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm font-medium text-slate-700">{label}</span>
      <input
        type="time"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        className="input w-full tabular-nums disabled:bg-slate-50 disabled:text-slate-500"
      />
    </label>
  )
}

function NumberField({ label, unit, value, onChange, min, max, step = 1, disabled, hint }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="min-w-0">
        <div className="text-sm font-medium text-slate-700">{label}</div>
        {hint && <div className="text-xs tabular-nums text-slate-400">{hint}</div>}
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <input
          type="number"
          value={value}
          min={min}
          max={max}
          step={step}
          disabled={disabled}
          onChange={(e) => onChange(Number(e.target.value))}
          className="input w-24 text-right tabular-nums disabled:bg-slate-50 disabled:text-slate-500"
        />
        <span className="w-28 text-xs text-slate-500">{unit}</span>
      </div>
    </div>
  )
}

function Toggle({ label, checked, onChange, disabled, hint }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="min-w-0">
        <div className="text-sm font-medium text-slate-700">{label}</div>
        {hint && <div className="text-xs tabular-nums text-slate-400">{hint}</div>}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={`relative h-6 w-11 shrink-0 rounded-full transition ${
          checked ? 'bg-sky-600' : 'bg-slate-300'
        } ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'}`}
      >
        <span
          className={`absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all ${
            checked ? 'left-[1.375rem]' : 'left-0.5'
          }`}
        />
      </button>
    </div>
  )
}
