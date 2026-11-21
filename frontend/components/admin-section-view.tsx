'use client'

import { Suspense, use, useMemo, useState, type ReactNode } from 'react'
import Link from 'next/link'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Activity, AlertCircle, ArrowLeft, ArrowUpDown, CalendarDays, CheckCircle2, Clock3, Download, FileText, Info, Package, Search, Settings, ShieldCheck, Wallet, type LucideIcon } from 'lucide-react'
import { Alert } from '@/components/ui/alert'
import { Badge, type BadgeTone } from '@/components/ui/badge'
import { Empty } from '@/components/ui/empty'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { PageHeader } from '@/components/ui/page-header'
import { AdminOutageReview } from '@/components/admin-outage-review'
import { AdminIntakeReport } from '@/components/admin-intake-report'
import { AdminDeviceManager } from '@/components/admin-device-manager'
import { clearDeviceQueryCache, getDeviceQuery } from '@/lib/device-query'
import { formatCurrency, formatDate, formatDigits, type DigitMode } from '@/lib/admin-format'
import type { Source } from '@/lib/demo/admin-data'
import { demoAnalytics, demoAudit, demoEmployees, demoInventory, demoProducts, demoReconciliations, demoSales, demoSessions, demoShifts, demoUsers } from '@/lib/demo/admin-data'

type Preferences = { digits: DigitMode; timezone: string; demoMode: boolean }
type SectionProps = {
  section: string
  token: string
  deviceId?: string
  recordId?: string
  digits: DigitMode
  timezone: string
  demoMode: boolean
  dark: boolean
  onThemeChanged: (dark: boolean) => void
  onPreferencesSaved: (preferences: Preferences) => void
}

type Row = { id: string; date: string; name: string; detail: string; status: string; amount: number; durationMinutes?: number }
type SortKey = 'name' | 'date' | 'amount'

function statusTone(status: string): BadgeTone {
  if (['مكتملة', 'نشط', 'متوفر', 'نجاح'].includes(status)) return 'success'
  if (['مراجعة', 'قيد المراجعة', 'منخفض'].includes(status)) return 'warning'
  if (['ملغاة', 'مستردة'].includes(status)) return 'danger'
  if (status === 'توريد') return 'info'
  return 'neutral'
}

const titles: Record<string, string> = {
  analytics: 'التحليلات', sessions: 'الجلسات', devices: 'الأجهزة', products: 'المنتجات', inventory: 'المخزون',
  sales: 'المبيعات والفواتير', employees: 'الموظفون', shifts: 'الشيفتات', 'cash-reconciliation': 'مطابقة الخزينة',
  reports: 'التقارير', 'audit-logs': 'سجل التدقيق', 'users-roles': 'المستخدمون والصلاحيات', settings: 'الإعدادات',
}
const descriptions: Record<string, string> = {
  analytics: 'اتجاهات وإشغال وأداء توضيحي للفترة المعروضة.', sessions: 'قائمة توضيحية للقراءة فقط. لا تتضمن إجراءات تشغيلية.',
  devices: 'الأجهزة والأسعار المحفوظة من واجهة الإدارة الفعلية.', products: 'كتالوج منتجات توضيحي حتى تتوفر واجهة قراءة من الخادم.',
  inventory: 'مستويات وحركات توضيحية؛ لا تمثل مخزونًا فعليًا.', sales: 'سجل مبيعات وفواتير توضيحي للقراءة فقط.',
  employees: 'دليل توضيحي للقراءة فقط.', shifts: 'سجل ورديات توضيحي للقراءة فقط.',
  'cash-reconciliation': 'مطابقات توضيحية؛ لا تغيّر أي رصيد نقدي.', reports: 'تقارير من مجموعة البيانات التوضيحية الحالية.',
  'audit-logs': 'خط زمني توضيحي للقراءة فقط.', 'users-roles': 'مستخدمون وأدوار وصلاحيات توضيحية للقراءة فقط.',
  settings: 'تفضيلات هذا المتصفح فقط؛ لا تغيّر إعدادات الخادم.',
}

function SourceLabel({ source }: { source: Source | 'local' }) {
  const label = source === 'live' ? 'مباشر من API' : source === 'demo' ? 'Demo · توضيحي' : source === 'local' ? 'محلي في هذا المتصفح' : 'غير متاح'
  return <span className={`admin-source admin-source--${source}`}><i aria-hidden="true" />{label}</span>
}

