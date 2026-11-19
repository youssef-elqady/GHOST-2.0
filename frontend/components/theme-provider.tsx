'use client'

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'

export type GhostTheme = 'light' | 'dark'

type ThemeContextValue = {
  theme: GhostTheme
  isDark: boolean
  setTheme: (theme: GhostTheme) => void
  toggleTheme: () => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<GhostTheme>('dark')
  const [initialized, setInitialized] = useState(false)

  useEffect(() => {
    const currentTheme = document.documentElement.classList.contains('dark')
      ? 'dark'
      : 'light'

    setThemeState(currentTheme)
    setInitialized(true)
  }, [])

  useEffect(() => {
    if (!initialized) return

    document.documentElement.classList.toggle('dark', theme === 'dark')

    try {
      window.localStorage.setItem('ghost-theme', theme)
    } catch {
      // Theme still works for this session when browser storage is unavailable.
    }
  }, [initialized, theme])

  const setTheme = useCallback((nextTheme: GhostTheme) => {
    setThemeState(nextTheme)
  }, [])

  const toggleTheme = useCallback(() => {
    setThemeState((current) => current === 'dark' ? 'light' : 'dark')
  }, [])

  const value = useMemo<ThemeContextValue>(() => ({
    theme,
    isDark: theme === 'dark',
    setTheme,
    toggleTheme,
  }), [theme, setTheme, toggleTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme() {
  const context = useContext(ThemeContext)

  if (!context) {
    throw new Error('useTheme must be used within GHOST ThemeProvider.')
  }

  return context
}
