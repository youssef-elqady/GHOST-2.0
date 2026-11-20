export type ApiDeviceRate = { id: string; playType: 'Single' | 'Multi' | string; hourlyRate: number }
export type ApiDevice = { id: string; name: string; category: string; status: string; isActive: boolean; rates: ApiDeviceRate[] }
export type RateInput = { playType: 'Single' | 'Multi'; hourlyRate: number }
export type AuthenticatedUser = { id: string; username: string; fullName: string; roles: string[] }
export type AuthResponse = { status: 'authenticated'; tokenType: 'Bearer'; accessToken: string; expiresAtUtc: string; user: AuthenticatedUser }
export type PasswordChangeRequired = { status: 'password_change_required'; tokenType: 'Bearer'; passwordChangeToken: string; expiresAtUtc: string }
const API_BASE_URL = (process.env.NEXT_PUBLIC_GHOST_API_BASE_URL ?? 'http://localhost:5153').replace(/\/$/, '')

async function request<T>(path: string, init?: RequestInit, token?: string): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init?.headers },
      cache: 'no-store',
    })
  } catch {
    throw new Error('لا يمكن الوصول إلى GHOST API. تحقق من تشغيل الخدمة وإعداد عنوانها.')
  }
  if (!response.ok) {
    if (response.status === 401 && typeof window !== 'undefined') window.dispatchEvent(new Event('ghost-admin-session-expired'))
    const payload = await response.json().catch(() => null) as { error?: string; title?: string; codes?: string[] } | null
    const error = payload?.error ?? payload?.title
    const messages: Record<string, string> = {
      invalid_credentials: 'اسم المستخدم أو كلمة المرور غير صحيحة.', account_locked: 'تم إيقاف الحساب مؤقتًا بعد محاولات فاشلة.',
      authentication_unavailable: 'خدمة تسجيل الدخول غير متاحة حاليًا.', password_change_rejected: 'تعذر تغيير كلمة المرور. تحقق من المتطلبات وحاول مجددًا.',
      invalid_request: 'راجع الحقول المدخلة وحاول مرة أخرى.',
    }
    const passwordCodes = payload?.codes?.map((code) => ({ PasswordTooShort: 'كلمة المرور أقصر من الحد المطلوب.', PasswordRequiresDigit: 'أضف رقمًا واحدًا على الأقل.', PasswordRequiresLower: 'أضف حرفًا صغيرًا واحدًا على الأقل.', PasswordRequiresUpper: 'أضف حرفًا كبيرًا واحدًا على الأقل.', PasswordRequiresNonAlphanumeric: 'أضف رمزًا خاصًا واحدًا على الأقل.', PasswordMismatch: 'كلمة المرور الحالية غير صحيحة.' }[code])).filter(Boolean)
    throw new Error((error && messages[error]) || passwordCodes?.join(' ') || `تعذر إكمال الطلب (HTTP ${response.status}).`)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}
export function login(username: string, password: string) { return request<AuthResponse | PasswordChangeRequired>('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }) }
export function changePassword(token: string, currentPassword: string, newPassword: string) { return request<AuthResponse>('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }, token) }
export function getDevices(token?: string): Promise<ApiDevice[]> { return request('/api/devices', undefined, token) }
export function seedDefaultDevices(token?: string): Promise<{ added: number; message: string }> { return request('/api/devices/seed-defaults', { method: 'POST' }, token) }
export function createDevice(token: string, payload: { name: string; category: string; rates: RateInput[] }) { return request<{ deviceId: string }>('/api/devices', { method: 'POST', body: JSON.stringify(payload) }, token) }
export function updateDeviceRates(id: string, rates: RateInput[], token?: string): Promise<ApiDevice> { return request(`/api/devices/${encodeURIComponent(id)}/rates`, { method: 'PUT', body: JSON.stringify({ rates }) }, token) }
