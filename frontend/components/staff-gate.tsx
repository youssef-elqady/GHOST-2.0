'use client'

import { AdminLogin } from '@/components/admin-login'
import { StaffDashboard } from '@/components/staff-dashboard'
import { useAdminAuth } from '@/lib/admin-auth'

/** The operations screen needs a signed-in Staff or Admin user. The token stays in memory; reloads restore it from the refresh cookie. */
export function StaffGate() {
  const { token, user, restoring, logout } = useAdminAuth()
  if (restoring) return <main className="admin-boot" aria-live="polite" dir="rtl">جارٍ التحقق من الجلسة…</main>
  if (!token || !user) return <AdminLogin redirectTo="/" audience="staff" />
  return <StaffDashboard token={token} userId={user.id} userName={user.fullName} onLogout={() => void logout()} />
}
