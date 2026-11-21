import { StaffGate } from '@/components/staff-gate'
import { AdminAuthProvider } from '@/lib/admin-auth'

export default function Page() {
  return <AdminAuthProvider><StaffGate /></AdminAuthProvider>
}
