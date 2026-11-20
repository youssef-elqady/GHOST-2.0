'use client'

import { Suspense, use, useState } from 'react'
import Link from 'next/link'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { ArrowUpLeft, Check, Gamepad2, Plus, RefreshCw, Save, Search } from 'lucide-react'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Empty } from '@/components/ui/empty'
import { clearDeviceQueryCache, getDeviceQuery } from '@/lib/device-query'
import { createDevice, updateDeviceRates, type ApiDevice, type RateInput } from '@/lib/ghost-api'

const validOptionalRate = z.string().trim().refine((value) => !value || (Number.isFinite(Number(value)) && Number(value) > 0), 'أدخل سعرًا موجبًا صالحًا.')
const deviceSchema = z.object({
  name: z.string().trim().min(1, 'أدخل اسم الجهاز.').max(100, 'الاسم طويل جدًا.'),
  category: z.string().trim().min(1, 'أدخل فئة الجهاز.').max(100, 'اسم الفئة طويل جدًا.'),
  Single: validOptionalRate,
  Multi: validOptionalRate,
}).refine((values) => Boolean(values.Single || values.Multi), { message: 'أدخل سعرًا واحدًا على الأقل؛ لا توجد أسعار افتراضية.', path: ['Single'] })
const ratesSchema = z.object({
  Single: z.string().trim().refine((value) => !value || (Number.isFinite(Number(value)) && Number(value) > 0), 'أدخل قيمة موجبة صالحة.'),
  Multi: z.string().trim().refine((value) => !value || (Number.isFinite(Number(value)) && Number(value) > 0), 'أدخل قيمة موجبة صالحة.'),
})
type DeviceFormValues = z.infer<typeof deviceSchema>
type DraftRates = z.infer<typeof ratesSchema>

function draftsFromDevices(devices: ApiDevice[]): Record<string, DraftRates> {
  return Object.fromEntries(devices.map((device) => [device.id, {
    Single: String(device.rates.find((rate) => rate.playType.toLowerCase() === 'single')?.hourlyRate ?? ''),
    Multi: String(device.rates.find((rate) => rate.playType.toLowerCase() === 'multi')?.hourlyRate ?? ''),
  }]))
}

function deviceStatusLabel(status: string, isActive: boolean) {
  if (!isActive) return 'غير نشط'
  return ({ Available: 'متاح', Running: 'قيد التشغيل', Paused: 'متوقف مؤقتًا', Reserved: 'محجوز', Maintenance: 'صيانة', Offline: 'غير متصل' } as Record<string, string>)[status] ?? status
}

function deviceStatusTone(status: string, isActive: boolean) {
  if (!isActive || status === 'Offline') return 'neutral' as const
  if (status === 'Running') return 'info' as const
  if (status === 'Paused' || status === 'Reserved' || status === 'Maintenance') return 'warning' as const
  return status === 'Available' ? 'success' as const : 'neutral' as const
}

function DeviceManagerLoading() {
  return <div className="admin-loading-state" role="status">جارٍ تحميل الأجهزة من API…</div>
}