function Notice({ children, type = 'info' }: { children: ReactNode; type?: 'info' | 'warning' | 'success' }) {
  const Icon = type === 'warning' ? AlertCircle : type === 'success' ? CheckCircle2 : Info
  return <Alert variant={type}><Icon aria-hidden="true" />{children}</Alert>
}

function PanelCard({ title, description, source, children, className = '' }: { title: string; description?: string; source: Source | 'local'; children: ReactNode; className?: string }) {
  return <Card className={`admin-panel-card ${className}`}>
    <CardHeader>
      <div><CardTitle>{title}</CardTitle>{description && <CardDescription>{description}</CardDescription>}</div>
      <SourceLabel source={source} />
    </CardHeader>
    <CardContent>{children}</CardContent>
  </Card>
}

function StatCard({ label, value, icon: Icon, source, digits }: { label: string; value: string; icon: LucideIcon; source: Source; digits: DigitMode }) {
  return <Card className="admin-stat-card">
    <CardHeader>
      <div className="admin-stat-top"><span className="admin-stat-icon"><Icon aria-hidden="true" /></span><SourceLabel source={source} /></div>
      <CardTitle>{label}</CardTitle>
    </CardHeader>
    <CardContent><strong>{formatDigits(value, digits)}</strong></CardContent>
  </Card>
}

function OverviewLoading() {
  return <div className="admin-page-content"><PageHeader title="نظرة عامة" description="مؤشرات الإدارة ومصادرها." /><div className="admin-loading-state" role="status">جارٍ تحميل بيانات الأجهزة من API…</div></div>
}

function AdminOverview({ token, digits, timezone, demoMode }: Pick<SectionProps, 'token' | 'digits' | 'timezone' | 'demoMode'>) {
  const [revision, setRevision] = useState(0)
  const refresh = () => { clearDeviceQueryCache(); setRevision((value) => value + 1) }
  return <Suspense fallback={<OverviewLoading />}><AdminOverviewData key={revision} token={token} revision={revision} digits={digits} timezone={timezone} demoMode={demoMode} onRefresh={refresh} /></Suspense>
}

