export type ApiDeviceRate = {
  id: string
  playType: 'Single' | 'Multi' | string
  hourlyRate: number
}

export type ApiDevice = {
  id: string
  name: string
  category: string
  status: string
  isActive: boolean
  rates: ApiDeviceRate[]
}

const API_BASE_URL = (
  process.env.NEXT_PUBLIC_GHOST_API_BASE_URL ?? 'http://localhost:5153'
).replace(/\/$/, '')

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response

  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...init?.headers,
      },
      cache: 'no-store',
    })
  } catch {
    throw new Error('لا يمكن الوصول إلى GHOST API. تأكد من تشغيل الخدمة.')
  }

  if (!response.ok) {
    const payload = await response.json().catch(() => null) as { error?: string; title?: string } | null
    throw new Error(payload?.error ?? payload?.title ?? `فشل الطلب (HTTP ${response.status}).`)
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export function getDevices(): Promise<ApiDevice[]> {
  return request<ApiDevice[]>('/api/devices')
}
