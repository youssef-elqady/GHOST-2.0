'use client'

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { clearDeviceQueryCache } from '@/lib/device-query'
import { configureApiAuth, logoutRequest, refreshAccessToken, type AuthResponse } from '@/lib/ghost-api'

export type AdminUser = { id: string; username: string; fullName: string; roles: string[] }
type AuthValue = { token: string | null; restoring: boolean; user: AdminUser | null; setSession: (token: string, user: AdminUser, expiresAtUtc: string) => void; clearSession: () => void; logout: () => Promise<void> }
const AuthContext = createContext<AuthValue | null>(null)

export function AdminAuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(null)
  const [user, setUser] = useState<AdminUser | null>(null)
  const [expiresAt, setExpiresAt] = useState<string | null>(null)
  useEffect(() => {
    const expireSession = () => { clearDeviceQueryCache(); setToken(null); setUser(null); setExpiresAt(null) }
    window.addEventListener('ghost-admin-session-expired', expireSession)
    return () => window.removeEventListener('ghost-admin-session-expired', expireSession)
  }, [])
  const [restoring, setRestoring] = useState(true)
  const applyAuth = useCallback((auth: AuthResponse) => {
    configureApiAuth({ token: auth.accessToken })
    setToken(auth.accessToken); setUser(auth.user); setExpiresAt(auth.expiresAtUtc)
  }, [])
  useEffect(() => { configureApiAuth({ token, onRefreshed: applyAuth }) }, [token, applyAuth])
  // A page reload loses the in-memory token. The HttpOnly refresh cookie brings the user back without a password.
  useEffect(() => {
    let cancelled = false
    refreshAccessToken().then((auth) => { if (!cancelled && auth) applyAuth(auth) }).finally(() => { if (!cancelled) setRestoring(false) })
    return () => { cancelled = true }
  }, [applyAuth])
  // Refresh about 60 seconds before the access token expires. Only a failed refresh ends the session.
  useEffect(() => {
    if (!token || !expiresAt) return
    const remaining = new Date(expiresAt).getTime() - Date.now()
    if (!Number.isFinite(remaining)) return
    const timeout = window.setTimeout(async () => {
      const auth = await refreshAccessToken()
      if (auth) applyAuth(auth)
      else window.dispatchEvent(new Event('ghost-admin-session-expired'))
    }, Math.max(remaining - 60_000, 0))
    return () => window.clearTimeout(timeout)
  }, [token, expiresAt, applyAuth])
  const setSession = useCallback((nextToken: string, nextUser: AdminUser, nextExpiresAt: string) => { clearDeviceQueryCache(); setToken(nextToken); setUser(nextUser); setExpiresAt(nextExpiresAt) }, [])
  const clearSession = useCallback(() => { clearDeviceQueryCache(); configureApiAuth({ token: null }); setToken(null); setUser(null); setExpiresAt(null) }, [])
  const logout = useCallback(async () => { await logoutRequest(); clearSession() }, [clearSession])
  const value = useMemo(() => ({ token, restoring, user, setSession, clearSession, logout }), [token, restoring, user, setSession, clearSession, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAdminAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAdminAuth must be used inside AdminAuthProvider.')
  return value
}
