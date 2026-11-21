'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import { CupSoda, Minus, Plus, Trash2 } from 'lucide-react'
import { checkout, getProducts, newIdempotencyKey, type ApiProduct, type ApiSale } from '@/lib/ghost-api'

const money = (value: number) => `${new Intl.NumberFormat('ar-EG').format(value)} ج.م`

/** Cash POS. The cart total here is a preview: the server recomputes prices, stock and totals at checkout. */
export function StaffPos({ token, shiftId, onSold }: { token: string; shiftId: string | null; onSold?: () => void }) {
  const [products, setProducts] = useState<ApiProduct[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [cart, setCart] = useState<Record<string, number>>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [receipt, setReceipt] = useState<ApiSale | null>(null)
  // One key per cart content: a retry after a timeout reuses it, so the server can never charge twice.
  const keyRef = useRef<string>(newIdempotencyKey())

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
    if (busy || !shiftId || lines.length === 0) return
    setBusy(true)
    setError('')
    try {
      const sale = await checkout(token, { shiftId, items: lines.map((l) => ({ productId: l.product.id, quantity: l.quantity })), idempotencyKey: keyRef.current })
      setReceipt(sale)
      setCart({})
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
    {receipt && <div className="notice-bar" aria-live="polite"><div className="notice-icon"><CupSoda size={17} /></div><div><strong>تم البيع · فاتورة {receipt.invoiceNumber}</strong><p>الإجمالي {money(receipt.total)}</p></div></div>}
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
      <p>الإجمالي المبدئي: <b>{money(total)}</b></p>
      <button className="primary-button" disabled={busy || !shiftId || lines.length === 0} onClick={pay}>{busy ? 'جارٍ الدفع…' : 'تأكيد البيع نقدًا'}</button>
    </section>
  </div>
}
