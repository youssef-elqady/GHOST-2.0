'use client'

import { useEffect, useState } from 'react'
import { AdminDeviceManager } from '@/components/admin-device-manager'
import {
  Activity,
  Bell,
  ChartNoAxesCombined,
  ChevronLeft,
  ClipboardList,
  Gamepad2,
  LayoutDashboard,
  LogOut,
  Menu,
  Moon,
  Package,
  Settings,
  ShieldCheck,
  Sun,
  Users,
  Wallet,
  X,
} from 'lucide-react'

type AdminSection = 'overview' | 'devices' | 'shifts' | 'staff' | 'inventory' | 'reports' | 'settings'

const navigation: { id: AdminSection; label: string; english: string; icon: typeof LayoutDashboard }[] = [
  { id: 'overview', label: 'الرئيسية', english: 'Overview', icon: LayoutDashboard },
  { id: 'devices', label: 'الأجهزة والجلسات', english: 'Devices & sessions', icon: Gamepad2 },
  { id: 'shifts', label: 'الشيفتات والخزينة', english: 'Shifts & cash', icon: Wallet },
  { id: 'staff', label: 'الموظفون والصلاحيات', english: 'Staff & permissions', icon: Users },
  { id: 'inventory', label: 'المبيعات والمخزون', english: 'Sales & inventory', icon: Package },
  { id: 'reports', label: 'التقارير والتحليلات', english: 'Reports & analytics', icon: ChartNoAxesCombined },
  { id: 'settings', label: 'إعدادات النظام', english: 'System settings', icon: Settings },
]

const sectionDetails: Record<AdminSection, { eyebrow: string; title: string; description: string; icon: typeof LayoutDashboard; message: string }> = {
  overview: {
    eyebrow: 'إدارة GHOST · نظرة عامة',
    title: 'لوحة تحكم الإدارة',
    description: 'متابعة التشغيل اليومي من مكان واحد، مع صلاحيات الإدارة الكاملة بعد استكمال الربط.',
    icon: Activity,
    message: 'لن تظهر إحصائيات مالية أو نسب تشغيل حتى يتم توفير endpoints حقيقية وربطها بقاعدة البيانات.',
  },
  devices: {
    eyebrow: 'التشغيل · الأجهزة والجلسات',
    title: 'الأجهزة والجلسات',
    description: 'متابعة حالة الأجهزة والجلسات النشطة والتحكم في العمليات المصرّح بها.',
    icon: Gamepad2,
    message: 'الـ API الحالي يوفر قائمة الأجهزة وإنشاء جهاز، لكنه لا يوفر بعد استعلام الجلسات النشطة أو تحديث حالة الصيانة من لوحة الإدارة.',
  },
  shifts: {
    eyebrow: 'المالية · الشيفتات والخزينة',
    title: 'الشيفتات والخزينة',
    description: 'مراجعة فتح وإغلاق الشيفتات ومطابقة النقدية وحركات التحصيل.',
    icon: Wallet,
    message: 'يلزم استعلام الشيفتات المفتوحة وحركات الخزينة والمدفوعات قبل عرض أرصدة أو اعتماد إغلاق.',
  },
  staff: {
    eyebrow: 'الإدارة · الموظفون والصلاحيات',
    title: 'الموظفون والصلاحيات',
    description: 'إدارة حسابات الموظفين وأدوارهم وحدود الوصول.',
    icon: Users,
    message: 'لم تتم إضافة نظام هوية وتسجيل دخول وصلاحيات بعد. هذه الصفحة ليست وسيلة حماية، ويجب تنفيذ التفويض داخل الـ API قبل الاستخدام الفعلي.',
  },
  inventory: {
    eyebrow: 'المبيعات · المنتجات والمخزون',
    title: 'المبيعات والمخزون',
    description: 'إدارة المنتجات والأسعار والكميات وحركات المخزون.',
    icon: Package,
    message: 'لا توجد endpoints للمنتجات والمخزون حتى الآن؛ لن نعرض كميات أو أسعارًا افتراضية.',
  },
  reports: {
    eyebrow: 'التحليلات · التقارير',
    title: 'التقارير والتحليلات',
    description: 'مقارنة الإيرادات والإشغال وحركة الشيفتات على أساس بيانات محفوظة.',
    icon: ChartNoAxesCombined,
    message: 'التقارير تحتاج استعلامات تجميعية موثوقة واختبارات مطابقة مع الفواتير؛ لم يتم ربطها بعد.',
  },
  settings: {
    eyebrow: 'النظام · الإعدادات',
    title: 'إعدادات النظام',
    description: 'إدارة التسعير وأسماء الفئات وإعدادات النسخ الاحتياطي.',
    icon: Settings,
    message: 'إعدادات التسعير والنسخ الاحتياطي ستظهر هنا بعد إنشاء واجهات الإدارة الخاصة بها. النسخ الاحتياطي نفسه يجب تنفيذه في خدمة Windows، وليس في المتصفح.',
  },
}

