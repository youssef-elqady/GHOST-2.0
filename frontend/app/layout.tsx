import type { Metadata, Viewport } from 'next'
import './globals.css'
import { ThemeProvider } from '@/components/theme-provider'

export const metadata: Metadata = {
  title: 'GHOST Staff · مساحة الموظفين',
  description: 'واجهة التشغيل اليومية لموظفي GHOST PlayStation Lounge.',
  generator: 'GHOST',
}

export const viewport: Viewport = {
  colorScheme: 'dark light',
  themeColor: '#0e0e0d',
  userScalable: true,
}

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <html lang="ar" dir="rtl" suppressHydrationWarning><head><script dangerouslySetInnerHTML={{ __html: "try{var t=localStorage.getItem('ghost-theme');t=t==='light'||t==='dark'?t:'dark';document.documentElement.classList.toggle('dark',t==='dark');document.documentElement.style.colorScheme=t}catch(e){}" }} /></head><body className="antialiased"><ThemeProvider>{children}</ThemeProvider></body></html>
}
