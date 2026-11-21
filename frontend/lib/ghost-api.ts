export type ApiDeviceRate = { id: string; playType: 'Single' | 'Multi' | string; hourlyRate: number }
export type ApiDevice = { id: string; name: string; category: string; status: string; isActive: boolean; rates: ApiDeviceRate[] }
export type RateInput = { playType: 'Single' | 'Multi'; hourlyRate: number }
export type AuthenticatedUser = { id: string; username: string; fullName: string; roles: string[] }
export type AuthResponse = { status: 'authenticated'; tokenType: 'Bearer'; accessToken: string; expiresAtUtc: string; user: AuthenticatedUser }
export type PasswordChangeRequired = { status: 'password_change_required'; tokenType: 'Bearer'; passwordChangeToken: string; expiresAtUtc: string }
const API_BASE_URL = (process.env.NEXT_PUBLIC_GHOST_API_BASE_URL ?? 'http://localhost:5153').replace(/\/$/, '')

// --- Session plumbing: the provider registers these so every call can refresh once on 401. ---
let currentToken: string | null = null
let onTokenRefreshed: ((auth: AuthResponse) => void) | null = null
export function configureApiAuth(handlers: { token: string | null; onRefreshed?: (auth: AuthResponse) => void }) {
  currentToken = handlers.token
  if (handlers.onRefreshed) onTokenRefreshed = handlers.onRefreshed
}

const NO_REFRESH_PATHS = ['/api/auth/login', '/api/auth/change-password', '/api/auth/refresh', '/api/auth/logout']
let refreshing: Promise<AuthResponse | null> | null = null

/** One shared refresh for parallel calls. Returns null when the refresh cookie is missing, expired or revoked. */
export function refreshAccessToken(): Promise<AuthResponse | null> {
  refreshing ??= fetch(`${API_BASE_URL}/api/auth/refresh`, {
    method: 'POST', credentials: 'include', cache: 'no-store', headers: { Accept: 'application/json', 'X-GHOST-Request': '1' },
  }).then(async (r) => {
    if (!r.ok) return null
    const body = await r.json().catch(() => null) as AuthResponse | null
    return body?.status === 'authenticated' ? body : null
  }).catch(() => null).finally(() => { refreshing = null })
  return refreshing
}

export async function logoutRequest() {
  try { await fetch(`${API_BASE_URL}/api/auth/logout`, { method: 'POST', credentials: 'include', cache: 'no-store', headers: { 'X-GHOST-Request': '1' } }) } catch { /* offline: local session is cleared anyway */ }
}

async function send(path: string, init: RequestInit | undefined, token?: string): Promise<Response> {
  try {
    return await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      credentials: 'include',
      headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init?.headers },
      cache: 'no-store',
    })
  } catch {
    throw new Error('لا يمكن الوصول إلى GHOST API. تحقق من تشغيل الخدمة وإعداد عنوانها.')
  }
}

async function request<T>(path: string, init?: RequestInit, token?: string): Promise<T> {
  let response = await send(path, init, token ? (currentToken ?? token) : undefined)
  if (response.status === 401 && token && !NO_REFRESH_PATHS.includes(path)) {
    const next = await refreshAccessToken()
    if (next) {
      currentToken = next.accessToken
      onTokenRefreshed?.(next)
      response = await send(path, init, next.accessToken)
    }
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
