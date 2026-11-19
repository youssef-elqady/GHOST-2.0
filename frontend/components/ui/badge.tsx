import type { HTMLAttributes } from 'react'

export type BadgeTone = 'neutral' | 'gold' | 'success' | 'warning' | 'danger' | 'info' | 'violet'

type BadgeProps = HTMLAttributes<HTMLSpanElement> & {
  tone?: BadgeTone
  dot?: boolean
}

export function Badge({
  tone = 'neutral',
  dot = false,
  className,
  children,
  ...props
}: BadgeProps) {
  return (
    <span
      className={['ui-badge', `ui-badge--${tone}`, className].filter(Boolean).join(' ')}
      {...props}
    >
      {dot && <span className="ui-badge__dot" aria-hidden="true" />}
      {children}
    </span>
  )
}