function AdminOverviewData({ token, revision, digits, timezone, demoMode, onRefresh }: Pick<SectionProps, 'token' | 'digits' | 'timezone' | 'demoMode'> & { revision: number; onRefresh: () => void }) {
  const result = use(getDeviceQuery(token, revision))
  const devices = result.status === 'ready' ? result.devices : []
  const liveUnavailable = result.status === 'error'
  const active = devices.filter((device) => device.isActive).length
  const inactive = devices.length - active
  const today = new Intl.DateTimeFormat(digits === 'arabic' ? 'ar-EG' : 'ar-EG-u-nu-latn', { weekday: 'long', day: 'numeric', month: 'long', timeZone: timezone }).format(new Date())
  const demoSource: Source = demoMode ? 'demo' : 'unavailable'
  const demoValue = (value: string) => demoMode ? formatDigits(value, digits) : '—'
  return <div className="admin-page-content">
    <PageHeader title="نظرة عامة" description={`ملخص الإدارة · ${today}`} />
    {liveUnavailable && <Notice type="warning"><span><strong>تعذر تحميل الأجهزة من API.</strong><small>{result.message}</small><button className="admin-inline-action" onClick={onRefresh}>إعادة المحاولة</button></span></Notice>}
    {!liveUnavailable && devices.length === 0 && <Notice>لا توجد أجهزة مسجلة في مصدر البيانات الحالي.</Notice>}
    <div className="admin-stat-grid">
      <StatCard label="إجمالي الأجهزة" value={liveUnavailable ? '—' : String(devices.length)} icon={Package} source={liveUnavailable ? 'unavailable' : 'live'} digits={digits} />
      <StatCard label="أجهزة نشطة" value={liveUnavailable ? '—' : String(active)} icon={CheckCircle2} source={liveUnavailable ? 'unavailable' : 'live'} digits={digits} />
      <StatCard label="أجهزة غير نشطة" value={liveUnavailable ? '—' : String(inactive)} icon={AlertCircle} source={liveUnavailable ? 'unavailable' : 'live'} digits={digits} />
      <StatCard label="إيراد توضيحي" value={demoValue('12,450 ج.م')} icon={Wallet} source={demoSource} digits={digits} />
      <StatCard label="جلسات توضيحية" value={demoValue('38')} icon={Activity} source={demoSource} digits={digits} />
    </div>
    <div className="admin-overview-grid">
      <PanelCard title="اتجاه الإيرادات" description="قيم أسبوعية توضيحية، وليست أداءً فعليًا." source={demoSource}>
        {demoMode ? <div className="admin-chart-wrap"><ResponsiveContainer width="100%" height="100%"><AreaChart data={demoAnalytics} margin={{ top: 8, right: 8, left: 4, bottom: 0 }}><defs><linearGradient id="overviewRevenue" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="#147653" stopOpacity={0.26} /><stop offset="95%" stopColor="#147653" stopOpacity={0.02} /></linearGradient></defs><CartesianGrid strokeDasharray="3 5" vertical={false} stroke="var(--admin-line)" /><XAxis dataKey="day" tickLine={false} axisLine={false} tick={{ fill: 'var(--admin-muted)', fontSize: 10 }} /><YAxis tickLine={false} axisLine={false} tick={{ fill: 'var(--admin-muted)', fontSize: 9 }} tickFormatter={(value) => formatCurrency(Number(value), digits)} width={62} /><Tooltip formatter={(value) => formatCurrency(Number(value), digits)} /><Area type="monotone" dataKey="revenue" name="الإيراد التوضيحي" stroke="#147653" strokeWidth={2} fill="url(#overviewRevenue)" /></AreaChart></ResponsiveContainer></div> : <UnavailableState description="فعّل وضع Demo لعرض بيانات توضيحية." />}
      </PanelCard>
      <PanelCard title="حالة الأجهزة" description="الحالة والإجمالي من GET /api/devices." source={liveUnavailable ? 'unavailable' : 'live'}>
        {liveUnavailable ? <UnavailableState description="لا يمكن عرض حالة الأجهزة قبل نجاح اتصال API." /> : devices.length === 0 ? <EmptyState description="أضف جهازًا من صفحة الأجهزة لبدء المتابعة." icon={Package} /> : <div className="admin-device-summary"><div><span className="admin-status-dot" />نشطة<strong>{formatDigits(String(active), digits)}</strong></div><div><span className="admin-status-dot admin-status-dot--muted" />غير نشطة<strong>{formatDigits(String(inactive), digits)}</strong></div><Link href="/admin/devices">إدارة الأجهزة <ArrowLeft aria-hidden="true" /></Link></div>}
      </PanelCard>
    </div>
    <div className="admin-lower-grid">
      <PanelCard title="النشاط الأخير" description="سجل تمثيلي للواجهة؛ ليس سجل التدقيق الفعلي." source={demoSource}>
        {demoMode ? <div className="admin-activity-list">{demoAudit.slice(0, 4).map((item) => <div key={item.id}><span className="admin-activity-icon"><Activity aria-hidden="true" /></span><span><strong>{item.name}</strong><small>{item.detail}</small></span><time className="admin-mono">{formatDate(item.date, timezone, digits)}</time></div>)}</div> : <UnavailableState description="فعّل وضع Demo لعرض سجل توضيحي." />}
      </PanelCard>
      <PanelCard title="معلومات العرض" description="تفضيلات محلية لهذه الواجهة." source="local">
        <div className="admin-system-info"><span><ShieldCheck aria-hidden="true" />مكان واحد</span><span><Clock3 aria-hidden="true" />{timezone}</span><span><CalendarDays aria-hidden="true" />{today}</span></div>
      </PanelCard>
    </div>
  </div>
}

function UnavailableState({ description }: { description: string }) {
  return <Empty className="admin-unavailable" meta={<SourceLabel source="unavailable" />} icon={<span className="admin-unavailable-icon"><Info aria-hidden="true" /></span>} title="البيانات غير متاحة" description={description} />
}

function EmptyState({ description, icon: Icon = Info }: { description: string; icon?: LucideIcon }) {
  return <Empty icon={<Icon aria-hidden="true" />} description={description} />
}

