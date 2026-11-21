'use client'

import { useCallback, useEffect, useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { approveOutage, getCurrentShift, getOutageReview, type ApiOutageItem } from '@/lib/ghost-api'

function toLocalInput(iso: string) {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Admin review of sessions that were running during a power cut. Renders nothing when there is nothing to review. */
export function AdminOutageReview({ token }: { token: string }) {
  const [items, setItems] = useState<ApiOutageItem[]>([])
  const [shiftId, setShiftId] = useState<string | null>(null)
  const [ends, setEnds] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const [list, shift] = await Promise.all([getOutageReview(token), getCurrentShift(token)])
      setItems(list)
      setShiftId(shift && shift.status === 'Open' ? shift.id : null)
      setEnds((prev) => {
        const next = { ...prev }
        for (const item of list) next[item.sessionId] ??= toLocalInput(item.outageStartedAt)
        return next
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'تعذر تحميل مراجعة انقطاع الكهرباء.')
    }
  }, [token])

  useEffect(() => {
    const timer = setTimeout(() => { void load() }, 0)
    return () => clearTimeout(timer)
  }, [load])

  if (items.length === 0 && !error && !message) return null

  async function approve(item: ApiOutageItem) {
    if (!shiftId) { setError('افتح وردية أولًا لاعتماد الجلسة.'); return }
    const value = ends[item.sessionId]
    if (!value) { setError('حدد وقت النهاية الفعلي.'); return }
    setBusy(item.sessionId); setError(null); setMessage(null)
    try {
      const result = await approveOutage(token, item.sessionId, shiftId, new Date(value).toISOString())
      setMessage(`تم اعتماد الجلسة. الإجمالي ${result.totalAmount} ج.م`)
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'تعذر الاعتماد.')
    } finally { setBusy(null) }
  }

  return (
    <section aria-label="مراجعة انقطاع الكهرباء" style={{ margin: '0 0 16px' }}>
      <Alert variant="warning">
        جلسات كانت تعمل أثناء انقطاع الكهرباء. وقت الانقطاع غير محسوب. آخر نقطة مؤكدة هي آخر نبضة للنظام، وليس وقت الانقطاع الدقيق. حدد وقت النهاية الفعلي ثم اعتمد.
      </Alert>
      {error && <Alert variant="warning">{error}</Alert>}
      {message && <Alert variant="success">{message}</Alert>}
      {items.map((item) => (
        <div key={item.sessionId} style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap', padding: '10px 0' }}>
          <strong>{item.deviceName ?? 'جهاز'}</strong>
          <span>بدأت {new Date(item.startedAt).toLocaleString('ar-EG')}</span>
          <span>آخر نقطة مؤكدة {new Date(item.outageStartedAt).toLocaleString('ar-EG')}</span>
          <span>عودة النظام {new Date(item.outageEndedAt).toLocaleString('ar-EG')}</span>
          <input type="datetime-local" value={ends[item.sessionId] ?? ''} onChange={(e) => setEnds((p) => ({ ...p, [item.sessionId]: e.target.value }))} aria-label="وقت النهاية الفعلي" />
          <button type="button" disabled={busy === item.sessionId || !shiftId} onClick={() => void approve(item)}>اعتماد</button>
        </div>
      ))}
      {!shiftId && items.length > 0 && <p>لا توجد وردية مفتوحة. افتح وردية لتتمكن من الاعتماد.</p>}
    </section>
  )
}