export function AdminDashboard() {
  const [dark, setDark] = useState(true)
  const [activeNav, setActiveNav] = useState<AdminSection>('overview')
  const [mobileNav, setMobileNav] = useState(false)
  const current = sectionDetails[activeNav]
  const CurrentIcon = current.icon

  useEffect(() => {
    const saved = window.localStorage.getItem('ghost-theme')
    if (saved) setDark(saved === 'dark')
  }, [])

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    window.localStorage.setItem('ghost-theme', dark ? 'dark' : 'light')
  }, [dark])

  return (
    <main className="app-shell" dir="rtl">
      <aside className={`sidebar ${mobileNav ? 'mobile-open' : ''}`}>
        <div className="brand-row">
          <div className="brand-mark">G</div>
          <div><strong>GHOST</strong><span>ADMINISTRATION</span></div>
          <button className="icon-button mobile-close" aria-label="إغلاق القائمة" onClick={() => setMobileNav(false)}><X size={18} /></button>
        </div>
        <div className="venue-card"><span className="venue-dot" />فرع أبشواي <b>محلي</b><small>لوحة الإدارة</small></div>
        <nav className="nav-list" aria-label="تنقل الإدارة">
          <p className="nav-label">إدارة المكان</p>
          {navigation.map((item) => {
            const Icon = item.icon
            return <button key={item.id} className={`nav-item ${activeNav === item.id ? 'active' : ''}`} onClick={() => { setActiveNav(item.id); setMobileNav(false) }}><Icon size={19} /><span>{item.label}<small>{item.english}</small></span></button>
          })}
        </nav>
        <div className="sidebar-bottom">
          <div className="user-card"><div className="avatar">A</div><div><strong>مساحة الإدارة</strong><small>حساب محلي · غير موثّق</small></div></div>
          <button className="logout" disabled><LogOut size={17} /> تسجيل الخروج بعد ربط الهوية</button>
        </div>
      </aside>

      <section className="content-area">
        <header className="topbar">
          <button className="icon-button menu-button" onClick={() => setMobileNav(true)} aria-label="فتح القائمة"><Menu size={21} /></button>
          <div className="breadcrumbs"><span>إدارة GHOST</span><ChevronLeft size={14} /><strong>{current.title}</strong></div>
          <div className="top-actions">
            <div className="sync-state"><span /> واجهة تطوير</div>
            <button className="icon-button notification" aria-label="التنبيهات"><Bell size={19} /></button>
            <button className="theme-toggle" onClick={() => setDark(!dark)} aria-label="تبديل المظهر">{dark ? <Sun size={18} /> : <Moon size={18} />}<span>{dark ? 'فاتح' : 'داكن'}</span></button>
          </div>
        </header>

        <div className="page-heading">
          <div><p className="eyebrow">{current.eyebrow}</p><h1>{current.title} <span>—</span></h1><p className="subheading">{current.description}</p></div>
          <div className="shift-pill"><ShieldCheck size={18} /><div><b>حالة الربط</b><strong>قيد التطوير</strong></div></div>
        </div>

        <div className="notice-bar">
          <div className="notice-icon"><CurrentIcon size={17} /></div>
          <div><strong>هذه مساحة الإدارة بنفس هوية GHOST</strong><p>{current.message}</p></div>
        </div>

        {activeNav === 'devices' ? (
          <AdminDeviceManager />
        ) : (
          <>
            <div className="section-header">
              <div><h2>متطلبات إكمال هذا القسم</h2><p>يتم تفعيل العمليات بعد تنفيذ endpoints واختبارها.</p></div>
            </div>
            <div className="bottom-grid">
              <section className="panel activity-panel">
                <div className="panel-heading"><div><h2>مصدر البيانات</h2><p>لا توجد بيانات تجريبية معروضة</p></div><ClipboardList size={19} /></div>
                <div className="timeline"><div className="timeline-item"><span className="timeline-dot gold" /><div><strong>Backend API</strong><p>قراءة البيانات والتحقق من الصلاحيات يجب أن تتم في الخادم.</p></div></div><div className="timeline-item"><span className="timeline-dot blue" /><div><strong>قاعدة البيانات</strong><p>الأرقام المالية يجب أن تطابق السجلات المحفوظة والفواتير.</p></div></div><div className="timeline-item"><span className="timeline-dot green" /><div><strong>اختبارات القبول</strong><p>لن تُعرض الحالة على أنها جاهزة قبل نجاح اختبارات الربط والاسترجاع.</p></div></div></div>
              </section>
              <section className="panel handover-panel">
                <div className="panel-heading"><div><h2>حالة الأمان</h2><p>قبل تشغيل لوحة الإدارة فعليًا</p></div><ShieldCheck size={19} /></div>
                <div className="warning-box"><ShieldCheck size={17} /><span><strong>تسجيل الدخول غير مفعّل</strong><small>المسار /admin ليس محميًا حاليًا. لا تستخدمه لتشغيل المكان قبل إضافة المصادقة والتفويض في الـ API.</small></span></div>
              </section>
            </div>
          </>
        )}
      </section>
    </main>
  )
}
