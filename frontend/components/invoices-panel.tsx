'use client'

import { useEffect, useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { getCurrentShift, listSales, refundSale, type ApiSale } from '@/lib/ghost-api'

const money = (v: number) => `${new Intl.NumberFormat('ar-EG').format(v)} ج.م`
const when = (iso: string) => new Date(iso).toLocaleString('ar-EG', { dateStyle: 'medium', timeStyle: 'short' })
const statusAr: Record<string, string> = { Paid: 'مدفوعة', Refunded: 'مستردة', Voided: 'ملغاة', Draft: 'مسودة', Pending: 'معلقة' }

/** Invoice list (Staff see their own, Admin all). Admin can refund a whole invoice: needs a reason and an open shift; the cash leaves that shift. */
export function InvoicesPanel({ token, isAdmin }: { token: string; isAdmin: boolean }) {
  const [sales, setSales] = useState<ApiSale[]>([])
  const [openShiftId, setOpenShiftId] = useState<string | null>(null)
  const [reasons, setReasons] = useState<Record<string, string>>({})
  const [toStock, setToStock] = useState<Record<string, boolean>>({})
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [key, setKey] = useState(0)

  useEffect(() => {
    let cancelled = false
    Promise.all([listSales(token), getCurrentShift(token)])
      .then(([list, shift]) => { if (!cancelled) { setSales(list); setOpenShiftId(shift && shift.status === 'Open' ? shift.id : null) } })
      .catch((e: unknown) => { if (!cancelled) setError(e instanceof Error ? e.message : 'تعذر تحميل الفواتير.') })
    return () => { cancelled = true }
  }, [token, key])

  async function refund(sale: ApiSale) {
    setError(''); setMessage('')
    const reason = (reasons[sale.id] ?? '').trim()
    if (!openShiftId) { setError('افتح وردية أولًا. الاسترداد يخرج من الوردية المفتوحة.'); return }
    if (reason.length < 3) { setError('اكتب سبب الاسترداد (إجباري).'); return }
    if (!window.confirm(`استرداد الفاتورة ${sale.invoiceNumber} كاملة بمبلغ ${money(sale.total)} نقدًا من الدرج؟ الاسترداد للفاتورة بالكامل وليس جزئيًا.${toStock[sale.id] ? '\nسيتم إرجاع الأصناف للمخزون بناءً على تأكيدك أنها استُلمت وصالحة للبيع.' : '\nلن يتم إرجاع شيء للمخزون.'}`)) return
    setBusy(sale.id)
    try {
      await refundSale(token, sale.id, { reason, returnToStock: toStock[sale.id] === true })
      setMessage(`تم استرداد الفاتورة ${sale.invoiceNumber}.`)
      setKey((k) => k + 1)
    } catch (e) { setError(e instanceof Error ? e.message : 'تعذر الاسترداد.') }
    finally { setBusy(null) }
  }

  return (
    <section className="panel" aria-label="الفواتير">
      <div className="panel-heading"><div><h2>الفواتير</h2><p>{isAdmin ? 'كل الفواتير' : 'فواتيرك'}</p></div></div>
      {error && <Alert variant="warning">{error}</Alert>}
      {message && <Alert variant="success">{message}</Alert>}
      {sales.length === 0 ? <p className="subheading">لا توجد فواتير.</p> : sales.map((s) => (
        <div key={s.id} style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center', padding: '8px 0', borderBottom: '1px solid var(--border, #ddd)' }}>
          <strong>{s.invoiceNumber}</strong><span>{statusAr[s.status] ?? s.status}</span><span>{money(s.total)}</span><span>{when(s.createdAt)}</span>
          {isAdmin && s.status === 'Paid' && (
            <>
              <input placeholder="سبب الاسترداد (إجباري)" value={reasons[s.id] ?? ''} onChange={(e) => setReasons((p) => ({ ...p, [s.id]: e.target.value }))} maxLength={300} aria-label="سبب الاسترداد" />
              <label><input type="checkbox" checked={toStock[s.id] === true} onChange={(e) => setToStock((p) => ({ ...p, [s.id]: e.target.checked }))} /> تم استلام البضاعة فعليًا وهي صالحة للبيع، أرجعها للمخزون</label>
              <span className="subheading">الاسترداد للفاتورة كاملة وليس جزئيًا لكل منتج. الافتراضي: لا يرجع شيء للمخزون.</span>
              <button type="button" disabled={busy === s.id} onClick={() => void refund(s)}>استرداد</button>
            </>
          )}
        </div>
      ))}
    </section>
  )
}
