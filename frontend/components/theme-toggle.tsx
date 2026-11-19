'use client'

import { Moon, Sun } from 'lucide-react'
import { useGhostTheme } from '@/components/theme-provider'

export function ThemeToggle() {
  const { theme, toggleTheme } = useGhostTheme()
  const isDark = theme === 'dark'

  return (
    <button
      className="theme-toggle"
      type="button"
      onClick={toggleTheme}
      aria-label={isDark ? 'تفعيل الوضع الفاتح' : 'تفعيل الوضع الداكن'}
      title={isDark ? 'تفعيل الوضع الفاتح' : 'تفعيل الوضع الداكن'}
    >
      {isDark ? <Sun size={18} aria-hidden="true" /> : <Moon size={18} aria-hidden="true" />}
      <span>{isDark ? 'فاتح' : 'داكن'}</span>
    </button>
  )
}
