import { AdminShell } from '@/components/admin-shell'

type AdminRoutePageProps = {
  section: string
  deviceId?: string
  recordId?: string
}

export function AdminRoutePage({ section, deviceId, recordId }: AdminRoutePageProps) {
  return <AdminShell section={section} deviceId={deviceId} recordId={recordId} />
}
