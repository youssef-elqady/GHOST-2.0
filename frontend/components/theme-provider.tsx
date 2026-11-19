'use client'

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'

export type GhostTheme = 'light' | 'dark'

type ThemeContextValue = {
  theme: GhostTheme
  setTheme: (theme: GhostTheme) => void
  toggleTheme: () => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)
const STORAGE_KEY = 'ghost-theme'

function applyTheme(theme: GhostTheme) {
  document.documentElement.classList.toggle('dark', theme === 'dark')
  document.documentElement.style.colorScheme = theme
}

export function ThemeProvider({ children }: Readonly<{ children: React.ReactNode }>) {
  const [theme, setThemeState] = useState<GhostTheme>('dark')

  useEffect(() => {
    const saved = window.localStorage.getItem(STORAGE_KEY)
    const initial: GhostTheme = saved === 'light' || saved === 'dark'
      ? saved
      : document.documentElement.classList.contains('dark') ? 'dark' : 'light'

    setThemeState(initial)
    applyTheme(initial)
  }, [])

  const setTheme = useCallback((next: GhostTheme) => {
    setThemeState(next)
    applyTheme(next)
    window.localStorage.setItem(STORAGE_KEY, next)
  }, [])

  const toggleTheme = useCallback(() => {
    setTheme(theme === 'dark' ? 'light' : 'dark')
  }, [setTheme, theme])

  const value = useMemo(() => ({ theme, setTheme, toggleTheme }), [theme, setTheme, toggleTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useGhostTheme() {
  const context = useContext(ThemeContext)
  if (!context) throw new Error('useGhostTheme must be used within ThemeProvider')
  return context
}