function DataTable({ section, rows, digits, timezone, exportable = false }: { section: string; rows: Row[]; digits: DigitMode; timezone: string; exportable?: boolean }) {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('all')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [sort, setSort] = useState<{ key: SortKey; direction: 'asc' | 'desc' }>({ key: 'date', direction: 'desc' })
  const [page, setPage] = useState(0)
  const pageSize = 5
  const hasAmount = !['employees', 'users-roles', 'audit-logs'].includes(section)
  const filtered = useMemo(() => rows.filter((row) => {
    const matchesSearch = `${row.id} ${row.name} ${row.detail} ${row.status}`.toLocaleLowerCase('ar').includes(search.trim().toLocaleLowerCase('ar'))
    const matchesStatus = status === 'all' || row.status === status
    const matchesDate = (!from || row.date >= from) && (!to || row.date <= to)
    return matchesSearch && matchesStatus && matchesDate
  }).sort((first, second) => {
    const left = first[sort.key]
    const right = second[sort.key]
    const comparison = sort.key === 'name' ? String(left).localeCompare(String(right), 'ar') : sort.key === 'amount' ? first.amount - second.amount : first.date.localeCompare(second.date)
    return sort.direction === 'asc' ? comparison : -comparison
  }), [rows, search, status, from, to, sort])
  const pageCount = Math.ceil(filtered.length / pageSize)
  const visibleRows = filtered.slice(page * pageSize, page * pageSize + pageSize)
  const statuses = Array.from(new Set(rows.map((row) => row.status)))
  const detailsRoute = section === 'sessions' || section === 'shifts' || section === 'employees'
  const title = section === 'sessions' ? 'الجلسات' : section === 'shifts' ? 'الورديات' : section === 'employees' ? 'الموظفون' : section === 'audit-logs' ? 'سجل التدقيق' : section === 'users-roles' ? 'المستخدمون' : section === 'cash-reconciliation' ? 'مطابقات الخزينة' : section === 'sales' ? 'الفواتير' : section === 'reports' ? 'فواتير العينة' : section === 'inventory' ? 'حركات المخزون' : section === 'products' ? 'الكتالوج' : 'البيانات المعروضة'
  const amountLabel = section === 'products' ? 'سعر توضيحي (EGP)' : section === 'inventory' ? 'قيمة الحركة (EGP) توضيحية' : section === 'cash-reconciliation' ? 'الفرق (EGP)' : section === 'shifts' ? 'قيمة توضيحية' : 'الإجمالي التوضيحي'
  function toggleSort(key: SortKey) {
    setPage(0)
    setSort((current) => current.key === key ? { key, direction: current.direction === 'asc' ? 'desc' : 'asc' } : { key, direction: key === 'name' ? 'asc' : 'desc' })
  }
  function exportCsv() {
    const header = ['المعرّف', 'التاريخ', 'الاسم', 'التفاصيل', 'الحالة', ...(hasAmount ? [amountLabel] : [])]
    const records = filtered.map((row) => [row.id, row.date, row.name, formatDigits(row.detail, digits), row.status, ...(hasAmount ? [formatCurrency(row.amount, digits)] : [])])
    const csv = [header, ...records].map((record) => record.map((value) => `"${String(value).replaceAll('"', '""')}"`).join(',')).join('\r\n')
    const url = URL.createObjectURL(new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' }))
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = 'ghost-admin-demo-report.csv'
    document.body.appendChild(anchor)
    anchor.click()
    anchor.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  }
  return <Card className="admin-table-card">
    <CardHeader><div><CardTitle>{title}</CardTitle><CardDescription>استخدم البحث والفلاتر لترتيب مجموعة البيانات المعروضة.</CardDescription></div><SourceLabel source="demo" /></CardHeader>
    <CardContent>
      <div className="admin-table-tools">
        <label className="admin-search"><Search aria-hidden="true" /><span className="sr-only">ابحث في {title}</span><input value={search} onChange={(event) => { setSearch(event.target.value); setPage(0) }} placeholder="بحث بالاسم أو الرقم أو الحالة" /></label>
        <label className="admin-filter">الحالة<select value={status} onChange={(event) => { setStatus(event.target.value); setPage(0) }}><option value="all">الكل</option>{statuses.map((item) => <option value={item} key={item}>{item}</option>)}</select></label>
        <label className="admin-filter">من<input aria-label="من تاريخ" type="date" value={from} max={to || undefined} onChange={(event) => { setFrom(event.target.value); setPage(0) }} /></label>
        <label className="admin-filter">إلى<input aria-label="إلى تاريخ" type="date" value={to} min={from || undefined} onChange={(event) => { setTo(event.target.value); setPage(0) }} /></label>
        {exportable && <button className="admin-secondary-button" type="button" onClick={exportCsv}><Download aria-hidden="true" />تصدير CSV</button>}
      </div>
      <div className="admin-table-scroll"><table className="admin-data-table"><thead><tr>
        <th scope="col">الرقم</th>
        <th scope="col" aria-sort={sort.key === 'date' ? sort.direction === 'asc' ? 'ascending' : 'descending' : 'none'}><button className="admin-sort-button" type="button" onClick={() => toggleSort('date')} aria-label="ترتيب حسب التاريخ">التاريخ <ArrowUpDown aria-hidden="true" /></button></th>
        <th scope="col" aria-sort={sort.key === 'name' ? sort.direction === 'asc' ? 'ascending' : 'descending' : 'none'}><button className="admin-sort-button" type="button" onClick={() => toggleSort('name')} aria-label="ترتيب حسب الاسم">الاسم <ArrowUpDown aria-hidden="true" /></button></th>
        <th scope="col">التفاصيل</th><th scope="col">الحالة</th>
        {hasAmount && <th scope="col" aria-sort={sort.key === 'amount' ? sort.direction === 'asc' ? 'ascending' : 'descending' : 'none'}><button className="admin-sort-button" type="button" onClick={() => toggleSort('amount')} aria-label={`ترتيب حسب ${amountLabel}`}>{amountLabel} <ArrowUpDown aria-hidden="true" /></button></th>}
      </tr></thead><tbody>
        {visibleRows.map((row) => <tr key={row.id}>
          <td className="admin-mono">{formatDigits(row.id, digits)}</td><td className="admin-mono">{formatDate(row.date, timezone, digits)}</td>
          <td>{detailsRoute ? <Link className="admin-row-link" href={`/admin/${section}/${encodeURIComponent(row.id)}`}>{row.name}</Link> : <strong>{row.name}</strong>}</td>
          <td>{formatDigits(row.detail, digits)}{section === 'sessions' && row.durationMinutes ? ` · ${formatDigits(String(row.durationMinutes), digits)} دقيقة` : ''}</td>
          <td><Badge tone={statusTone(row.status)}>{row.status}</Badge></td>
          {hasAmount && <td className="admin-mono">{section === 'cash-reconciliation' ? formatDigits(String(row.amount), digits) : formatCurrency(row.amount, digits)}</td>}
        </tr>)}
      </tbody></table></div>
      {filtered.length === 0 ? <EmptyState description="لا توجد نتائج مطابقة للفلاتر الحالية." /> : <div className="admin-pagination"><span>عرض {formatDigits(String(page * pageSize + 1), digits)}–{formatDigits(String(Math.min((page + 1) * pageSize, filtered.length)), digits)} من {formatDigits(String(filtered.length), digits)}</span><button type="button" className="admin-secondary-button" disabled={page === 0} onClick={() => setPage((value) => Math.max(0, value - 1))}>السابق</button><button type="button" className="admin-secondary-button" disabled={page >= pageCount - 1} onClick={() => setPage((value) => Math.min(pageCount - 1, value + 1))}>التالي</button></div>}
    </CardContent>
  </Card>
}

function DemoDetail({ section, row, digits, timezone }: { section: string; row: Row; digits: DigitMode; timezone: string }) {
  return <div className="admin-page-content"><PageHeader title={titles[section] ?? 'التفاصيل'} description="تفاصيل توضيحية للقراءة فقط." />
    <Link className="admin-back-link" href={`/admin/${section}`}><ArrowLeft aria-hidden="true" />العودة إلى القائمة</Link>
    <Card className="admin-detail-card"><CardHeader><div><CardTitle>{row.name}</CardTitle><CardDescription>هذه السجلات تجريبية وليست بيانات تشغيلية.</CardDescription></div><SourceLabel source="demo" /></CardHeader>
      <CardContent><dl><div><dt>المعرّف</dt><dd className="admin-mono">{formatDigits(row.id, digits)}</dd></div><div><dt>التاريخ</dt><dd>{formatDate(row.date, timezone, digits)}</dd></div><div><dt>الحالة</dt><dd>{row.status}</dd></div><div><dt>التفاصيل</dt><dd>{formatDigits(row.detail, digits)}</dd></div>{section === 'sessions' && row.durationMinutes && <div><dt>المدة التوضيحية</dt><dd>{formatDigits(String(row.durationMinutes), digits)} دقيقة</dd></div>}</dl>
        {section !== 'employees' && <p className="admin-detail-total">القيمة التوضيحية: <strong>{formatCurrency(row.amount, digits)}</strong></p>}
      </CardContent>
    </Card>
  </div>
}

function SettingsPage({ digits, timezone, demoMode, dark, onThemeChanged, onPreferencesSaved }: Pick<SectionProps, 'digits' | 'timezone' | 'demoMode' | 'dark' | 'onThemeChanged' | 'onPreferencesSaved'>) {
  const [nextDigits, setNextDigits] = useState<DigitMode>(digits)
  const [nextTimezone, setNextTimezone] = useState(timezone)
  const [nextDemoMode, setNextDemoMode] = useState(demoMode)
  const [saved, setSaved] = useState(false)
  function savePreferences() {
    const preferences = { digits: nextDigits, timezone: nextTimezone, demoMode: nextDemoMode }
    window.localStorage.setItem('ghost-admin-digits', nextDigits)
    window.localStorage.setItem('ghost-admin-timezone', nextTimezone)
    window.localStorage.setItem('ghost-admin-demo', String(nextDemoMode))
    onPreferencesSaved(preferences)
    setSaved(true)
  }
  return <div className="admin-page-content"><PageHeader title="الإعدادات" description={descriptions.settings} />
    <Notice type="info">هذه التفضيلات محفوظة محليًا في هذا المتصفح، ولا تغيّر إعدادات الخادم أو صلاحياته.</Notice>
    <Card className="admin-settings-card"><CardHeader><div><CardTitle>تفضيلات العرض</CardTitle><CardDescription>تنطبق على واجهة الإدارة الحالية فقط.</CardDescription></div><SettingsIcon /></CardHeader>
      <CardContent><div className="admin-settings-fields">
        <label className="admin-setting-toggle"><span><strong>المظهر الداكن</strong><small>تبديل المظهر محليًا دون تغيير تفضيلات Staff.</small></span><input type="checkbox" checked={dark} onChange={(event) => { onThemeChanged(event.target.checked); window.localStorage.setItem('ghost-admin-theme', event.target.checked ? 'dark' : 'light') }} /></label>
        <label><span>عرض الأرقام</span><select value={nextDigits} onChange={(event) => { setNextDigits(event.target.value as DigitMode); setSaved(false) }}><option value="western">Western · 123</option><option value="arabic">العربية · ١٢٣</option></select></label>
        <label><span>المنطقة الزمنية</span><select value={nextTimezone} onChange={(event) => { setNextTimezone(event.target.value); setSaved(false) }}><option value="Africa/Cairo">Africa/Cairo</option><option value="UTC">UTC</option></select></label>
        <label className="admin-setting-toggle"><span><strong>وضع Demo</strong><small>يعرض بيانات تجريبية معلّمة بوضوح. يبدأ متوقفًا ولا يغيّر بيانات API الحية.</small></span><input type="checkbox" checked={nextDemoMode} onChange={(event) => { setNextDemoMode(event.target.checked); setSaved(false) }} /></label>
      </div>
      <div className="admin-settings-actions"><button className="admin-primary-button" type="button" onClick={savePreferences}>حفظ التفضيلات</button>{saved && <span role="status">تم حفظ التفضيلات محليًا.</span>}</div></CardContent>
    </Card>
  </div>
}

function SettingsIcon() {
  return <Settings aria-hidden="true" />
}

function AnalyticsPage({ digits, timezone, demoMode }: Pick<SectionProps, 'digits' | 'timezone' | 'demoMode'>) {
  const source: Source = demoMode ? 'demo' : 'unavailable'
  return <div className="admin-page-content"><PageHeader title={titles.analytics} description={descriptions.analytics} />
    {!demoMode ? <UnavailableState description="لا توجد واجهات تحليلات فعلية بعد. فعّل وضع Demo من الإعدادات لرؤية رسوم توضيحية." /> : <>
      <div className="admin-stat-grid admin-stat-grid--three">
        <StatCard label="إجمالي إيراد أسبوعي" value={formatCurrency(demoAnalytics.reduce((sum, day) => sum + day.revenue, 0), digits)} icon={Wallet} source={source} digits={digits} />
        <StatCard label="ساعات استخدام توضيحية" value={formatDigits(String(demoAnalytics.reduce((sum, day) => sum + day.hours, 0)), digits)} icon={Clock3} source={source} digits={digits} />
        <StatCard label="متوسط الجلسات اليومي" value={formatDigits(String(Math.round(demoAnalytics.reduce((sum, day) => sum + day.sessions, 0) / demoAnalytics.length)), digits)} icon={Activity} source={source} digits={digits} />
      </div>
      <div className="admin-overview-grid">
        <PanelCard title="الإيرادات حسب اليوم" description="EGP · قيم توضيحية." source={source}><div className="admin-chart-wrap"><ResponsiveContainer width="100%" height="100%"><AreaChart data={demoAnalytics}><CartesianGrid strokeDasharray="3 5" vertical={false} stroke="var(--admin-line)" /><XAxis dataKey="day" tickLine={false} axisLine={false} tick={{ fill: 'var(--admin-muted)', fontSize: 10 }} /><YAxis width={64} tickLine={false} axisLine={false} tick={{ fill: 'var(--admin-muted)', fontSize: 9 }} tickFormatter={(value) => formatCurrency(Number(value), digits)} /><Tooltip formatter={(value) => formatCurrency(Number(value), digits)} /><Area dataKey="revenue" name="الإيراد التوضيحي" type="monotone" stroke="#147653" fill="#14765322" /></AreaChart></ResponsiveContainer></div></PanelCard>
        <PanelCard title="الإشغال التوضيحي" description="نسبة استخدام تقديرية للعرض فقط." source={source}><div className="admin-chart-wrap"><ResponsiveContainer width="100%" height="100%"><BarChart data={demoAnalytics}><CartesianGrid strokeDasharray="3 5" vertical={false} stroke="var(--admin-line)" /><XAxis dataKey="day" tickLine={false} axisLine={false} tick={{ fill: 'var(--admin-muted)', fontSize: 10 }} /><YAxis width={40} unit="%" tickLine={false} axisLine={false} tick={{ fill: 'var(--admin-muted)', fontSize: 9 }} tickFormatter={(value) => formatDigits(String(value), digits)} /><Tooltip formatter={(value) => `${formatDigits(String(value), digits)}%`} /><Bar dataKey="utilization" name="الإشغال التوضيحي" fill="#9b7437" radius={[4, 4, 0, 0]} /></BarChart></ResponsiveContainer></div></PanelCard>
      </div>
      <PanelCard title="مقارنة أداء الجلسات" description="المدة والحالة من سجلات الجلسات التوضيحية." source={source}><DataTable section="sessions" rows={demoSessions} digits={digits} timezone={timezone} /></PanelCard>
    </>}
  </div>
}

function InventoryPage({ digits, timezone }: Pick<SectionProps, 'digits' | 'timezone'>) {
  const lowStock = demoProducts.filter((product) => product.status === 'منخفض').length
  return <div className="admin-page-content">
    <PageHeader title={titles.inventory} description={descriptions.inventory} />
    <div className="admin-stat-grid admin-stat-grid--three">
      <StatCard label="أصناف توضيحية" value={formatDigits(String(demoProducts.length), digits)} icon={Package} source="demo" digits={digits} />
      <StatCard label="أصناف منخفضة توضيحيًا" value={formatDigits(String(lowStock), digits)} icon={AlertCircle} source="demo" digits={digits} />
      <StatCard label="حركات توضيحية" value={formatDigits(String(demoInventory.length), digits)} icon={Activity} source="demo" digits={digits} />
    </div>
    <PanelCard title="مستويات المخزون" description={`المقياس يعرض الكمية التوضيحية من أصل ${formatDigits('60', digits)} قطعة.`} source="demo">
      <div className="admin-stock-list">{demoProducts.map((product) => {
        const quantity = product.stockQuantity ?? 0
        return <div className="admin-stock-row" key={product.id}>
          <span><strong>{product.name}</strong><small>{product.status}</small></span>
          <strong>{formatDigits(String(quantity), digits)} قطعة</strong>
          <progress className={product.status === 'منخفض' ? 'is-low' : ''} max={60} value={quantity} aria-label={`مخزون ${product.name}`} />
        </div>
      })}</div>
    </PanelCard>
    <DataTable section="inventory" rows={demoInventory} digits={digits} timezone={timezone} />
  </div>
}

function ReportsPage({ digits, timezone }: Pick<SectionProps, 'digits' | 'timezone'>) {
  const total = demoSales.reduce((sum, invoice) => sum + invoice.amount, 0)
  const average = demoSales.length ? Math.round(total / demoSales.length) : 0
  return <div className="admin-page-content">
    <PageHeader title={titles.reports} description={descriptions.reports} />
    <Notice type="info"><span><strong>تقرير توضيحي</strong><small>الملخص والتصدير يستخدمان مجموعة الفواتير التجريبية نفسها، ولا يمثلان بيانات مبيعات فعلية.</small></span></Notice>
    <div className="admin-stat-grid admin-stat-grid--three">
      <StatCard label="فواتير في العينة" value={formatDigits(String(demoSales.length), digits)} icon={FileText} source="demo" digits={digits} />
      <StatCard label="إجمالي العينة" value={formatCurrency(total, digits)} icon={Wallet} source="demo" digits={digits} />
      <StatCard label="متوسط الفاتورة" value={formatCurrency(average, digits)} icon={Activity} source="demo" digits={digits} />
    </div>
    <DataTable section="reports" rows={demoSales} digits={digits} timezone={timezone} exportable />
  </div>
}

function DemoPage({ section, token, deviceId, recordId, digits, timezone, demoMode, dark, onThemeChanged, onPreferencesSaved }: SectionProps) {
  const meta = titles[section]
  if (!meta) return <div className="admin-page-content"><PageHeader title="الصفحة غير موجودة" description="هذا المسار غير متاح داخل لوحة الإدارة." /><Link href="/admin" className="admin-back-link"><ArrowLeft aria-hidden="true" />العودة إلى النظرة العامة</Link></div>
  if (section === 'devices') return <div className="admin-page-content"><PageHeader title={titles.devices} description={descriptions.devices} /><AdminDeviceManager token={token} deviceId={deviceId} /></div>
  if (section === 'settings') return <SettingsPage digits={digits} timezone={timezone} demoMode={demoMode} dark={dark} onThemeChanged={onThemeChanged} onPreferencesSaved={onPreferencesSaved} />
  if (!demoMode) return <div className="admin-page-content"><PageHeader title={meta} description={descriptions[section]} /><UnavailableState description="هذه الصفحة لا تملك واجهة قراءة فعلية بعد. شغّل وضع Demo من الإعدادات لمراجعة مثال توضيحي." /><Link href="/admin/settings" className="admin-back-link"><ArrowLeft aria-hidden="true" />فتح تفضيلات العرض</Link></div>
  const rows: Record<string, Row[]> = {
    sessions: demoSessions, products: demoProducts, inventory: demoInventory, sales: demoSales,
    employees: demoEmployees, shifts: demoShifts, 'cash-reconciliation': demoReconciliations,
    reports: demoSales, 'audit-logs': demoAudit, 'users-roles': demoUsers,
  }
  if (section === 'inventory') return <InventoryPage digits={digits} timezone={timezone} />
  if (section === 'reports') return <ReportsPage digits={digits} timezone={timezone} />
  const row = recordId ? rows[section]?.find((item) => item.id === recordId) : undefined
  if (recordId && ['sessions', 'shifts', 'employees'].includes(section)) return row ? <DemoDetail section={section} row={row} digits={digits} timezone={timezone} /> : <div className="admin-page-content"><PageHeader title="التفاصيل غير موجودة" description="لا يوجد سجل توضيحي بهذا المعرّف." /><Link href={`/admin/${section}`} className="admin-back-link"><ArrowLeft aria-hidden="true" />العودة إلى القائمة</Link></div>
  if (section === 'analytics') return <AnalyticsPage digits={digits} timezone={timezone} demoMode={demoMode} />
  if (section === 'cash-reconciliation') return <div className="admin-page-content"><PageHeader title={meta} description={descriptions[section]} /><div className="admin-stat-grid admin-stat-grid--three"><StatCard label="متوقع توضيحي" value={formatCurrency(8450, digits)} icon={Wallet} source="demo" digits={digits} /><StatCard label="معدود توضيحي" value={formatCurrency(8400, digits)} icon={Wallet} source="demo" digits={digits} /><StatCard label="فرق توضيحي" value={formatCurrency(-50, digits)} icon={Activity} source="demo" digits={digits} /></div><DataTable section={section} rows={rows[section] ?? []} digits={digits} timezone={timezone} /></div>
  if (section === 'users-roles') return <div className="admin-page-content"><PageHeader title={meta} description={descriptions[section]} /><DataTable section={section} rows={rows[section] ?? []} digits={digits} timezone={timezone} /><PanelCard title="مصفوفة الصلاحيات" description="عرض توضيحي للقراءة فقط؛ لا يغيّر أدوارًا أو صلاحيات." source="demo"><div className="admin-table-scroll"><table className="admin-data-table"><thead><tr><th scope="col">الصلاحية</th><th scope="col">مدير</th><th scope="col">مشرف</th><th scope="col">محلل</th></tr></thead><tbody><tr><td>الأجهزة</td><td>عرض</td><td>عرض</td><td>—</td></tr><tr><td>التقارير</td><td>عرض</td><td>عرض</td><td>عرض</td></tr><tr><td>إدارة المستخدمين</td><td>عرض</td><td>—</td><td>—</td></tr></tbody></table></div></PanelCard></div>
  return <div className="admin-page-content"><PageHeader title={meta} description={descriptions[section]} /><DataTable section={section} rows={rows[section] ?? []} digits={digits} timezone={timezone} exportable={section === 'reports'} /></div>
}

export function AdminSectionView(props: SectionProps) {
  const { section, token, ...rest } = props
  if (section === 'overview') return <><AdminOutageReview token={token} /><AdminIntakeReport token={token} /><AdminOverview token={token} digits={rest.digits} timezone={rest.timezone} demoMode={rest.demoMode} /></>
  return <DemoPage section={section} token={token} {...rest} />
}