function DeviceManagerContent({ token, deviceId, revision, onRefresh, message, onMessage }: {
  token: string
  deviceId?: string
  revision: number
  onRefresh: () => void
  message: string
  onMessage: (message: string) => void
}) {
  const result = use(getDeviceQuery(token, revision))
  const devices = result.status === 'ready' ? result.devices : []
  const [search, setSearch] = useState('')
  const query = search.trim().toLocaleLowerCase('ar')
  const visibleDevices = devices.filter((device) => {
    if (deviceId && device.id.toLowerCase() !== deviceId.toLowerCase()) return false
    return !query || `${device.id} ${device.name} ${device.category} ${device.status} ${deviceStatusLabel(device.status, device.isActive)}`.toLocaleLowerCase('ar').includes(query)
  })
  const groups = [...new Set(visibleDevices.map((device) => device.category))]
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [ratesDraft, setRatesDraft] = useState<Record<string, DraftRates>>(() => result.status === 'ready' ? draftsFromDevices(result.devices) : {})
  const form = useForm<DeviceFormValues>({ resolver: zodResolver(deviceSchema), defaultValues: { name: '', category: '', Single: '', Multi: '' } })

  async function create(values: DeviceFormValues) {
    setSaving('create')
    setError('')
    onMessage('')
    try {
      const rates: RateInput[] = []
      if (values.Single) rates.push({ playType: 'Single', hourlyRate: Number(values.Single) })
      if (values.Multi) rates.push({ playType: 'Multi', hourlyRate: Number(values.Multi) })
      await createDevice(token, { name: values.name.trim(), category: values.category.trim(), rates })
      form.reset({ name: '', category: '', Single: '', Multi: '' })
      onMessage('تم إنشاء الجهاز وحفظ الأسعار التي أدخلتها.')
      onRefresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر إنشاء الجهاز.')
    } finally {
      setSaving(null)
    }
  }

  async function saveRates(device: ApiDevice) {
    const draft = ratesDraft[device.id] ?? { Single: '', Multi: '' }
    const parsed = ratesSchema.safeParse(draft)
    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? 'أدخل أسعارًا موجبة وصالحة.')
      return
    }
    const rates: RateInput[] = []
    for (const playType of ['Single', 'Multi'] as const) {
      const value = parsed.data[playType]
      const existing = device.rates.find((rate) => rate.playType.toLowerCase() === playType.toLowerCase())
      if (!value) {
        if (existing) rates.push({ playType, hourlyRate: existing.hourlyRate })
        continue
      }
      rates.push({ playType, hourlyRate: Number(value) })
    }
    if (!rates.length) {
      setError('أدخل سعرًا واحدًا على الأقل. لن يتم استبدال الأسعار بقيم افتراضية.')
      return
    }
    if (!window.confirm(`حفظ أسعار الساعة للجهاز «${device.name}»؟`)) return
    setSaving(device.id)
    setError('')
    onMessage('')
    try {
      await updateDeviceRates(device.id, rates, token)
      onMessage(`تم حفظ أسعار «${device.name}» في النظام.`)
      onRefresh()
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر حفظ الأسعار.')
    } finally {
      setSaving(null)
    }
  }

  const requestError = result.status === 'error' ? result.message : ''
  return <section className="admin-device-manager" aria-label="إدارة الأجهزة">
    <Alert variant="info"><Gamepad2 aria-hidden="true" /><span><strong>العمليات المتاحة من API</strong><small>عرض الأجهزة وإنشاؤها وتحديث الأسعار المحفوظة فقط. لا تتوفر عمليات صيانة أو تفعيل أو سجل جلسات.</small></span></Alert>
    {(error || requestError) && <Alert variant="warning"><span><strong>تعذر إكمال العملية</strong><small>{error || requestError}</small></span><button className="admin-inline-action" onClick={onRefresh}>إعادة المحاولة</button></Alert>}
    {message && <Alert variant="success"><Check aria-hidden="true" /><span>{message}</span></Alert>}
    {!deviceId && <section className="admin-device-create"><div><h2>إضافة جهاز</h2><p>أدخل سعرًا واحدًا على الأقل كما يتطلب API؛ لا تُضاف أسعار افتراضية.</p></div>
      <form className="admin-device-form" onSubmit={form.handleSubmit(create)}>
        <label>اسم الجهاز<input {...form.register('name')} aria-invalid={Boolean(form.formState.errors.name)} aria-describedby={form.formState.errors.name ? 'device-name-error' : undefined} placeholder="مثال: PS5 — 1" maxLength={100} />{form.formState.errors.name && <small id="device-name-error" role="alert">{form.formState.errors.name.message}</small>}</label>
        <label>الفئة<input {...form.register('category')} aria-invalid={Boolean(form.formState.errors.category)} aria-describedby={form.formState.errors.category ? 'device-category-error' : undefined} placeholder="Room" maxLength={100} />{form.formState.errors.category && <small id="device-category-error" role="alert">{form.formState.errors.category.message}</small>}</label>
        <label>سعر Single (ج.م / ساعة)<input {...form.register('Single')} inputMode="decimal" type="number" step="any" min="0.01" aria-invalid={Boolean(form.formState.errors.Single)} aria-describedby={form.formState.errors.Single ? 'device-single-error' : undefined} placeholder="غير محدد" />{form.formState.errors.Single && <small id="device-single-error" role="alert">{form.formState.errors.Single.message}</small>}</label>
        <label>سعر Multi (ج.م / ساعة)<input {...form.register('Multi')} inputMode="decimal" type="number" step="any" min="0.01" aria-invalid={Boolean(form.formState.errors.Multi)} aria-describedby={form.formState.errors.Multi ? 'device-multi-error' : undefined} placeholder="غير محدد" />{form.formState.errors.Multi && <small id="device-multi-error" role="alert">{form.formState.errors.Multi.message}</small>}</label>
        <button className="admin-primary-button" type="submit" disabled={saving !== null || Boolean(requestError)}><Plus aria-hidden="true" />{saving === 'create' ? 'جارٍ الإنشاء…' : 'إنشاء الجهاز'}</button>
      </form>
    </section>}
    {deviceId && <Link className="admin-back-link" href="/admin/devices"><ArrowUpLeft aria-hidden="true" />العودة إلى الأجهزة</Link>}
    <div className="admin-devices-heading"><div><h2>{deviceId ? 'تفاصيل الجهاز' : 'الأجهزة المحفوظة'}</h2><p><span className={`admin-source admin-source--${requestError ? 'unavailable' : 'live'}`}><i aria-hidden="true" />{requestError ? 'غير متاح' : 'مباشر من API'}</span>{!deviceId && ` · ${devices.length} جهاز`}</p></div><button className="admin-secondary-button" onClick={onRefresh}><RefreshCw aria-hidden="true" />تحديث</button></div>
    {!deviceId && <label className="admin-device-search"><Search aria-hidden="true" /><span className="sr-only">البحث في الأجهزة</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="ابحث بالاسم أو الفئة أو الحالة" /></label>}
    {requestError ? null : visibleDevices.length === 0 ? <Empty icon={<Gamepad2 aria-hidden="true" />} title={deviceId ? 'لم يتم العثور على الجهاز' : 'لا توجد أجهزة محفوظة'} description={deviceId ? 'تحقق من المعرف أو ارجع إلى قائمة الأجهزة.' : 'أضف جهازًا من النموذج أعلاه أو تحقق من اتصال API.'} /> : groups.map((category) => <section className="admin-device-group" key={category}><header><div><Gamepad2 aria-hidden="true" /><h3>{category || 'بلا فئة'}</h3></div><span>{visibleDevices.filter((device) => device.category === category).length} جهاز</span></header><div className="admin-device-list">{visibleDevices.filter((device) => device.category === category).map((device) => {
      const draft = ratesDraft[device.id] ?? { Single: '', Multi: '' }
      return <article className="admin-device-row" key={device.id}><div className="admin-device-name"><strong>{device.name}</strong><span className="admin-mono">{device.id}</span><Badge tone={deviceStatusTone(device.status, device.isActive)}>{deviceStatusLabel(device.status, device.isActive)}</Badge>{!deviceId && <Link href={`/admin/devices/${encodeURIComponent(device.id)}`} className="admin-row-link">تفاصيل الجهاز</Link>}</div>
        <label>سعر Single (ج.م / ساعة)<input inputMode="decimal" type="number" step="any" value={draft.Single} onChange={(event) => setRatesDraft((current) => ({ ...current, [device.id]: { ...draft, Single: event.target.value } }))} placeholder={device.rates.some((rate) => rate.playType.toLowerCase() === 'single') ? undefined : 'غير محدد'} /></label>
        <label>سعر Multi (ج.م / ساعة)<input inputMode="decimal" type="number" step="any" value={draft.Multi} onChange={(event) => setRatesDraft((current) => ({ ...current, [device.id]: { ...draft, Multi: event.target.value } }))} placeholder={device.rates.some((rate) => rate.playType.toLowerCase() === 'multi') ? undefined : 'غير محدد'} /></label>
        <small className="admin-device-rate-note">اترك السعر الحالي فارغًا للاحتفاظ به؛ لا تدعم API حذف سعر محفوظ.</small>
        <button className="admin-secondary-button" onClick={() => void saveRates(device)} disabled={saving !== null || Boolean(requestError)}><Save aria-hidden="true" />{saving === device.id ? 'جارٍ الحفظ…' : 'حفظ الأسعار'}</button>
      </article>
    })}</div></section>)}
  </section>
}

export function AdminDeviceManager({ token, deviceId }: { token: string; deviceId?: string }) {
  const [revision, setRevision] = useState(0)
  const [message, setMessage] = useState('')
  const refresh = () => {
    clearDeviceQueryCache()
    setRevision((value) => value + 1)
  }
  return <Suspense fallback={<DeviceManagerLoading />}><DeviceManagerContent key={revision} token={token} deviceId={deviceId} revision={revision} onRefresh={refresh} message={message} onMessage={setMessage} /></Suspense>
}
