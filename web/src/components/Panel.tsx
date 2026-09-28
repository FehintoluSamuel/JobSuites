import type { ReactNode } from 'react'

/**
 * The compartment. Every feature on the page lives in one of these bordered
 * containers, LinkedIn-style: on wide screens they sit at different widths in a
 * shared grid, on narrow screens they stack. The container does not chase
 * heights — its size is set by its content, which is what makes the columns
 * read as deliberately sized rather than uniformly tiled.
 */
export function Panel({
  title,
  actions,
  id,
  className = '',
  children,
}: {
  title: string
  actions?: ReactNode
  id?: string
  className?: string
  children: ReactNode
}) {
  return (
    <section
      id={id}
      tabIndex={-1}
      aria-labelledby={id ? `${id}-title` : undefined}
      className={`rounded-lg border border-border-subtle bg-surface outline-none focus-visible:ring-2 focus-visible:ring-accent/40 ${className}`}
    >
      <header className="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle px-4 py-3 lg:px-5">
        <h2
          id={id ? `${id}-title` : undefined}
          className="text-sm font-bold uppercase tracking-wide text-ink"
        >
          {title}
        </h2>
        {actions}
      </header>
      <div className="px-4 py-4 lg:px-5">{children}</div>
    </section>
  )
}

export function PanelButton({
  onClick,
  disabled,
  children,
}: {
  onClick: () => void
  disabled?: boolean
  children: ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="min-h-11 rounded-md px-3 text-sm font-bold text-accent transition-colors hover:bg-accent-subtle disabled:cursor-not-allowed disabled:text-ink-faint"
    >
      {children}
    </button>
  )
}