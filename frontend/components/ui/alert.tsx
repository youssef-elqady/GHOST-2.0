import type { HTMLAttributes } from 'react'

export type AlertVariant = 'info' | 'warning' | 'success'

type AlertProps = HTMLAttributes<HTMLDivElement> & {
  variant?: AlertVariant
}

export function Alert({ variant = 'info', className, role, ...props }: AlertProps) {
  return (
    <div
      className={['admin-notice', `admin-notice--${variant}`, className].filter(Boolean).join(' ')}
      role={role ?? (variant === 'warning' ? 'alert' : 'status')}
      {...props}
    />
  )
}
