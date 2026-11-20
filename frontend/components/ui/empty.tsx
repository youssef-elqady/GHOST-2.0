import type { HTMLAttributes, ReactNode } from 'react'

type EmptyProps = HTMLAttributes<HTMLDivElement> & {
  icon?: ReactNode
  meta?: ReactNode
  title?: string
  description?: string
}

export function Empty({ icon, meta, title, description, className, ...props }: EmptyProps) {
  return (
    <div className={['admin-empty-state', className].filter(Boolean).join(' ')} role="status" {...props}>
      {meta}
      {icon}
      {title && <strong>{title}</strong>}
      {description && <p>{description}</p>}
    </div>
  )
}
