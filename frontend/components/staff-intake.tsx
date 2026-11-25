'use client'

import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Alert } from '@/components/ui/alert'
import { correctIntake, createStockReceipt, getProductMovements, getProducts, listStockReceipts, newIdempotencyKey, type ApiProduct, type ApiReceipt, type ApiStockMovement } from '@/lib/ghost-api'

const money = (v: number) => `${new Intl.NumberFormat('ar-EG').format(v)} ج.م`
const when = (iso: string) => new Date(iso).toLocaleString('ar-EG', { dateStyle: 'medium', timeStyle: 'short' })

/**
 * Staff stock intake: register received goods (existing product), see own receipts, correct a mistaken quantity.
 * Entries are never edited or deleted. A correction is a new linked entry with a mandatory reason and only removes stock.
 * Under the current rules the cost of a receipt leaves the open shift's drawer when it is recorded.
 */
export function StaffIntake({ token, shiftId }: { token: string; shiftId: string | null }) {
  const [products, setProducts] = useState<ApiProduct[]>([])
  const [receipts, setReceipts] = useState<ApiReceipt[]>([])
  const [productId, setProductId] = useState('')
  const [quantity, setQuantity] = useState('')
  const [unitCost, setUnitCost] = useState('')
  const [supplier, setSupplier] = useState('')
  const [invoiceRef, setInvoiceRef] = useState('')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [movements, setMovements] = useState<ApiStockMovement[]>([])
  const [corrQty, setCorrQty] = useState<Record<string, string>>({})
  const [corrReason, setCorrReason] = useState<Record<string, string>>({})
  const [key, setKey] = useState(0)
  // One key per delivery. A retry of the same content (timeout, double tap) reuses it so the server records it once.
  const receiptKeyRef = useRef<{ signature: string; key: string } | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([getProducts(token), listStockReceipts(token)])
      .then(([p, r]) => { if (!cancelled) { setProducts(p); setReceipts(r) } })
      .catch((e: unknown) => { if (!cancelled) setError(e instanceof Error ? e.message : 'تعذر تحميل البيانات.') })
    return () => { cancelled = true }
  }, [token, key])

  const loadMovements = useCallback(async (id: string) => {
    if (!id) { setMovements([]); return }
    try { setMovements((await getProductMovements(token, id)).filter((m) => m.type === 'Purchase')) }
    catch (e) { setError(e instanceof Error ? e.message : 'تعذر تحميل الحركات.') }
  }, [token])

  useEffect(() => { const t = setTimeout(() => { void loadMovements(productId) }, 0); return () => clearTimeout(t) }, [productId, loadMovements, key])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(''); setMessage('')
    const q = Number(quantity); const c = Number(unitCost)
    if (!shiftId) { setError('افتح وردية أولًا لتسجيل التوريد.'); return }
    if (!productId) { setError('اختر المنتج.'); return }
    if (!(q > 0)) { setError('الكمية يجب أن تكون أكبر من صفر.'); return }
    if (!(c >= 0)) { setError('التكلفة غير صحيحة.'); return }
    if (!invoiceRef.trim() && !notes.trim()) { setError('اكتب رقم الفاتورة أو سبب/ملاحظة التوريد (إجباري).'); return }
    setBusy(true)
    try {
      const signature = JSON.stringify([shiftId, supplier.trim(), invoiceRef.trim(), notes.trim(), productId, q, c])
      if (receiptKeyRef.current?.signature !== signature) receiptKeyRef.current = { signature, key: newIdempotencyKey() }
      const r = await createStockReceipt(token, { shiftId, supplierName: supplier.trim() || undefined, invoiceReference: invoiceRef.trim() || undefined, notes: notes.trim() || undefined, idempotencyKey: receiptKeyRef.current.key, items: [{ productId, quantity: q, unitCost: c }] })
      receiptKeyRef.current = null
      setMessage(`تم تسجيل التوريد. الإجمالي ${money(r.totalCost)}. الكمية دخلت المخزون فورًا وهو بانتظار مراجعة الأدمن.`)
      setQuantity(''); setUnitCost(''); setInvoiceRef(''); setNotes('')
      setKey((k) => k + 1)
    } catch (err) { setError(err instanceof Error ? err.message : 'تعذر تسجيل التوريد.') }
    finally { setBusy(false) }
  }

  async function correct(m: ApiStockMovement) {
    setError(''); setMessage('')
    const q = Number(corrQty[m.id]); const reason = (corrReason[m.id] ?? '').trim()
    if (!(q > 0)) { setError('اكتب الكمية المراد خصمها.'); return }
    if (reason.length < 3) { setError('اكتب سبب التصحيح (إجباري).'); return }
    setBusy(true)
    try {
      await correctIntake(token, m.id, q, reason, shiftId ?? undefined)
      setMessage('تم تسجيل التصحيح كحركة مرتبطة. الحركة الأصلية لم تتغير ولا يوجد رد نقدي تلقائي.')
      setCorrQty((p) => ({ ...p, [m.id]: '' })); setCorrReason((p) => ({ ...p, [m.id]: '' }))
      setKey((k) => k + 1)
    } catch (err) { setError(err instanceof Error ? err.message : 'تعذر التصحيح.') }
    finally { setBusy(false) }
  }

  return (
    <section className="panel" aria-label="توريد المخزون">
      <div className="panel-heading"><div><h2>توريد المخزون</h2><p>سجّل البضاعة المستلمة. لا يمكن تعديل أو حذف أي توريد، والتصحيح يتم بحركة مرتبطة بسبب.</p></div></div>
      {!shiftId && <Alert variant="warning">لا توجد وردية مفتوحة.</Alert>}
      {error && <Alert variant="warning">{error}</Alert>}
      {message && <Alert variant="success">{message}</Alert>}
      <form onSubmit={submit} style={{ display: 'grid', gap: 8, maxWidth: 520 }}>
        <select value={productId} onChange={(e) => setProductId(e.target.value)} aria-label="المنتج">
          <option value="">اختر المنتج</option>
          {products.map((p) => <option key={p.id} value={p.id}>{p.name} (المخزون {p.stockQuantity})</option>)}
        </select>
        <input inputMode="decimal" placeholder="الكمية" value={quantity} onChange={(e) => setQuantity(e.target.value)} aria-label="الكمية" />
        <input inputMode="decimal" placeholder="تكلفة الوحدة (ج.م)" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} aria-label="تكلفة الوحدة" />
        <input placeholder="المورد (اختياري)" value={supplier} onChange={(e) => setSupplier(e.target.value)} maxLength={150} aria-label="المورد" />
        <input placeholder="رقم الفاتورة" value={invoiceRef} onChange={(e) => setInvoiceRef(e.target.value)} maxLength={100} aria-label="رقم الفاتورة" />
        <input placeholder="ملاحظة / سبب (إجباري لو مفيش رقم فاتورة)" value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={300} aria-label="ملاحظة" />
        <p className="subheading">تكلفة التوريد تُخصم من درج الوردية المفتوحة وقت التسجيل (السلوك الحالي).</p>
        <button type="submit" disabled={busy || !shiftId}>تسجيل التوريد</button>
      </form>

      {productId && movements.length > 0 && (
        <div style={{ marginTop: 16 }}>
          <h3>توريدات المنتج المختار (للتصحيح)</h3>
          {movements.map((m) => (
            <div key={m.id} style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center', padding: '6px 0' }}>
              <span>{when(m.createdAt)}</span><span>الكمية {m.qtyDelta}</span><span>التكلفة {money(m.unitCost)}</span>
              <input inputMode="decimal" placeholder="كمية الخصم" value={corrQty[m.id] ?? ''} onChange={(e) => setCorrQty((p) => ({ ...p, [m.id]: e.target.value }))} aria-label="كمية التصحيح" />
              <input placeholder="السبب (إجباري)" value={corrReason[m.id] ?? ''} onChange={(e) => setCorrReason((p) => ({ ...p, [m.id]: e.target.value }))} maxLength={300} aria-label="سبب التصحيح" />
              <button type="button" disabled={busy} onClick={() => void correct(m)}>تصحيح</button>
            </div>
          ))}
        </div>
      )}

      <div style={{ marginTop: 16 }}>
        <h3>آخر التوريدات</h3>
        {receipts.length === 0 ? <p className="subheading">لا توجد توريدات.</p> : receipts.slice(0, 20).map((r) => (
          <div key={r.id} style={{ padding: '6px 0' }}>
            <strong>{money(r.totalCost)}</strong> · {r.status} · {when(r.createdAt)}{r.supplierName ? ` · ${r.supplierName}` : ''}{r.invoiceReference ? ` · فاتورة ${r.invoiceReference}` : ''}
          </div>
        ))}
      </div>
    </section>
  )
}
