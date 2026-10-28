'use client'

import { useEffect, useMemo, useState } from 'react'
import { getDevices, type ApiDevice } from '@/lib/ghost-api'
import {
  ArrowLeftRight,
  Bell,
  Check,
  CheckCircle2,
  ChevronLeft,
  ClipboardCheck,
  Clock3,
  Gamepad2,
  History,
  CupSoda,
  Popcorn,
  Cookie,
  Utensils,
  Play,
  Plus,
  LayoutGrid,
  LogOut,
  Menu,
  MessageSquareText,
  Moon,
  MoreHorizontal,
  Search,
  Send,
  ShieldCheck,
  Sun,
  Users,
  WalletCards,
  X,
  Zap,
} from 'lucide-react'

type Device = { id: string; name: string; type: string; room: string; status: 'available' | 'playing' | 'maintenance'; duration?: string; game?: string; price?: string }

function mapApiDevice(device: ApiDevice): Device {
  const status = device.status.toLowerCase()
  const uiStatus: Device['status'] =
    status === 'available' ? 'available' :
    ['running', 'paused', 'reserved'].includes(status) ? 'playing' : 'maintenance'

  return {
    id: device.id,
    name: device.name,
    type: device.category,
    room: 'الأجهزة المسجلة',
    status: uiStatus,
  }
}

