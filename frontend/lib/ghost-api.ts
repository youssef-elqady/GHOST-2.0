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

export type RateInput = {
  playType: 'Single' | 'Multi'
  hourlyRate: number
}

export type AuthenticatedUser = {
  id: string
  username: string
  fullName: string
  roles: string[]
}

export type AuthenticatedResponse = {
  status: 'authenticated'
  tokenType: 'Bearer'
  accessToken: string
  expiresAtUtc: string
  user: AuthenticatedUser
}

export type PasswordChangeRequiredResponse = {
  status: 'password_change_required'
  tokenType: 'Bearer'
  passwordChangeToken: string
  expiresAtUtc: string
}

export type LoginResponse = AuthenticatedResponse | PasswordChangeRequiredResponse

const API_BASE_URL = (
  process.env.NEXT_PUBLIC_GHOST_API_BASE_URL ?? 'http://localhost:5153'
).replace(/\/$/, '')

const ACCESS_TOKEN_KEY = 'ghost-admin-access-token'
const ACCESS_EXPIRY_KEY = 'ghost-admin-access-expiry'
const USER_KEY = 'ghost-admin-user'

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message)
    this.name = 'ApiError'
  }
}

async function request<T>(
  path: string,
  init?: RequestInit,
  options: { accessToken?: string; authenticated?: boolean } = {},
): Promise<T> {
  let response: Response

  try {
    const token = options.accessToken ?? (
      options.authenticated === false
        ? null
        : typeof window !== 'undefined'
          ? window.sessionStorage.getItem(ACCESS_TOKEN_KEY)
          : null
    )

    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...init?.headers,
      },
      cache: 'no-store',
    })
  } catch {
    throw new Error('لا يمكن الوصول إلى GHOST API. تأكد من تشغيل الخدمة.')
  }

  if (!response.ok) {
    const payload = await response.json().catch(() => null) as {
      error?: string
      title?: string
    } | null

    if (response.status === 401 && typeof window !== 'undefined') {
      window.dispatchEvent(new Event('ghost:auth-expired'))
    }

    const messages: Record<string, string> = {
      invalid_credentials: 'اسم المستخدم أو كلمة المرور غير صحيحة.',
      account_locked: 'الحساب موقوف مؤقتًا بسبب محاولات تسجيل دخول غير ناجحة.',
      invalid_request: 'راجع البيانات المدخلة وحاول مرة أخرى.',
      password_change_rejected: 'تعذر تغيير كلمة المرور. تأكد من كلمة المرور الحالية ومتطلبات كلمة المرور الجديدة.',
    }

    throw new ApiError(
      messages[payload?.error ?? ''] ?? payload?.error ?? payload?.title ?? `فشل الطلب (HTTP ${response.status}).`,
      response.status,
    )
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export function login(username: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password }),
  }, { authenticated: false })
}

export function changePassword(
  passwordChangeToken: string,
  currentPassword: string,
  newPassword: string,
): Promise<AuthenticatedResponse> {
  return request<AuthenticatedResponse>('/api/auth/change-password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword }),
  }, { accessToken: passwordChangeToken })
}

export function saveAdminSession(session: AuthenticatedResponse): void {
  if (typeof window === 'undefined') return
  window.sessionStorage.setItem(ACCESS_TOKEN_KEY, session.accessToken)
  window.sessionStorage.setItem(ACCESS_EXPIRY_KEY, session.expiresAtUtc)
  window.sessionStorage.setItem(USER_KEY, JSON.stringify(session.user))
}

export function readAdminSession(): { user: AuthenticatedUser } | null {
  if (typeof window === 'undefined') return null

  const token = window.sessionStorage.getItem(ACCESS_TOKEN_KEY)
  const expiry = window.sessionStorage.getItem(ACCESS_EXPIRY_KEY)
  const rawUser = window.sessionStorage.getItem(USER_KEY)

  if (!token || !expiry || !rawUser || new Date(expiry).getTime() <= Date.now()) {
    clearAdminSession()
    return null
  }

  try {
    return { user: JSON.parse(rawUser) as AuthenticatedUser }
  } catch {
    clearAdminSession()
    return null
  }
}

export function clearAdminSession(): void {
  if (typeof window === 'undefined') return
  window.sessionStorage.removeItem(ACCESS_TOKEN_KEY)
  window.sessionStorage.removeItem(ACCESS_EXPIRY_KEY)
  window.sessionStorage.removeItem(USER_KEY)
}

export function getDevices(): Promise<ApiDevice[]> {
  return request<ApiDevice[]>('/api/devices')
}

export function seedDefaultDevices(): Promise<{ added: number; message: string }> {
  return request('/api/devices/seed-defaults', { method: 'POST' })
}

export function updateDeviceRates(id: string, rates: RateInput[]): Promise<ApiDevice> {
  return request<ApiDevice>(`/api/devices/${encodeURIComponent(id)}/rates`, {
    method: 'PUT',
    body: JSON.stringify({ rates }),
  })
}
