import { AdminRoutePage } from '@/components/admin-route-page'

type DeviceDetailPageProps = {
  params: Promise<{ deviceId: string }>
}

export default async function DeviceDetailPage({ params }: DeviceDetailPageProps) {
  const { deviceId } = await params
  return <AdminRoutePage section="devices" deviceId={deviceId} />
}
