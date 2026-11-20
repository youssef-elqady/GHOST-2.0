import type { ReactNode } from 'react'
import { AdminAuthProvider } from '@/lib/admin-auth'

export const metadata = { title: 'إدارة GHOST', description: 'لوحة إدارة وتشغيل GHOST.' }
export default function AdminLayout({ children }: { children: ReactNode }) {
  return <AdminAuthProvider>{children}</AdminAuthProvider>
}
