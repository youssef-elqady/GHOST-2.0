'use client'

import { useEffect, useState, type FormEvent } from 'react'
import { ArrowLeft, Eye, EyeOff, Gamepad2, LockKeyhole, ShieldCheck, UserRound } from 'lucide-react'
import { AdminDashboard } from '@/components/admin-dashboard'
import {
  changePassword,
  clearAdminSession,
  login,
  readAdminSession,
  saveAdminSession,
  type AuthenticatedUser,
} from '@/lib/ghost-api'
import './admin-auth.css'

type AuthMode = 'login' | 'change-password'

export function AdminAuthGate() {
  const [ready, setReady] = useState(false)
  const [user, setUser] = useState<AuthenticatedUser | null>(null)
  const [mode, setMode] = useState<AuthMode>('login')
  const [passwordToken, setPasswordToken] = useState<string | null>(null)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const session = readAdminSession()
    if (session) setUser(session.user)
    setReady(true)

    const handleExpired = () => {
      clearAdminSession()
      setUser(null)
      setMode('login')
      setError('انتهت صلاحية جلسة الدخول. سجّل الدخول مرة أخرى.')
    }

    window.addEventListener('ghost:auth-expired', handleExpired)
    return () => window.removeEventListener('ghost:auth-expired', handleExpired)
  }, [])

  async function handleLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError(null)

    try {
      const result = await login(username.trim(), password)
      if (result.status === 'password_change_required') {
        setPasswordToken(result.passwordChangeToken)
        setMode('change-password')
        setCurrentPassword(password)
        setPassword('')
        return
      }

      saveAdminSession(result)
      setUser(result.user)
      setPassword('')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر تسجيل الدخول.')
    } finally {
      setBusy(false)
    }
  }

  async function handlePasswordChange(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)

    if (!passwordToken) {
      setMode('login')
      setError('انتهت صلاحية رابط تغيير كلمة المرور. سجّل الدخول مرة أخرى.')
      return
    }
    if (newPassword.length < 8) {
      setError('كلمة المرور الجديدة يجب ألا تقل عن 8 أحرف.')
      return
    }
    if (newPassword !== confirmPassword) {
      setError('تأكيد كلمة المرور غير مطابق.')
      return
    }

    setBusy(true)
    try {
      const result = await changePassword(passwordToken, currentPassword, newPassword)
      saveAdminSession(result)
      setUser(result.user)
      setPasswordToken(null)
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر تغيير كلمة المرور.')
    } finally {
      setBusy(false)
    }
  }

  function handleLogout() {
    clearAdminSession()
    setUser(null)
    setMode('login')
    setPasswordToken(null)
    setPassword('')
    setError(null)
  }

  if (!ready) {
    return <div className="admin-auth-loading" dir="rtl">جاري التحقق من جلسة الإدارة...</div>
  }

  if (user) {
    return <AdminDashboard user={user} onLogout={handleLogout} />
  }

  return (
    <main className="admin-auth-page" dir="rtl">
      <div className="admin-auth-glow admin-auth-glow--one" />
      <div className="admin-auth-glow admin-auth-glow--two" />
      <section className="admin-auth-card">
        <div className="admin-auth-brand">
          <span className="admin-auth-mark"><Gamepad2 size={25} /></span>
          <div><strong>GHOST</strong><small>ADMINISTRATION</small></div>
        </div>

        <div className="admin-auth-heading">
          <span className="admin-auth-heading-icon">{mode === 'login' ? <LockKeyhole size={20} /> : <ShieldCheck size={20} />}</span>
          <p className="admin-auth-eyebrow">نظام إدارة GHOST · أبشواي</p>
          <h1>{mode === 'login' ? 'مرحبًا بعودتك' : 'تأمين حساب الإدارة'}</h1>
          <p>{mode === 'login' ? 'سجّل الدخول للمتابعة إلى لوحة الإدارة.' : 'هذه أول مرة تستخدم فيها الحساب. أنشئ كلمة مرور جديدة قبل المتابعة.'}</p>
        </div>

        {error && <div className="admin-auth-error" role="alert">{error}</div>}

        {mode === 'login' ? (
          <form className="admin-auth-form" onSubmit={handleLogin}>
            <label>
              <span>اسم المستخدم</span>
              <div className="admin-auth-input-wrap"><UserRound size={17} /><input autoComplete="username" value={username} onChange={(event) => setUsername(event.target.value)} placeholder="أدخل اسم المستخدم" required maxLength={256} /></div>
            </label>
            <label>
              <span>كلمة المرور</span>
              <div className="admin-auth-input-wrap"><LockKeyhole size={17} /><input type={showPassword ? 'text' : 'password'} autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="أدخل كلمة المرور" required maxLength={128} /><button type="button" className="admin-auth-visibility" onClick={() => setShowPassword((value) => !value)} aria-label={showPassword ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور'}>{showPassword ? <EyeOff size={17} /> : <Eye size={17} />}</button></div>
            </label>
            <button className="admin-auth-submit" type="submit" disabled={busy}>{busy ? 'جارٍ التحقق...' : 'تسجيل الدخول'} <ArrowLeft size={17} /></button>
          </form>
        ) : (
          <form className="admin-auth-form" onSubmit={handlePasswordChange}>
            <label>
              <span>كلمة المرور الحالية</span>
              <div className="admin-auth-input-wrap"><LockKeyhole size={17} /><input type="password" autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} required maxLength={128} /></div>
            </label>
            <label>
              <span>كلمة المرور الجديدة</span>
              <div className="admin-auth-input-wrap"><LockKeyhole size={17} /><input type="password" autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} required minLength={8} maxLength={128} /></div>
            </label>
            <label>
              <span>تأكيد كلمة المرور الجديدة</span>
              <div className="admin-auth-input-wrap"><LockKeyhole size={17} /><input type="password" autoComplete="new-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} required minLength={8} maxLength={128} /></div>
            </label>
            <button className="admin-auth-submit" type="submit" disabled={busy}>{busy ? 'جارٍ حفظ كلمة المرور...' : 'حفظ ومتابعة'} <ArrowLeft size={17} /></button>
          </form>
        )}

        <div className="admin-auth-footer"><ShieldCheck size={15} /><span>اتصال آمن عبر GHOST API · لا يتم حفظ كلمة المرور في المتصفح</span></div>
      </section>
      <p className="admin-auth-copyright">GHOST PLAYSTATION <span>·</span> ADMIN PORTAL</p>
    </main>
  )
}
