'use client'

import { useState } from 'react'
import { Gamepad2, Pause, Play, X } from 'lucide-react'
import { completeSession, pauseSession, resumeSession, transferSession, type PlayTypeName } from '@/lib/ghost-api'

export type ControlDevice = { id: string; name: string; apiStatus: string }
export type TransferTarget = { id: string; name: string }

/** Actions on a running session. The server enforces every rule (state, shift, rates); the UI shows its Arabic error. */
export function SessionControls({ token, device, sessionId, shiftId, targets, onClose, onDone }: {
  token: string; device: ControlDevice; sessionId: string | null; shiftId: string | null
  targets: TransferTarget[]; onClose: () => void; onDone: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [target, setTarget] = useState('')
  const [playType, setPlayType] = useState<PlayTypeName>('Single')
  const paused = device.apiStatus.toLowerCase() === 'paused'

  async function run(action: () => Promise<unknown>) {
    if (busy) return
    setBusy(true)
    setError('')
    try { await action(); onDone() } catch (reason) { setError(reason instanceof Error ? reason.message : 'تعذر إكمال العملية.') } finally { setBusy(false) }
  }

  return <div className="modal-backdrop" onClick={onClose}><div className="session-modal" onClick={(e) => e.stopPropagation()}>
    <div className="modal-header"><div><span className="eyebrow session-step">إدارة الجلسة</span><h2>{device.name}</h2><p>{paused ? 'الجلسة متوقفة مؤقتًا' : 'الجلسة قيد التشغيل'}</p></div><button className="icon-button" onClick={onClose} aria-label="إغلاق"><X size={18} /></button></div>
    {!sessionId && <p role="alert">لم يتم العثور على جلسة نشطة لهذا الجهاز. حدّث الصفحة.</p>}
    {sessionId && <>
      <div className="mode-buttons">
        {paused
          ? <button disabled={busy} onClick={() => run(() => resumeSession(token, sessionId))}><Play size={18} /><strong>استئناف</strong></button>
          : <button disabled={busy} onClick={() => run(() => pauseSession(token, sessionId))}><Pause size={18} /><strong>إيقاف مؤقت</strong></button>}
        <button disabled={busy || !shiftId} title={shiftId ? undefined : 'افتح شيفتك أولًا'} onClick={() => run(() => completeSession(token, sessionId, shiftId!))}><Gamepad2 size={18} /><strong>إنهاء الجلسة</strong></button>
      </div>
      <div className="form-grid">
        <div><label>نقل إلى جهاز متاح</label>
          <select value={target} onChange={(e) => setTarget(e.target.value)}><option value="">اختر جهازًا</option>{targets.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></div>
        <div><label>نوع الجلسة بعد النقل</label>
          <select value={playType} onChange={(e) => setPlayType(e.target.value as PlayTypeName)}><option value="Single">فردية</option><option value="Multi">متعددة</option></select></div>
      </div>
      <button className="primary-button" disabled={busy || !target} onClick={() => run(() => transferSession(token, sessionId, target, playType))}>نقل الجلسة</button>
    </>}
    {error && <p className="admin-login-error" role="alert">{error}</p>}
  </div></div>
}