export function StaffDashboard() {
  const [dark, setDark] = useState(true)
  const [devices, setDevices] = useState<Device[]>([])
  const [devicesLoading, setDevicesLoading] = useState(true)
  const [devicesError, setDevicesError] = useState<string | null>(null)
  const [activeNav, setActiveNav] = useState('overview')
  const [selectedDevice, setSelectedDevice] = useState<Device | null>(null)
  const [mode, setMode] = useState<'single' | 'multi'>('single')
  const [mobileNav, setMobileNav] = useState(false)

  useEffect(() => {
    const saved = window.localStorage.getItem('ghost-theme')
    if (saved) setDark(saved === 'dark')
  }, [])

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    window.localStorage.setItem('ghost-theme', dark ? 'dark' : 'light')
  }, [dark])

  useEffect(() => {
    let cancelled = false

    getDevices()
      .then((items) => {
        if (cancelled) return
        setDevices(items.filter((item) => item.isActive).map(mapApiDevice))
        setDevicesError(null)
      })
      .catch((error: unknown) => {
        if (cancelled) return
        setDevicesError(error instanceof Error ? error.message : 'تعذر تحميل الأجهزة.')
      })
      .finally(() => {
        if (!cancelled) setDevicesLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  const grouped = useMemo(() => {
    return devices.reduce<Record<string, Device[]>>((acc, device) => {
      acc[device.room] = [...(acc[device.room] || []), device]
      return acc
    }, {})
  }, [devices])

  const navItems = [
    { id: 'overview', label: 'الرئيسية', en: 'Operations', icon: LayoutGrid },
    { id: 'devices', label: 'الأجهزة والجلسات', en: 'Devices & sessions', icon: Gamepad2 },
    { id: 'products', label: 'الطلبات والمنتجات', en: 'Orders & products', icon: Zap },
    { id: 'payments', label: 'الفواتير والمدفوعات', en: 'Invoices & payments', icon: WalletCards },
    { id: 'shift', label: 'شيفتي', en: 'My shift', icon: ClipboardCheck },
    { id: 'messages', label: 'الرسائل', en: 'Messages & handover', icon: MessageSquareText },
    { id: 'history', label: 'سجل نشاطي', en: 'My history', icon: History },
  ]

  const status = { available: { label: 'متاح', cls: 'available' }, playing: { label: 'قيد التشغيل', cls: 'playing' }, maintenance: { label: 'صيانة', cls: 'maintenance' } }

  return (
    <main className="app-shell" dir="rtl">
      <aside className={`sidebar ${mobileNav ? 'mobile-open' : ''}`}>
        <div className="brand-row">
          <div className="brand-mark">G</div>
          <div><strong>GHOST</strong><span>PLAYSTATION LOUNGE</span></div>
          <button className="icon-button mobile-close" aria-label="إغلاق القائمة" onClick={() => setMobileNav(false)}><X size={18} /></button>
        </div>
        <div className="venue-card"><span className="venue-dot" />فرع أبشواي <b>محلي</b><small>نقطة تشغيل GHOST</small></div>
        <nav className="nav-list" aria-label="تنقل الموظف">
          <p className="nav-label">مساحة العمل</p>
          {navItems.map((item) => {
            const Icon = item.icon
            return <button key={item.id} className={`nav-item ${activeNav === item.id ? 'active' : ''}`} onClick={() => { setActiveNav(item.id); setMobileNav(false) }}><Icon size={19} /><span>{item.label}<small>{item.en}</small></span>{item.count && <em>{item.count}</em>}</button>
          })}
        </nav>
        <div className="sidebar-bottom"><div className="user-card"><div className="avatar">G</div><div><strong>موظف GHOST</strong><small>حساب الكاشير</small></div><MoreHorizontal size={18} /></div><button className="logout"><LogOut size={17} /> تسجيل الخروج</button></div>
      </aside>

      <section className="content-area">
        <header className="topbar"><button className="icon-button menu-button" onClick={() => setMobileNav(true)} aria-label="فتح القائمة"><Menu size={21} /></button><div className="breadcrumbs"><span>مساحة الموظفين</span><ChevronLeft size={14} /><strong>{navItems.find(n => n.id === activeNav)?.label}</strong></div><div className="top-actions"><div className="sync-state"><span /> متصل الآن</div><button className="icon-button notification" aria-label="التنبيهات"><Bell size={19} /></button><button className="theme-toggle" onClick={() => setDark(!dark)} aria-label="تبديل المظهر">{dark ? <Sun size={18} /> : <Moon size={18} />}<span>{dark ? 'فاتح' : 'داكن'}</span></button></div></header>

        {activeNav === 'overview' && <>
          <div className="page-heading"><div><p className="eyebrow">لوحة التشغيل اليومية</p><h1>أهلاً بك في GHOST <span>—</span></h1><p className="subheading">هذه نظرة سريعة على ما يحتاج انتباهك خلال ورديتك.</p></div><div className="shift-pill"><span className="live-pulse" /><div><b>شيفتك الحالية</b><strong>بيانات الشيفت بعد الربط</strong></div><ArrowLeftRight size={17} /></div></div>
          <div className="notice-bar"><div className="notice-icon"><MessageSquareText size={17} /></div><div><strong>مركز التسليم</strong><p>ستظهر ملاحظات الشيفتات هنا بعد ربط خدمة الرسائل المحفوظة.</p></div><button onClick={() => setActiveNav('messages')}>فتح الرسائل <ChevronLeft size={16} /></button></div>
          <div className="section-header"><div><h2>الأجهزة والجلسات</h2><p>إدارة سريعة لحالة الأجهزة في مساحتك</p></div><div className="legend"><span><i className="dot green" /> متاح <b>{devices.filter((d) => d.status === 'available').length}</b></span><span><i className="dot amber" /> قيد التشغيل <b>{devices.filter((d) => d.status === 'playing').length}</b></span><span><i className="dot red" /> صيانة <b>{devices.filter((d) => d.status === 'maintenance').length}</b></span></div></div>
          {devicesLoading && <div className="notice-bar"><div className="notice-icon"><Gamepad2 size={17} /></div><div><strong>جاري تحميل الأجهزة</strong><p>يتم جلب البيانات من GHOST API.</p></div></div>}{devicesError && <div className="notice-bar"><div className="notice-icon"><ShieldCheck size={17} /></div><div><strong>تعذر تحميل الأجهزة</strong><p>{devicesError} تأكد أن الـ API يعمل وأن عنوانه مضبوط في إعدادات الواجهة.</p></div></div>}{!devicesLoading && !devicesError && devices.length === 0 && <div className="notice-bar"><div className="notice-icon"><Gamepad2 size={17} /></div><div><strong>لا توجد أجهزة مسجلة</strong><p>أضف الأجهزة من النظام ثم أعد تحميل الصفحة.</p></div></div>}<div className="device-groups">{Object.entries(grouped).map(([room, roomDevices]) => <section className="room-section" key={room}><div className="room-title"><div><h3>{room}</h3><span>{roomDevices.length} أجهزة · <b>{roomDevices.filter(d => d.status === 'available').length} متاحة الآن</b></span></div><button className="text-button">عرض الكل <ChevronLeft size={15} /></button></div><div className="device-grid">{roomDevices.map((device) => <button className={`device-card ${device.status}`} key={device.id} onClick={() => device.status === 'available' && setSelectedDevice(device)} disabled={device.status === 'maintenance'}><div className="device-card-top"><span className="device-id">{device.id}</span><span className={`status-label ${status[device.status].cls}`}><i />{status[device.status].label}</span></div><div className="device-icon"><Gamepad2 size={23} /></div><h4>{device.name}</h4><p>{device.type}</p>{device.status === 'playing' && <div className="session-info"><span><Clock3 size={14} /> قيد التشغيل</span><small>تفاصيل الجلسة بعد ربط API الجلسات</small></div>}{device.status === 'available' && <span className="start-hint">عرض إعدادات الجلسة <Zap size={14} /></span>}{device.status === 'maintenance' && <span className="maintenance-note">غير متاح مؤقتاً</span>}</button>)}</div></section>)}</div>
          <div className="bottom-grid"><section className="panel activity-panel"><div className="panel-heading"><div><h2>آخر النشاطات</h2><p>أحدث العمليات في شيفتك</p></div><button className="text-button" onClick={() => setActiveNav('history')}>السجل الكامل <ChevronLeft size={15} /></button></div><div className="timeline"><p className="subheading">سيظهر سجل النشاط هنا بعد إضافة واجهة سجل العمليات إلى الـ API.</p></div></section><section className="panel handover-panel"><div className="panel-heading"><div><h2>تسليم سريع</h2><p>اترك ملاحظة للموظف التالي</p></div><MessageSquareText size={19} className="panel-icon" /></div><div className="message-preview"><div className="avatar small">G</div><div><strong>رسائل التسليم</strong><p>سيظهر آخر تسليم محفوظ بعد ربط خدمة الرسائل.</p></div></div><button className="handover-action" onClick={() => setActiveNav('messages')}>فتح مركز التسليم <ChevronLeft size={16} /></button></section></div>
        </>}

        {activeNav === 'devices' && <DevicesView devices={devices} onBack={() => setActiveNav('overview')} onSelect={(device) => setSelectedDevice(device)} />}
        {activeNav === 'products' && <ProductsView />}
        {activeNav === 'payments' && <PaymentsView />}
        {activeNav === 'shift' && <ShiftView onBack={() => setActiveNav('overview')} />}
        {activeNav === 'messages' && <MessagesView />}
        {activeNav === 'history' && <HistoryView />}
      </section>

      {selectedDevice && <div className="modal-backdrop" onClick={() => setSelectedDevice(null)}><div className="session-modal" onClick={(e) => e.stopPropagation()}><div className="modal-header"><div><span className="eyebrow session-step">الخطوة 1 من 2 · إعداد الجلسة</span><h2>بدء جلسة جديدة</h2><p><strong>{selectedDevice.name}</strong> · {selectedDevice.room}</p></div><button className="icon-button" onClick={() => setSelectedDevice(null)} aria-label="إغلاق"><X size={18} /></button></div><div className="mode-select"><label>نوع الجلسة</label><div className="mode-buttons"><button className={mode === 'single' ? 'selected' : ''} onClick={() => setMode('single')}><Gamepad2 size={18} /><strong>فردية</strong><span>Single player</span></button><button className={mode === 'multi' ? 'selected' : ''} onClick={() => setMode('multi')}><Users size={18} /><strong>متعددة</strong><span>Multiplayer</span></button></div></div><div className="form-grid"><label>معرّف العميل<input placeholder="مثال: C-10482" /></label><label>المدة<input placeholder="اختر من التسعيرة المعتمدة" /></label></div><div className="modal-note"><ShieldCheck size={16} /> السعر والمدة يحددهما إعداد التسعيرة المعتمد — لا يمكن تعديلهما من حساب الموظف.</div><button className="primary-button" disabled title="يجب ربط الشيفت المفتوح والتحقق من هوية الموظف أولاً">بدء الجلسة بعد ربط الشيفت <ChevronLeft size={16} /></button></div></div>}
    </main>
  )
}

function DevicesView({ devices, onBack, onSelect }: { devices: Device[]; onBack: () => void; onSelect: (device: Device) => void }) {
  return <div className="inner-page"><button className="back-link" onClick={onBack}><ChevronLeft size={16} /> العودة للرئيسية</button><div className="page-heading compact"><div><p className="eyebrow">التشغيل اليومي · الأجهزة المصرّح بها</p><h1>الأجهزة والجلسات</h1><p className="subheading">اختر جهازاً متاحاً لبدء جلسة وفق التسعيرة المعتمدة.</p></div><span className="secure-label"><ShieldCheck size={15} /> صلاحيات الموظف</span></div><div className="device-groups">{Object.entries(devices.reduce<Record<string, Device[]>>((a, d) => { a[d.room] = [...(a[d.room] || []), d]; return a }, {})).map(([room, items]) => <section className="room-section" key={room}><div className="room-title"><div><h3>{room}</h3><span>{items.length} أجهزة · <b>{items.filter((d) => d.status === 'available').length} متاحة</b></span></div></div><div className="device-grid">{items.map((device) => <button className={`device-card ${device.status}`} key={device.id} disabled={device.status !== 'available'} onClick={() => onSelect(device)}><div className="device-card-top"><span className="device-id">{device.id}</span><span className={`status-label ${device.status}`}><i />{statusLabel(device.status)}</span></div><div className="device-icon"><Gamepad2 size={23} /></div><h4>{device.name}</h4><p>{device.type}</p>{device.status === 'available' ? <span className="start-hint">بدء جلسة فردية أو متعددة <Zap size={14} /></span> : <span className="maintenance-note">{device.status === 'playing' ? 'جلسة قيد التشغيل' : 'غير متاح مؤقتاً'}</span>}</button>)}</div></section>)}</div></div>
}

function ProductsView() {
  return <div className="inner-page">
    <div className="page-heading compact">
      <div><p className="eyebrow">الطلبات والمنتجات</p><h1>الطلبات والمنتجات</h1><p className="subheading">إضافة المنتجات إلى فاتورة الجلسة ومتابعة المخزون.</p></div>
      <span className="secure-label"><ShieldCheck size={15} /> بيانات النظام فقط</span>
    </div>
    <div className="notice-bar"><div className="notice-icon"><CupSoda size={17} /></div><div><strong>كتالوج المنتجات غير متصل بعد</strong><p>لن نعرض منتجات أو أسعارًا تجريبية. يلزم إضافة API للمنتجات والمخزون قبل تفعيل البيع.</p></div></div>
  </div>
}

function PaymentsView() {
  return <div className="inner-page">
    <div className="page-heading compact">
      <div><p className="eyebrow">الفواتير والمدفوعات</p><h1>الفواتير والمدفوعات</h1><p className="subheading">عرض الفواتير الفعلية وتسجيل المدفوعات المرتبطة بالجلسات.</p></div>
      <span className="secure-label"><ShieldCheck size={15} /> بيانات النظام فقط</span>
    </div>
    <div className="notice-bar"><div className="notice-icon"><WalletCards size={17} /></div><div><strong>الفواتير غير متصلة بالواجهة بعد</strong><p>الـ API الحالي لا يوفر استعلامًا عن الفواتير والمدفوعات؛ لذلك لن نعرض أرقامًا تجريبية قد تُفهم على أنها حسابات حقيقية.</p></div></div>
  </div>
}

function statusLabel(status: Device['status']) { return status === 'available' ? 'متاح' : status === 'playing' ? 'قيد التشغيل' : 'صيانة' }

function ShiftView({ onBack }: { onBack: () => void }) {
  return <div className="inner-page">
    <button className="back-link" onClick={onBack}><ChevronLeft size={16} /> العودة للتشغيل</button>
    <div className="page-heading compact"><div><p className="eyebrow">الشيفتات والخزينة</p><h1>الشيفت الحالي</h1><p className="subheading">عرض الرصيد والحركات ومطابقة النقدية قبل إغلاق الشيفت.</p></div><span className="secure-label"><ShieldCheck size={15} /> بيانات النظام فقط</span></div>
    <div className="notice-bar"><div className="notice-icon"><ClipboardCheck size={17} /></div><div><strong>بيانات الشيفت الحالي تحتاج API للقراءة</strong><p>المتاح حاليًا هو فتح الشيفت وإغلاقه عبر API. نحتاج endpoints لاسترجاع الشيفت المفتوح وحركاته قبل إظهار أرقام مالية.</p></div></div>
  </div>
}

function MessagesView() {
  return <div className="inner-page">
    <div className="page-heading compact"><div><p className="eyebrow">التسليم والاستلام</p><h1>مركز الرسائل والتسليم</h1><p className="subheading">ملاحظات محفوظة على النظام بين الشيفتات.</p></div><span className="secure-label"><ShieldCheck size={15} /> بيانات دائمة</span></div>
    <div className="messages-layout"><section className="panel conversation"><div className="conversation-head"><div className="avatar small">G</div><div><strong>محادثات الشيفت</strong><p>لن تظهر رسائل إلا بعد ربط خدمة الرسائل.</p></div></div><div className="message-thread"><p className="subheading">لا توجد رسائل معروضة. لم يتم ربط API للرسائل بعد.</p></div><div className="compose"><input disabled placeholder="سيتم تفعيل الإرسال بعد ربط API الرسائل" aria-label="رسائل غير مفعلة بعد" /><button disabled aria-label="الإرسال غير متاح بعد"><Send size={17} /></button></div></section><section className="panel handover-info"><h2>حالة التسليم</h2><div className="privacy-note"><ShieldCheck size={16} /><p>لن نعرض رسائل وهمية أو ندّعي حفظ رسالة محليًا. يجب حفظ الرسائل على الخادم لتكون متاحة بعد إغلاق التطبيق.</p></div></section></div>
  </div>
}

function HistoryView() {
  return <div className="inner-page">
    <div className="page-heading compact"><div><p className="eyebrow">سجل النشاط</p><h1>سجل نشاطي</h1><p className="subheading">عمليات موثقة بمعرّف ووقت وحالة.</p></div><span className="secure-label"><ShieldCheck size={15} /> سجل دائم</span></div>
    <div className="notice-bar"><div className="notice-icon"><History size={17} /></div><div><strong>سجل النشاط غير متصل بعد</strong><p>يلزم endpoint للأحداث وسجل التدقيق قبل عرض أو تصدير أي عمليات.</p></div></div>
  </div>
}
