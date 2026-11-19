import type { HTMLAttributes, ReactNode } from 'react'

type CardProps = HTMLAttributes<HTMLDivElement> & {
  padded?: boolean
}

export function Card({ padded = true, className, ...props }: CardProps) {
  return (
    <div
      className={['ui-card', padded ? 'ui-card--padded' : '', className].filter(Boolean).join(' ')}
      {...props}
    />
  )
}

export function CardHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={['ui-card__header', className].filter(Boolean).join(' ')} {...props} />
}

export function CardTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return <h2 className={['ui-card__title', className].filter(Boolean).join(' ')} {...props} />
}

export function CardDescription({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return <p className={['ui-card__description', className].filter(Boolean).join(' ')} {...props} />
}

export function CardContent({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={['ui-card__content', className].filter(Boolean).join(' ')} {...props} />
}

export function CardFooter({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={['ui-card__footer', className].filter(Boolean).join(' ')} {...props} />
}
