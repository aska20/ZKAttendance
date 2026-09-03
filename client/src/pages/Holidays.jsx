import { useEffect, useMemo, useState } from 'react'
import { holidays as api } from '../api/resources'
import { apiErrorMessage } from '../lib/errors'
import { ymd } from '../lib/dates'
import { PageHeader, Card, ErrorText, Button, Modal, Field, Input, Select, Badge } from '../components/ui'
import { useFeedback } from '../components/feedback'

const WD = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const TYPES = ['Festival', 'Religious', 'National', 'Public', 'Bandh / strike', 'Company', 'Other']

export default function Holidays() {
  const fb = useFeedback()
  const [month, setMonth] = useState(() => {
    const d = new Date()
    return { y: d.getFullYear(), m: d.getMonth() }
  })
  const [holidays, setHolidays] = useState([])
  const [error, setError] = useState('')
  const [editing, setEditing] = useState(null) // { iso, existing }
  const [form, setForm] = useState({ holidayName: '', holidayType: 'Festival', description: '' })
  const [saving, setSaving] = useState(false)

  const first = useMemo(() => new Date(month.y, month.m, 1), [month])
  const daysInMonth = new Date(month.y, month.m + 1, 0).getDate()

  const holidayByDate = useMemo(() => {
    const map = {}
    for (const h of holidays) map[ymd(h.date)] = h
    return map
  }, [holidays])

  function load() {
    setError('')
    api.list({ from: ymd(new Date(month.y, month.m, 1)), to: ymd(new Date(month.y, month.m + 1, 0)) })
      .then(setHolidays)
      .catch((e) => setError(apiErrorMessage(e)))
  }
  useEffect(load, [month]) // eslint-disable-line react-hooks/exhaustive-deps

  function openDay(iso, isSat) {
    if (isSat) { fb.info('Saturday is the fixed weekly off.'); return }
    const existing = holidayByDate[iso]
    setForm(existing
      ? { holidayName: existing.holidayName, holidayType: existing.holidayType || 'Festival', description: existing.description || '' }
      : { holidayName: '', holidayType: 'Festival', description: '' })
    setEditing({ iso, existing })
  }

  async function save(e) {
    e.preventDefault()
    setSaving(true)
    try {
      await api.create({
        date: editing.iso,
        holidayName: form.holidayName.trim(),
        holidayType: form.holidayType,
        description: form.description.trim() || null,
      })
      fb.success(editing.existing ? 'Holiday updated' : 'Holiday added')
      setEditing(null)
      load()
    } catch (err) {
      fb.error(apiErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  async function remove(iso) {
    const ok = await fb.confirm({ title: 'Remove holiday', message: `Unmark ${iso} as a holiday?`, confirmText: 'Remove', danger: true })
    if (!ok) return
    try {
      await api.removeOnDate(iso)
      fb.success('Holiday removed')
      setEditing(null)
      load()
    } catch (err) { fb.error(apiErrorMessage(err)) }
  }

  const cells = []
  for (let i = 0; i < first.getDay(); i++) cells.push(null)
  for (let d = 1; d <= daysInMonth; d++) cells.push(d)
  const monthLabel = first.toLocaleString('default', { month: 'long', year: 'numeric' })

  return (
    <div>
      <PageHeader title="Holidays" subtitle="Click a day to add or edit a holiday. Marked days are excluded from working-day counts." />
      {error && <ErrorText>{error}</ErrorText>}

      <Card className="p-4">
        <div className="mb-3 flex items-center justify-between">
          <Button variant="secondary" onClick={() => setMonth(({ y, m }) => m === 0 ? { y: y - 1, m: 11 } : { y, m: m - 1 })}>← Prev</Button>
          <div className="text-lg font-semibold text-slate-800">{monthLabel}</div>
          <Button variant="secondary" onClick={() => setMonth(({ y, m }) => m === 11 ? { y: y + 1, m: 0 } : { y, m: m + 1 })}>Next →</Button>
        </div>

        <div className="grid grid-cols-7 gap-1 text-center text-xs font-medium text-slate-400">
          {WD.map((w) => <div key={w} className="py-1">{w}</div>)}
        </div>
        <div className="grid grid-cols-7 gap-1">
          {cells.map((d, i) => {
            if (d === null) return <div key={i} />
            const date = new Date(month.y, month.m, d)
            const iso = ymd(date)
            const isSat = date.getDay() === 6
            const h = holidayByDate[iso]
            return (
              <button
                key={i}
                onClick={() => openDay(iso, isSat)}
                className={`flex h-20 flex-col items-center justify-center gap-0.5 rounded-md border p-1 text-sm transition
                  ${isSat ? 'border-slate-200 bg-slate-50 text-slate-400'
                    : h ? 'border-sky-300 bg-sky-50 text-sky-700 hover:bg-sky-100'
                    : 'border-slate-200 hover:bg-slate-50'}`}
              >
                <span className="font-semibold">{d}</span>
                {isSat && <span className="text-[10px]">weekly off</span>}
                {h && <>
                  <span className="line-clamp-1 text-[11px] font-medium leading-tight">{h.holidayName}</span>
                  {h.holidayType && <span className="text-[9px] text-sky-500">{h.holidayType}</span>}
                </>}
              </button>
            )
          })}
        </div>
      </Card>

      <Card className="mt-4 p-4">
        <div className="mb-2 text-sm font-medium text-slate-700">Holidays this month</div>
        {holidays.length === 0
          ? <p className="text-sm text-slate-400">None.</p>
          : (
            <ul className="space-y-1 text-sm">
              {holidays.map((h) => (
                <li key={h.holidayId} className="flex items-center justify-between border-b border-slate-50 py-1.5 last:border-0">
                  <span className="flex items-center gap-2">
                    <span className="text-slate-400">{ymd(h.date)}</span>
                    <span className="font-medium text-slate-800">{h.holidayName}</span>
                    {h.holidayType && <Badge tone="sky">{h.holidayType}</Badge>}
                    {h.description && <span className="text-xs text-slate-400">— {h.description}</span>}
                  </span>
                  <span className="whitespace-nowrap">
                    <button onClick={() => openDay(ymd(h.date), false)} className="text-xs text-sky-600 hover:underline">edit</button>
                    <button onClick={() => remove(ymd(h.date))} className="ml-3 text-xs text-red-600 hover:underline">remove</button>
                  </span>
                </li>
              ))}
            </ul>
          )}
      </Card>

      {editing && (
        <Modal
          title={`${editing.existing ? 'Edit' : 'Add'} holiday — ${editing.iso}`}
          onClose={() => setEditing(null)}
        >
          <form onSubmit={save} className="space-y-4">
            <Field label="Name" required hint="e.g. Teej, Tihar, Dashain, Constitution Day">
              <Input autoFocus value={form.holidayName} onChange={(e) => setForm({ ...form, holidayName: e.target.value })} required />
            </Field>
            <Field label="Type">
              <Select value={form.holidayType} onChange={(e) => setForm({ ...form, holidayType: e.target.value })}>
                {TYPES.map((t) => <option key={t} value={t}>{t}</option>)}
              </Select>
            </Field>
            <Field label="Note" hint="Optional — a short description">
              <Input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} placeholder="e.g. Women's festival, government holiday" />
            </Field>
            <div className="flex justify-between pt-2">
              <span>
                {editing.existing && (
                  <Button type="button" variant="danger" onClick={() => remove(editing.iso)}>Remove</Button>
                )}
              </span>
              <span className="flex gap-2">
                <Button type="button" variant="ghost" onClick={() => setEditing(null)}>Cancel</Button>
                <Button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
              </span>
            </div>
          </form>
        </Modal>
      )}
    </div>
  )
}
