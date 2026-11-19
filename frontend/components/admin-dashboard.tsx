'use client'

import { useState } from 'react'
import { AdminDeviceManager } from '@/components/admin-device-manager'
import { AdminOverview } from '@/components/admin-overview'
import { ThemeToggle } from '@/components/theme-toggle'
import type { AuthenticatedUser } from '@/lib/ghost-api'
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
  Package,
  Settings,
  ShieldCheck,
  Users,
  Wallet,
  X,
} from 'lucide-react'

type AdminSection = 'overview' | 'devices' | 'shifts' | 'staff' | 'inventory' | 'reports' | 'settings'

const navigation: { id: AdminSection; label: string; english: string; icon: typeof LayoutDashboard }[] = [
  { id: 'overview', label: 'الرئيسية', english: 'Overview', icon: LayoutDashboard },
  { id: 'devices', label: 'الأجهزة والتسعير', english: 'Devices & pricing', icon: Gamepad2 },
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
    description: 'متابعة أداء المكان من شاشة واحدة، مع الاعتماد على البيانات المحفوظة في النظام فقط.',
    icon: Activity,
    message: 'لن تظهر إحصائيات مالية أو نسب تشغيل حتى يتم توفير endpoints حقيقية وربطها بقاعدة البيانات.',
  },
  devices: {
    eyebrow: 'التشغيل · الأجهزة والتسعير',
    title: 'الأجهزة والتسعير',
    description: 'إدارة الأجهزة المسجلة وأسعار الساعة للفردية والمتعددة.',
    icon: Gamepad2,
    message: 'يتم تحميل الأجهزة وحفظ الأسعار مباشرة من GHOST API. حالة الجلسات والصيانة تحتاج endpoints مستقلة.',
  },
  shifts: {
    eyebrow: 'المالية · الشيفتات والخزينة',
    title: 'الشيفتات والخزينة',
    description: 'مراجعة الشيفتات ومطابقة النقدية وحركات التحصيل.',
    icon: Wallet,
    message: 'يلزم استعلام الشيفتات المفتوحة وحركات الخزينة والمدفوعات قبل عرض أرصدة أو اعتماد إغلاق.',
  },
  staff: {
    eyebrow: 'الإدارة · الموظفون والصلاحيات',
    title: 'الموظفون والصلاحيات',
    description: 'إدارة حسابات الموظفين وأدوارهم وحدود الوصول.',
    icon: Users,
    message: 'تسجيل الدخول متصل بالـ API. إدارة المستخدمين وتعديل الأدوار لم يتم توفير endpoints لها بعد.',
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
    description: 'إدارة التسعير العام وإعدادات التشغيل والنسخ الاحتياطي.',
    icon: Settings,
    message: 'إعدادات التسعير والنسخ الاحتياطي ستظهر هنا بعد إنشاء واجهات الإدارة الخاصة بها. النسخ الاحتياطي نفسه يجب تنفيذه في خدمة Windows.',
  },
}

export function AdminDashboard({
  user,
  onLogout,
}: {
  user: AuthenticatedUser
  onLogout: () => void
}) {
  const [activeNav, setActiveNav] = useState<AdminSection>('overview')
  const [mobileNav, setMobileNav] = useState(false)
  const current = sectionDetails[activeNav]
  const CurrentIcon = current.icon
  const initial = user.fullName.trim().charAt(0) || 'G'
  const roleLabel = user.roles.includes('Admin') ? 'مدير النظام' : user.roles.join('، ')

  return (
    <main className="app-shell" dir="rtl">
      <aside className={`sidebar ${mobileNav ? 'mobile-open' : ''}`}>
        <div className="brand-row">
          <div className="brand-mark">G</div>
          <div><strong>GHOST</strong><span>ADMINISTRATION</span></div>
          <button className="icon-button mobile-close" aria-label="إغلاق القائمة" onClick={() => setMobileNav(false)}><X size={18} /></button>
        </div>
        <div className="venue-card"><span className="venue-dot" /> GHOST · أبشواي <b>متصل</b><small>لوحة الإدارة الرئيسية</small></div>
        <nav className="nav-list" aria-label="تنقل الإدارة">
          <p className="nav-label">إدارة المكان</p>
          {navigation.map((item) => {
            const Icon = item.icon
            return <button key={item.id} className={`nav-item ${activeNav === item.id ? 'active' : ''}`} onClick={() => { setActiveNav(item.id); setMobileNav(false) }}><Icon size={19} /><span>{item.label}<small>{item.english}</small></span></button>
          })}
        </nav>
        <div className="sidebar-bottom">
          <div className="user-card"><div className="avatar">{initial}</div><div><strong>{user.fullName}</strong><small>{roleLabel}</small></div></div>
          <button className="logout" onClick={onLogout}><LogOut size={17} /> تسجيل الخروج</button>
        </div>
      </aside>

      <section className="content-area">
        <header className="topbar">
          <button className="icon-button menu-button" onClick={() => setMobileNav(true)} aria-label="فتح القائمة"><Menu size={21} /></button>
          <div className="breadcrumbs"><span>GHOST · أبشواي</span><ChevronLeft size={14} /><strong>{current.title}</strong></div>
          <div className="top-actions">
            <div className="sync-state"><span /> متصل بحساب الإدارة</div>
            <button className="icon-button notification" aria-label="التنبيهات" disabled title="التنبيهات ستتاح بعد ربط مركز الإشعارات"><Bell size={19} /></button>
            <ThemeToggle />
          </div>
        </header>

        {activeNav !== 'overview' && (
          <div className="page-heading">
            <div><p className="eyebrow">{current.eyebrow}</p><h1>{current.title} <span>—</span></h1><p className="subheading">{current.description}</p></div>
            <div className="shift-pill"><ShieldCheck size={18} /><div><b>حالة القسم</b><strong>{activeNav === 'devices' ? 'متصل بالـ API' : 'قيد الربط'}</strong></div></div>
          </div>
        )}

        {activeNav !== 'overview' && (
          <div className="notice-bar">
            <div className="notice-icon"><CurrentIcon size={17} /></div>
            <div><strong>حالة تكامل القسم</strong><p>{current.message}</p></div>
          </div>
        )}

        {activeNav === 'overview' ? (
          <AdminOverview />
        ) : activeNav === 'devices' ? (
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
                <div className="panel-heading"><div><h2>حالة الأمان</h2><p>حالة صلاحيات واجهات الإدارة</p></div><ShieldCheck size={19} /></div>
                <div className="warning-box"><ShieldCheck size={17} /><span><strong>التحقق يتم داخل الـ API</strong><small>الواجهة ترسل access token قصير العمر، والـ API هو المرجع النهائي للصلاحيات. لا توجد صلاحية تشغيلية لمجرد إظهار زر في الواجهة.</small></span></div>
              </section>
            </div>
          </>
        )}
      </section>
    </main>
  )
}
