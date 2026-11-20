'use client'

import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useEffect, useMemo, useState } from 'react'
import { Activity, BarChart3, Boxes, ChevronLeft, FileText, Gamepad2, LayoutDashboard, LogOut, Menu, Moon, Package, PanelRightClose, PanelRightOpen, Settings, ShieldCheck, Sun, Users, Wallet, X } from 'lucide-react'
import { useAdminAuth } from '@/lib/admin-auth'
import type { DigitMode } from '@/lib/admin-format'
import { AdminSectionView } from '@/components/admin-section-view'

const items = [
  { key: 'overview', label: 'نظرة عامة', icon: LayoutDashboard },
  { key: 'analytics', label: 'التحليلات', icon: BarChart3 },
  { key: 'sessions', label: 'الجلسات', icon: Activity },
  { key: 'devices', label: 'الأجهزة', icon: Gamepad2 },
  { key: 'products', label: 'المنتجات', icon: Package },
  { key: 'inventory', label: 'المخزون', icon: Boxes },
  { key: 'sales', label: 'المبيعات والفواتير', icon: Wallet },
  { key: 'employees', label: 'الموظفون', icon: Users },
  { key: 'shifts', label: 'الشيفتات', icon: Wallet },
  { key: 'cash-reconciliation', label: 'مطابقة الخزينة', icon: Wallet },
  { key: 'reports', label: 'التقارير', icon: FileText },
  { key: 'audit-logs', label: 'سجل التدقيق', icon: ShieldCheck },
  { key: 'users-roles', label: 'المستخدمون والصلاحيات', icon: Users },
  { key: 'settings', label: 'الإعدادات', icon: Settings },
]
const titles: Record<string, string> = { overview: 'لوحة الإدارة', analytics: 'التحليلات', sessions: 'الجلسات', devices: 'الأجهزة', products: 'المنتجات', inventory: 'المخزون', sales: 'المبيعات والفواتير', employees: 'الموظفون', shifts: 'الشيفتات', 'cash-reconciliation': 'مطابقة الخزينة', reports: 'التقارير', 'audit-logs': 'سجل التدقيق', 'users-roles': 'المستخدمون والصلاحيات', settings: 'الإعدادات' }

type AdminPreferences = { dark: boolean; digits: DigitMode; timezone: string; demoMode: boolean }

function readAdminPreferences(): AdminPreferences {
  const defaults: AdminPreferences = { dark: false, digits: 'western', timezone: 'Africa/Cairo', demoMode: false }
  if (typeof window === 'undefined') return defaults
  try {
    const theme = window.localStorage.getItem('ghost-admin-theme')
    const digits = window.localStorage.getItem('ghost-admin-digits')
    const timezone = window.localStorage.getItem('ghost-admin-timezone')
    return {
      dark: theme === 'dark' || (theme === null && window.matchMedia('(prefers-color-scheme: dark)').matches),
      digits: digits === 'arabic' || digits === 'western' ? digits : defaults.digits,
      timezone: timezone === 'UTC' || timezone === 'Africa/Cairo' ? timezone : defaults.timezone,
      demoMode: window.localStorage.getItem('ghost-admin-demo') === 'true',
    }
  } catch {
    return defaults
  }
}

