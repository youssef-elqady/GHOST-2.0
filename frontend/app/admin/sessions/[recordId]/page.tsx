import { AdminRoutePage } from '@/components/admin-route-page'

type SessionDetailPageProps = {
  params: Promise<{ recordId: string }>
}

export default async function SessionDetailPage({ params }: SessionDetailPageProps) {
  const { recordId } = await params
  return <AdminRoutePage section="sessions" recordId={recordId} />
}
