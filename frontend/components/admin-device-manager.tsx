'use client'

import { useCallback, useEffect, useState } from 'react'
import { Check, CircleAlert, Gamepad2, RefreshCw, Save, ShieldCheck } from 'lucide-react'
import { getDevices, seedDefaultDevices, updateDeviceRates, type ApiDevice, type RateInput } from '@/lib/ghost-api'

type RateDraft = { single: string; multi: string }

function toDraft(device: ApiDevice): RateDraft {
  return {
    single: String(device.rates.find((rate) => rate.playType.toLowerCase() === 'single')?.hourlyRate ?? ''),
    multi: String(device.rates.find((rate) => rate.playType.toLowerCase() === 'multi')?.hourlyRate ?? ''),
  }
}

export function AdminDeviceManager() {
  const [devices, setDevices] = useState<ApiDevice[]>([])
  const [drafts, setDrafts] = useState<Record<string, RateDraft>>({})
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [savingId, setSavingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const reload = useCallback(async () => {
    setLoading(true)
    try {
      const items = await getDevices()
      setDevices(items.filter((device) => device.isActive))
      setDrafts(Object.fromEntries(items.map((device) => [device.id, toDraft(device)])))
      setError(null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر تحميل الأجهزة.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void reload() }, [reload])

  async function addDefaults() {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const result = await seedDefaultDevices()
      await reload()
      setNotice(result.added > 0
        ? `تمت إضافة ${result.added} أجهزة إلى قاعدة البيانات. اضبط أسعارها من هذه الصفحة.`
        : 'الأجهزة الافتراضية موجودة بالفعل؛ لم يتم إنشاء نسخ مكررة.')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر إضافة الأجهزة.')
    } finally {
      setBusy(false)
    }
  }

  async function saveRates(device: ApiDevice) {
    const draft = drafts[device.id] ?? { single: '', multi: '' }
    const rates: RateInput[] = []
    for (const [value, playType, label] of [
      [draft.single, 'Single', 'الفردية'],
      [draft.multi, 'Multi', 'المتعددة'],
    ] as const) {
      if (value.trim() === '') continue
      const hourlyRate = Number(value)
      if (!Number.isFinite(hourlyRate) || hourlyRate <= 0) {
        setError(`أدخل سعرًا صحيحًا أكبر من صفر للجلسة ${label} في جهاز ${device.name}.`)
        return
      }
      rates.push({ playType, hourlyRate })
    }

    if (rates.length === 0) {
      setError(`أدخل سعر الفردية أو المتعددة على الأقل لجهاز ${device.name}.`)
      return
    }

    setSavingId(device.id)
    setError(null)
    setNotice(null)
    try {
      const updated = await updateDeviceRates(device.id, rates)
      setDevices((current) => current.map((item) => item.id === updated.id ? updated : item))
      setDrafts((current) => ({ ...current, [updated.id]: toDraft(updated) }))
      setNotice(`تم حفظ تسعير ${device.name} في قاعدة البيانات.`)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر حفظ التسعير.')
    } finally {
      setSavingId(null)
    }
  }

  const groups = [
    { title: 'أجهزة الصالة العادية', items: devices.filter((device) => device.category === 'عادي') },
    { title: 'أجهزة VIP', items: devices.filter((device) => device.category === 'VIP') },
    { title: 'الغرف', items: devices.filter((device) => device.category === 'Room') },
    { title: 'أجهزة أخرى', items: devices.filter((device) => !['عادي', 'VIP', 'Room'].includes(device.category)) },
  ].filter((group) => group.items.length > 0)

  return (
    <section style={{ display: 'grid', gap: 18, marginTop: 18 }}>
      <div className="notice-bar">
        <div className="notice-icon"><ShieldCheck size={18} /></div>
        <div>
          <strong>إدارة الأجهزة والتسعير</strong>
          <p>الأجهزة تُحفظ في قاعدة البيانات بدون سعر افتراضي. الإدارة وحدها هي التي تضبط سعر الساعة للفردية والمتعددة؛ ولا يمكن بدء جلسة لنوع لعب بلا سعر محفوظ.</p>
        </div>
        <button className="primary-button" onClick={addDefaults} disabled={busy}>
          <Gamepad2 size={16} /> {busy ? 'جاري الإضافة...' : 'إضافة أجهزة GHOST الافتراضية'}
        </button>
      </div>

      {notice && <div className="notice-bar"><div className="notice-icon"><Check size={17} /></div><div><strong>تمت العملية</strong><p>{notice}</p></div></div>}
      {error && <div className="notice-bar"><div className="notice-icon"><CircleAlert size={17} /></div><div><strong>تعذر إكمال العملية</strong><p>{error}</p></div></div>}

      <div className="section-header">
        <div><h2>الأجهزة المسجلة</h2><p>{loading ? 'جاري تحميل البيانات من الـ API...' : `${devices.length} أجهزة محفوظة في النظام`}</p></div>
        <button className="text-button" onClick={() => void reload()} disabled={loading}><RefreshCw size={15} /> تحديث القائمة</button>
      </div>

      {loading && <div className="panel" style={{ padding: 24 }}>جاري قراءة الأجهزة من قاعدة البيانات...</div>}
      {!loading && devices.length === 0 && (
        <div className="panel" style={{ padding: 24 }}>
          <strong>قاعدة البيانات لا تحتوي على أجهزة نشطة.</strong>
          <p className="subheading">اضغط «إضافة أجهزة GHOST الافتراضية» لإضافة PS2 إلى PS6 وVIP 1 إلى VIP 3 وRoom 1 وRoom 2.</p>
        </div>
      )}

      {groups.map((group) => (
        <section key={group.title} className="panel" style={{ padding: 20 }}>
          <div className="panel-heading"><div><h2>{group.title}</h2><p>{group.items.length} أجهزة</p></div><Gamepad2 size={19} /></div>
          <div style={{ display: 'grid', gap: 12, marginTop: 16 }}>
            {group.items.map((device) => {
              const draft = drafts[device.id] ?? { single: '', multi: '' }
              const configured = device.rates.length > 0
              return (
                <article key={device.id} style={{ display: 'grid', gridTemplateColumns: 'minmax(130px, 1fr) minmax(120px, 1fr) minmax(120px, 1fr) auto', gap: 12, alignItems: 'end', border: '1px solid var(--border)', borderRadius: 12, padding: 14 }}>
                  <div>
                    <strong>{device.name}</strong>
                    <p style={{ margin: '5px 0 0', fontSize: 12, color: 'var(--muted-foreground)' }}>{device.category} · {configured ? 'السعر مضبوط' : 'لم يتم تحديد السعر'}</p>
                  </div>
                  <label style={{ display: 'grid', gap: 6, fontSize: 13 }}>
                    سعر الفردية / ساعة (ج.م)
                    <input type="number" min="0.01" step="0.5" value={draft.single} onChange={(event) => setDrafts((current) => ({ ...current, [device.id]: { ...draft, single: event.target.value } }))} placeholder="لم يحدد" style={{ width: '100%', minWidth: 0, padding: '10px 12px', borderRadius: 8, border: '1px solid var(--border)', background: 'var(--input)', color: 'var(--foreground)' }} />
                  </label>
                  <label style={{ display: 'grid', gap: 6, fontSize: 13 }}>
                    سعر المتعددة / ساعة (ج.م)
                    <input type="number" min="0.01" step="0.5" value={draft.multi} onChange={(event) => setDrafts((current) => ({ ...current, [device.id]: { ...draft, multi: event.target.value } }))} placeholder="لم يحدد" style={{ width: '100%', minWidth: 0, padding: '10px 12px', borderRadius: 8, border: '1px solid var(--border)', background: 'var(--input)', color: 'var(--foreground)' }} />
                  </label>
                  <button className="primary-button" onClick={() => void saveRates(device)} disabled={savingId === device.id || loading}>
                    <Save size={15} /> {savingId === device.id ? 'حفظ...' : 'حفظ السعر'}
                  </button>
                </article>
              )
            })}
          </div>
        </section>
      ))}
    </section>
  )
}
