'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import { CupSoda, Minus, Plus, Trash2 } from 'lucide-react'
import { checkout, getProducts, listSessions, newIdempotencyKey, printInvoice, type ApiProduct, type ApiSale, type ApiSessionSummary } from '@/lib/ghost-api'

const money = (value: number) => `${new Intl.NumberFormat('ar-EG').format(value)} ج.م`

/** Cash POS. The cart total here is a preview: the server recomputes prices, stock and totals at checkout. */
export function StaffPos({ token, shiftId, isAdmin = false, onSold }: { token: string; shiftId: string | null; isAdmin?: boolean; onSold?: () => void }) {
  const [products, setProducts] = useState<ApiProduct[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [cart, setCart] = useState<Record<string, number>>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [receipt, setReceipt] = useState<ApiSale | null>(null)
  // One key per cart content: a retry after a timeout reuses it, so the server can never charge twice.
  const keyRef = useRef<string>(newIdempotencyKey())
  const [sessionId, setSessionId] = useState('')
  const [doneSessions, setDoneSessions] = useState<ApiSessionSummary[]>([])
  const [discountType, setDiscountType] = useState<'None' | 'Fixed' | 'Percent'>('None')
  const [discountValue, setDiscountValue] = useState('')
  const [discountReason, setDiscountReason] = useState('')
  const [printing, setPrinting] = useState(false)

  useEffect(() => {
    if (!shiftId) return
    let cancelled = false
    listSessions(token, { status: 'Completed', shiftId, pageSize: 50 })
      .then((page) => { if (!cancelled) setDoneSessions(page.items) })
      .catch(() => undefined)
    return () => { cancelled = true }
  }, [token, shiftId, receipt])

  useEffect(() => {
    let cancelled = false
    getProducts(token)
      .then((items) => { if (!cancelled) setProducts(items) })
      .catch((reason: unknown) => { if (!cancelled) setError(reason instanceof Error ? reason.message : 'تعذر تحميل المنتجات.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [token])

  const byId = useMemo(() => new Map(products.map((p) => [p.id, p])), [products])
  const lines = Object.entries(cart).map(([id, quantity]) => ({ product: byId.get(id)!, quantity })).filter((l) => l.product)
  const total = lines.reduce((sum, l) => sum + l.product.salePrice * l.quantity, 0)
  const shown = products.filter((p) => !search.trim() || p.name.toLowerCase().includes(search.trim().toLowerCase()) || p.barcode === search.trim())

  const discountNumber = Number(discountValue)
  const discountValid = discountType === 'None' || (Number.isFinite(discountNumber) && discountNumber > 0 && (discountType === 'Fixed' || discountNumber <= 100) && discountReason.trim().length > 0)

  // Any change to what is being sold is a different checkout, so it gets a new key. Only a retry of the same checkout reuses it.
  function touchCheckout() { keyRef.current = newIdempotencyKey() }

  async function doPrint(saleId: string) {
    setPrinting(true)
    setError('')
    try {
      const result = await printInvoice(token, saleId)
      setReceipt(result.sale)
      setTimeout(() => window.print(), 50)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر تسجيل الطباعة.')
    } finally {
      setPrinting(false)
    }
  }

  function change(id: string, delta: number) {
    keyRef.current = newIdempotencyKey() // cart changed: this is a different sale
    setReceipt(null)
    setError('')
    setCart((current) => {
      const next = Math.max((current[id] ?? 0) + delta, 0)
      const copy = { ...current }
      if (next === 0) delete copy[id]; else copy[id] = next
      return copy
    })
  }

  async function pay() {
    if (busy || !shiftId || (lines.length === 0 && !sessionId) || !discountValid) return
    setBusy(true)
    setError('')
    try {
      const sale = await checkout(token, { shiftId, items: lines.map((l) => ({ productId: l.product.id, quantity: l.quantity })), idempotencyKey: keyRef.current, ...(sessionId ? { sessionId } : {}), ...(isAdmin && discountType !== 'None' ? { discount: { type: discountType, value: discountNumber, reason: discountReason.trim() } } : {}) })
      setReceipt(sale)
      setCart({})
      setSessionId('')
      setDiscountType('None'); setDiscountValue(''); setDiscountReason('')
      keyRef.current = newIdempotencyKey()
      onSold?.()
      getProducts(token).then(setProducts).catch(() => undefined)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر إتمام البيع. يمكنك المحاولة مرة أخرى دون خطر تكرار الفاتورة.')
    } finally {
      setBusy(false)
    }
  }

  return <div className="inner-page">
    <div className="page-heading compact"><div><p className="eyebrow">الطلبات والمنتجات</p><h1>نقطة البيع</h1><p className="subheading">بيع نقدي. السعر والمخزون والإجمالي يحسبها النظام عند الدفع.</p></div></div>
    {!shiftId && <div className="notice-bar" role="alert"><div className="notice-icon"><CupSoda size={17} /></div><div><strong>لا يوجد شيفت مفتوح</strong><p>افتح شيفتك من شاشة شيفتي قبل البيع.</p></div></div>}
    {error && <div className="notice-bar" role="alert"><div className="notice-icon"><CupSoda size={17} /></div><div><strong>تعذر الإكمال</strong><p>{error}</p></div></div>}
    {receipt && <div className="notice-bar" aria-live="polite"><div className="notice-icon"><CupSoda size={17} /></div><div><strong>تم البيع · فاتورة {receipt.invoiceNumber}</strong><p>الإجمالي {money(receipt.total)}{receipt.discountAmount > 0 ? ` · خصم ${money(receipt.discountAmount)}` : ''}</p><button disabled={printing} onClick={() => void doPrint(receipt.id)}>{printing ? 'جارٍ الطباعة…' : 'طباعة الفاتورة'}</button></div></div>}
    {receipt && <div className="print-only" dir="rtl"><h2>GHOST</h2><p>فاتورة {receipt.invoiceNumber}</p><table><tbody>{receipt.items.map((i, n) => <tr key={n}><td>{i.description}</td><td>{i.quantity}</td><td>{money(i.lineTotal)}</td></tr>)}</tbody></table>{receipt.discountAmount > 0 && <p>خصم: {money(receipt.discountAmount)}</p>}<p><b>الإجمالي: {money(receipt.total)}</b></p></div>}
    <input aria-label="بحث عن منتج أو باركود" placeholder="ابحث بالاسم أو الباركود" value={search} onChange={(e) => setSearch(e.target.value)} />
    {loading && <p aria-live="polite">جاري تحميل المنتجات…</p>}
    {!loading && shown.length === 0 && <p>لا توجد منتجات مطابقة.</p>}
    <div className="device-grid">{shown.map((p) => <button key={p.id} className="device-card available" disabled={!shiftId || (p.trackStock && p.stockQuantity <= (cart[p.id] ?? 0))} onClick={() => change(p.id, 1)}>
      <h4>{p.name}</h4><p>{p.category}</p><strong>{money(p.salePrice)}</strong>{p.trackStock && <small>المتاح {p.stockQuantity}</small>}</button>)}</div>
    <section className="panel" aria-label="سلة البيع">
      <div className="panel-heading"><div><h2>السلة</h2></div></div>
      {lines.length === 0 && <p>السلة فارغة.</p>}
      {lines.map((l) => <div key={l.product.id} className="form-grid"><span>{l.product.name}</span>
        <span><button aria-label="إنقاص" onClick={() => change(l.product.id, -1)}><Minus size={14} /></button> {l.quantity} <button aria-label="زيادة" disabled={l.product.trackStock && l.quantity >= l.product.stockQuantity} onClick={() => change(l.product.id, 1)}><Plus size={14} /></button> <button aria-label="حذف" onClick={() => change(l.product.id, -l.quantity)}><Trash2 size={14} /></button></span></div>)}
      <label>ربط بجلسة منتهية (اختياري)
        <select value={sessionId} onChange={(e) => { setSessionId(e.target.value); touchCheckout() }}>
          <option value="">بدون جلسة</option>
          {doneSessions.map((s) => <option key={s.id} value={s.id}>{new Date(s.startedAt).toLocaleTimeString('ar-EG')} · {s.id.slice(0, 8)}</option>)}
        </select></label>
      {isAdmin && <div className="form-grid">
        <label>الخصم (أدمن فقط)<select value={discountType} onChange={(e) => { setDiscountType(e.target.value as 'None' | 'Fixed' | 'Percent'); touchCheckout() }}><option value="None">بدون</option><option value="Fixed">مبلغ ثابت</option><option value="Percent">نسبة %</option></select></label>
        {discountType !== 'None' && <><label>القيمة<input inputMode="decimal" value={discountValue} onChange={(e) => { setDiscountValue(e.target.value); touchCheckout() }} /></label>
        <label>السبب (مطلوب)<input value={discountReason} onChange={(e) => { setDiscountReason(e.target.value); touchCheckout() }} /></label></>}
      </div>}
      <p>الإجمالي المبدئي: <b>{money(total)}</b></p>
      <button className="primary-button" disabled={busy || !shiftId || (lines.length === 0 && !sessionId) || !discountValid} onClick={pay}>{busy ? 'جارٍ الدفع…' : 'تأكيد البيع نقدًا'}</button>
    </section>
  </div>
}
