import { AdminRoutePage } from '@/components/admin-route-page'

type ShiftDetailPageProps = {
  params: Promise<{ recordId: string }>
}

export default async function ShiftDetailPage({ params }: ShiftDetailPageProps) {
  const { recordId } = await params
  return <AdminRoutePage section="shifts" recordId={recordId} />
}
