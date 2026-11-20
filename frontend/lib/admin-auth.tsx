'use client'

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { clearDeviceQueryCache } from '@/lib/device-query'

export type AdminUser = { id: string; username: string; fullName: string; roles: string[] }
type AuthValue = { token: string | null; user: AdminUser | null; setSession: (token: string, user: AdminUser, expiresAtUtc: string) => void; clearSession: () => void }
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
  useEffect(() => {
    if (!token || !expiresAt) return
    const remaining = new Date(expiresAt).getTime() - Date.now()
    if (!Number.isFinite(remaining) || remaining <= 0) {
      window.dispatchEvent(new Event('ghost-admin-session-expired'))
      return
    }
    const timeout = window.setTimeout(() => window.dispatchEvent(new Event('ghost-admin-session-expired')), remaining)
    return () => window.clearTimeout(timeout)
  }, [token, expiresAt])
  const setSession = useCallback((nextToken: string, nextUser: AdminUser, nextExpiresAt: string) => { clearDeviceQueryCache(); setToken(nextToken); setUser(nextUser); setExpiresAt(nextExpiresAt) }, [])
  const clearSession = useCallback(() => { clearDeviceQueryCache(); setToken(null); setUser(null); setExpiresAt(null) }, [])
  const value = useMemo(() => ({ token, user, setSession, clearSession }), [token, user, setSession, clearSession])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAdminAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAdminAuth must be used inside AdminAuthProvider.')
  return value
}
