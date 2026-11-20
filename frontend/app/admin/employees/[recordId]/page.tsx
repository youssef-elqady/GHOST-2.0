import { AdminRoutePage } from '@/components/admin-route-page'

type EmployeeDetailPageProps = {
  params: Promise<{ recordId: string }>
}

export default async function EmployeeDetailPage({ params }: EmployeeDetailPageProps) {
  const { recordId } = await params
  return <AdminRoutePage section="employees" recordId={recordId} />
}
