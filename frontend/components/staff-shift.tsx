'use client'

import { useEffect, useState, type FormEvent } from 'react'
import { ChevronLeft, ClipboardCheck } from 'lucide-react'
import { closeShift, getCurrentBusinessDay, getCurrentShift, openShift, type ApiBusinessDay, type ApiShift } from '@/lib/ghost-api'

const money = (value: number) => `${new Intl.NumberFormat('ar-EG').format(value)} ج.م`
const time = (iso: string) => new Date(iso).toLocaleString('ar-EG', { dateStyle: 'medium', timeStyle: 'short' })

/** Live shift screen: open with opening cash, see the open shift, close with the counted cash. The server decides expected cash and differences. */
export function StaffShiftView({ token, userId, onBack, onChanged }: { token: string; userId: string; onBack: () => void; onChanged?: () => void }) {
  const [loading, setLoading] = useState(true)
  const [day, setDay] = useState<ApiBusinessDay | null>(null)
  const [shift, setShift] = useState<ApiShift | null>(null)
  const [cash, setCash] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [closed, setClosed] = useState(false)

  const [reloadKey, setReloadKey] = useState(0)
  useEffect(() => {
    let cancelled = false
    Promise.all([getCurrentShift(token), getCurrentBusinessDay(token)])
      .then(([currentShift, currentDay]) => { if (!cancelled) { setShift(currentShift); setDay(currentDay) } })
      .catch((reason: unknown) => { if (!cancelled) setError(reason instanceof Error ? reason.message : 'تعذر تحميل بيانات الشيفت.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [token, reloadKey])

  const amount = Number(cash)
  const validAmount = cash.trim() !== '' && Number.isFinite(amount) && amount >= 0

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy || !validAmount) return
    setBusy(true)
    setError('')
    try {
      if (!shift) {
        if (!day) throw new Error('لا يوجد يوم عمل مفتوح. اطلب من المدير فتح يوم العمل.')
        await openShift(token, day.id, userId, amount)
        setCash('')
      } else {
        await closeShift(token, shift.businessDayId, shift.id, amount, reason.trim() || undefined)
        setClosed(true)
        setCash('')
        setReason('')
      }
      onChanged?.()
      setReloadKey((k) => k + 1)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر إكمال العملية.')
    } finally {
      setBusy(false)
    }
  }
  return <div className="inner-page">
    <button className="back-link" onClick={onBack}><ChevronLeft size={16} /> العودة للتشغيل</button>
    <div className="page-heading compact"><div><p className="eyebrow">الشيفتات والخزينة</p><h1>الشيفت الحالي</h1><p className="subheading">افتح الشيفت برصيد الخزينة، وأغلقه بعد عدّ النقدية. النظام يحسب المتوقع والفرق.</p></div></div>
    {loading && <div className="notice-bar" aria-live="polite"><div className="notice-icon"><ClipboardCheck size={17} /></div><div><strong>جاري التحميل</strong><p>يتم جلب الشيفت من GHOST API.</p></div></div>}
    {error && <div className="notice-bar" role="alert"><div className="notice-icon"><ClipboardCheck size={17} /></div><div><strong>تعذر الإكمال</strong><p>{error}</p></div></div>}
    {closed && !shift && <div className="notice-bar" aria-live="polite"><div className="notice-icon"><ClipboardCheck size={17} /></div><div><strong>تم إغلاق الشيفت</strong><p>تم تسجيل النقدية المعدودة.</p></div></div>}
    {!loading && shift && <section className="panel" aria-label="الشيفت المفتوح">
      <div className="panel-heading"><div><h2>شيفت مفتوح</h2><p>بدأ {time(shift.openedAt)}</p></div></div>
      <p>رصيد الافتتاح: <b>{money(shift.openingCash)}</b></p>
    </section>}
    {!loading && !shift && !day && <div className="notice-bar"><div className="notice-icon"><ClipboardCheck size={17} /></div><div><strong>لا يوجد يوم عمل مفتوح</strong><p>يفتح المدير يوم العمل أولًا، ثم يمكنك فتح شيفتك.</p></div></div>}
    {!loading && (shift || day) && <form className="panel" onSubmit={submit}>
      <div className="panel-heading"><div><h2>{shift ? 'إغلاق الشيفت' : 'فتح شيفت'}</h2><p>{shift ? 'أدخل النقدية الفعلية في الدرج.' : 'أدخل رصيد الخزينة عند البدء.'}</p></div></div>
      <label>{shift ? 'النقدية المعدودة (ج.م)' : 'رصيد الافتتاح (ج.م)'}<input inputMode="decimal" required value={cash} onChange={(e) => setCash(e.target.value)} dir="ltr" /></label>
      {shift && <label>سبب الفرق (مطلوب عند وجود فرق)<input maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} /></label>}
      <button type="submit" disabled={busy || !validAmount}>{busy ? 'جارٍ الحفظ…' : shift ? 'إغلاق الشيفت' : 'فتح الشيفت'}</button>
    </form>}
  </div>
}