export function AdminShell({ section = 'overview', deviceId, recordId }: { section?: string; deviceId?: string; recordId?: string }) {
  const { token, user, clearSession } = useAdminAuth()
  const router = useRouter()
  const [menuOpen, setMenuOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const [initialPreferences] = useState(readAdminPreferences)
  const [dark, setDark] = useState(initialPreferences.dark)
  const [digits, setDigits] = useState<DigitMode>(initialPreferences.digits)
  const [timezone, setTimezone] = useState(initialPreferences.timezone)
  const [demoMode, setDemoMode] = useState(initialPreferences.demoMode)
  const title = useMemo(() => titles[section] ?? 'لوحة الإدارة', [section])
  useEffect(() => { if (!token) router.replace('/admin/login') }, [token, router])
  useEffect(() => {
    document.documentElement.classList.toggle('admin-theme-dark', dark)
    return () => document.documentElement.classList.remove('admin-theme-dark')
  }, [dark])
  if (!token) return <main className="admin-boot" aria-live="polite">جارٍ التحقق من جلسة الإدارة…</main>
  const changeTheme = (nextDark: boolean) => {
    window.localStorage.setItem('ghost-admin-theme', nextDark ? 'dark' : 'light')
    setDark(nextDark)
  }
  const logout = () => { clearSession(); router.replace('/admin/login') }
  return <main className={`admin-v2 ${collapsed ? 'is-collapsed' : ''}`} dir="rtl">
    {menuOpen && <button className="admin-scrim" aria-label="إغلاق القائمة" onClick={() => setMenuOpen(false)} />}
    <aside className={`admin-sidebar ${menuOpen ? 'is-open' : ''} ${collapsed ? 'is-collapsed' : ''}`}>
      <div className="admin-brand"><span className="admin-brand-mark">G</span><span><strong>GHOST</strong><small>مركز الإدارة</small></span><button className="admin-icon-button admin-mobile-close" aria-label="إغلاق القائمة" onClick={() => setMenuOpen(false)}><X /></button></div>
      <div className="admin-branch"><span className="admin-branch-icon"><ShieldCheck /></span><span>إدارة المكان</span><small>موقع واحد</small></div>
      <p className="admin-nav-caption">إدارة المكان</p>
      <nav className="admin-nav" aria-label="تنقل الإدارة">
        {items.map(({ key, label, icon: Icon }) => <Link key={key} href={key === 'overview' ? '/admin' : `/admin/${key}`} aria-label={collapsed ? label : undefined} title={collapsed ? label : undefined} aria-current={section === key ? 'page' : undefined} className={`admin-nav-link ${section === key ? 'is-active' : ''}`} onClick={() => setMenuOpen(false)}><Icon aria-hidden="true" /><span>{label}</span></Link>)}
      </nav>
      <div className="admin-sidebar-bottom"><div className="admin-account"><span className="admin-avatar">{user?.fullName?.slice(0, 1) || 'م'}</span><span><strong>{user?.fullName || user?.username}</strong><small>{user?.roles?.join('، ') || 'موظف'}</small></span></div><button className="admin-logout" aria-label="تسجيل الخروج" onClick={logout}><LogOut /><span>تسجيل الخروج</span></button></div>
    </aside>
    <section className="admin-main-column">
      <header className="admin-topbar"><button className="admin-icon-button admin-menu-open" aria-label="فتح القائمة" onClick={() => setMenuOpen(true)}><Menu /></button><button className="admin-icon-button admin-sidebar-collapse" aria-label={collapsed ? 'توسيع القائمة الجانبية' : 'طي القائمة الجانبية'} aria-expanded={!collapsed} onClick={() => setCollapsed((value) => !value)}>{collapsed ? <PanelRightOpen /> : <PanelRightClose />}</button><div className="admin-crumb"><span>GHOST</span><ChevronLeft /><strong>{title}</strong></div><div className="admin-top-actions"><span className="admin-api-state">مساحة الإدارة</span><button className="admin-icon-button" onClick={() => changeTheme(!dark)} aria-label={dark ? 'تفعيل الوضع الفاتح' : 'تفعيل الوضع الداكن'}>{dark ? <Sun /> : <Moon />}</button></div></header>
      <AdminSectionView section={section} token={token} deviceId={deviceId} recordId={recordId} digits={digits} timezone={timezone} demoMode={demoMode} dark={dark} onThemeChanged={changeTheme} onPreferencesSaved={(preferences) => { setDigits(preferences.digits); setTimezone(preferences.timezone); setDemoMode(preferences.demoMode) }} />
      <footer className="admin-footer"><ShieldCheck />بيانات الإدارة تُعرض من مصادرها فقط. العناصر التوضيحية مميزة بوضوح.</footer>
    </section>
  </main>
}
