'use client'

import { useEffect, useState, type FormEvent } from 'react'
import { useRouter } from 'next/navigation'
import Link from 'next/link'
import { ArrowLeft, Eye, EyeOff, LockKeyhole, ShieldCheck } from 'lucide-react'
import { changePassword, login, type PasswordChangeRequired } from '@/lib/ghost-api'
import { useAdminAuth } from '@/lib/admin-auth'

export function AdminLogin() {
  const router = useRouter()
  const { setSession, token: restoredToken } = useAdminAuth()
  const [step, setStep] = useState<'login' | 'password'>('login')
  useEffect(() => { if (restoredToken && step === 'login') router.replace('/admin') }, [restoredToken, step, router])
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [passwordToken, setPasswordToken] = useState('')
  const [visible, setVisible] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      if (step === 'login') {
        const result = await login(username.trim(), password)
        if (result.status === 'password_change_required') {
          setPasswordToken((result as PasswordChangeRequired).passwordChangeToken)
          setStep('password')
          return
        }
        setSession(result.accessToken, result.user, result.expiresAtUtc)
        router.replace('/admin')
        return
      }
      const result = await changePassword(passwordToken, password, newPassword)
      setSession(result.accessToken, result.user, result.expiresAtUtc)
      router.replace('/admin')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'تعذر تسجيل الدخول.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="admin-login-page" dir="rtl">
      <aside className="admin-login-aside">
        <Link href="/" className="admin-login-brand" aria-label="GHOST الرئيسية"><span className="admin-login-brand-mark">G</span> GHOST</Link>
        <div className="admin-login-pitch">
          <p>مساحة الإدارة</p>
          <h1>رؤية واضحة لتشغيل المكان.</h1>
          <span>تابع الأجهزة والتقارير والإعدادات من لوحة واحدة. تُعرض البيانات التجريبية بوضوح منفصلة عن بيانات النظام.</span>
        </div>
        <footer><ShieldCheck /> جلسة دخول آمنة — لا تُحفظ بيانات الدخول على هذا الجهاز.</footer>
        <div className="admin-login-geometry" aria-hidden="true" />
      </aside>
      <section className="admin-login-form-side">
        <div className="admin-login-card">
          <span className="admin-source admin-source--live"><i /> مصادقة عبر GHOST API</span>
          <h2>{step === 'login' ? 'تسجيل الدخول' : 'تغيير كلمة المرور'}</h2>
          <p>{step === 'login' ? 'أدخل بيانات حساب الإدارة للمتابعة.' : 'يتطلب الحساب كلمة مرور جديدة قبل فتح لوحة الإدارة.'}</p>
          <form className="admin-login-form" onSubmit={submit}>
            {step === 'login' && <label>اسم المستخدم<input autoComplete="username" required maxLength={256} value={username} onChange={(event) => setUsername(event.target.value)} placeholder="اسم المستخدم" /></label>}
            <label>{step === 'login' ? 'كلمة المرور' : 'كلمة المرور الحالية'}
              <span className="admin-password-field"><input autoComplete="current-password" required type={visible ? 'text' : 'password'} maxLength={128} value={password} onChange={(event) => setPassword(event.target.value)} placeholder="كلمة المرور" /><button type="button" aria-label={visible ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور'} onClick={() => setVisible((value) => !value)}>{visible ? <EyeOff /> : <Eye />}</button></span>
            </label>
            {step === 'password' && <label>كلمة المرور الجديدة<input autoComplete="new-password" required minLength={8} maxLength={128} type={visible ? 'text' : 'password'} value={newPassword} onChange={(event) => setNewPassword(event.target.value)} placeholder="ثمانية أحرف على الأقل" /><small>يتم التحقق من متطلبات كلمة المرور عند الحفظ.</small></label>}
            {error && <p className="admin-login-error" role="alert">{error}</p>}
            <button className="admin-login-submit" type="submit" disabled={busy}>{busy ? 'جارٍ التحقق…' : step === 'login' ? 'تسجيل الدخول' : 'حفظ كلمة المرور'}{!busy && <ArrowLeft />}</button>
          </form>
          {step === 'password' && <button className="admin-back-login" onClick={() => { setStep('login'); setError(''); setPasswordToken('') }}>العودة لتسجيل الدخول</button>}
          <div className="admin-login-security"><LockKeyhole /><span>رمز الدخول يبقى في الذاكرة فقط ويُحذف عند انتهاء الجلسة.</span></div>
        </div>
      </section>
    </main>
  )
}
