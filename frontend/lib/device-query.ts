import { getDevices, type ApiDevice } from '@/lib/ghost-api'

type DeviceQueryResult =
  | { status: 'ready'; source: 'live'; devices: ApiDevice[] }
  | { status: 'error'; source: 'unavailable'; message: string }

const requests = new Map<string, Promise<DeviceQueryResult>>()

export function getDeviceQuery(token: string, revision: number): Promise<DeviceQueryResult> {
  const key = JSON.stringify([token, revision])
  const cached = requests.get(key)
  if (cached) return cached

  const request = getDevices(token).then(
    (devices): DeviceQueryResult => ({ status: 'ready', source: 'live', devices }),
    (reason: unknown): DeviceQueryResult => ({ status: 'error', source: 'unavailable', message: reason instanceof Error ? reason.message : 'تعذر تحميل الأجهزة.' }),
  )
  requests.set(key, request)
  return request
}

export function clearDeviceQueryCache() {
  requests.clear()
}
