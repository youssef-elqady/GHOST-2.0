'use client'

import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { getIntakeReport, type IntakeReportRow } from '@/lib/ghost-api'

const money = (v: number) => `${new Intl.NumberFormat('ar-EG').format(v)} ج.م`
const dayStart = (d: string) => new Date(`${d}T00:00:00`).toISOString()
const dayEnd = (d: string) => { const x = new Date(`${d}T00:00:00`); x.setDate(x.getDate() + 1); return x.toISOString() }
const today = () => { const d = new Date(); const p = (n: number) => String(n).padStart(2, '0'); return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}` }

/** Admin report: stock intake per employee and product for a date range (corrections are netted by the server). */
export function AdminIntakeReport({ token }: { token: string }) {
  const [from, setFrom] = useState(today())
  const [to, setTo] = useState(today())
  const [rows, setRows] = useState<IntakeReportRow[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function load() {
    if (!from || !to || to < from) { setError('اختر فترة صحيحة.'); return }
    setBusy(true); setError('')
    try { setRows(await getIntakeReport(token, dayStart(from), dayEnd(to))) }
    catch (e) { setError(e instanceof Error ? e.message : 'تعذر تحميل تقرير التوريد.') }
    finally { setBusy(false) }
  }

  const totalCost = (rows ?? []).reduce((s, r) => s + r.cost, 0)
  const byUser = new Map<string, { ops: number; qty: number; cost: number }>()
  for (const r of rows ?? []) {
    const u = byUser.get(r.userId) ?? { ops: 0, qty: 0, cost: 0 }
    u.ops += r.operations; u.qty += r.quantity; u.cost += r.cost
    byUser.set(r.userId, u)
  }

  return (
    <section className="panel" aria-label="تقرير التوريد" style={{ margin: '0 0 16px' }}>
      <div className="panel-heading"><div><h2>تقرير التوريد</h2><p>إجمالي ما سجله كل موظف من توريدات في الفترة</p></div></div>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="من" />
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="إلى" />
        <button type="button" disabled={busy} onClick={() => void load()}>عرض</button>
      </div>
      {error && <Alert variant="warning">{error}</Alert>}
      {rows && rows.length === 0 && <p className="subheading">لا توجد توريدات في هذه الفترة.</p>}
      {rows && rows.length > 0 && (
        <>
          <h3>لكل موظف</h3>
          {[...byUser.entries()].map(([id, u]) => (
            <div key={id} style={{ padding: '4px 0' }}>موظف {id.slice(0, 8)} · {u.ops} عملية · كمية {u.qty} · تكلفة {money(u.cost)}</div>
          ))}
          <h3>لكل منتج</h3>
          {rows.map((r) => (
            <div key={`${r.userId}-${r.productId}`} style={{ padding: '4px 0' }}>{r.productName} · موظف {r.userName ?? 'غير معروف'} · {r.operations} عملية · كمية {r.quantity} · تكلفة {money(r.cost)}</div>
          ))}
          <strong>الإجمالي: {money(totalCost)}</strong>
        </>
      )}
    </section>
  )
}
