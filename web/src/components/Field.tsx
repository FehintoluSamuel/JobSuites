/**
 * Form field primitives.
 *
 * Rules enforced here rather than per page, so no screen can regress:
 *  - a real <label> bound to the input (never placeholder-only)
 *  - errors wired via aria-describedby + aria-invalid
 *  - the error text rendered in text, not colour alone
 *  - minimum 44px hit target
 */

import { useId, type InputHTMLAttributes, type ReactNode, type Ref } from 'react'

const base =
  'w-full min-h-11 rounded-md border bg-surface px-3 py-2 text-ink placeholder:text-ink-faint ' +
  'transition-colors disabled:cursor-not-allowed disabled:bg-canvas disabled:text-ink-muted'

interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  errors?: string[]
  hint?: string
}

export function Field({ label, errors, hint, ...props }: FieldProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`
  const hasError = Boolean(errors?.length)

  const describedBy = [hasError ? errorId : null, hint ? hintId : null]
    .filter(Boolean)
    .join(' ')

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-semibold text-ink">
        {label}
      </label>

      {hint && (
        <p id={hintId} className="text-sm text-ink-muted">
          {hint}
        </p>
      )}

      <input
        {...props}
        id={id}
        aria-invalid={hasError || undefined}
        aria-describedby={describedBy || undefined}
        className={`${base} ${
          hasError
            ? 'border-danger focus-visible:outline-danger'
            : 'border-border-strong hover:border-ink-muted'
        }`}
      />

      {hasError && (
        <p id={errorId} className="text-sm font-medium text-danger">
          {errors!.join(' ')}
        </p>
      )}
    </div>
  )
}

/**
 * Focusable error summary shown above the form after a failed submit.
 *
 * Per WCAG and the guidance in docs/UX-AUTH.md: it must be reachable by
 * keyboard and screen reader, it must receive focus, and each entry must link
 * to the field it refers to. A toast alone is not sufficient.
 */
export function ErrorSummary({
  title,
  errors,
  ref,
}: {
  title: string
  errors: Record<string, string[]>
  ref?: Ref<HTMLDivElement>
}) {
  const entries = Object.entries(errors).filter(([, msgs]) => msgs.length)
  if (entries.length === 0) return null

  return (
    <div
      ref={ref}
      role="alert"
      tabIndex={-1}
      data-testid="error-summary"
      className="rounded-md border border-danger bg-danger-subtle p-4 focus-visible:outline-danger"
    >
      <h2 className="text-sm font-bold text-danger">{title}</h2>
      <ul className="mt-2 space-y-1">
        {entries.map(([field, messages]) => (
          <li key={field} className="text-sm text-danger">
            <a href={`#${field}`} className="font-medium underline underline-offset-2">
              {messages.join(' ')}
            </a>
          </li>
        ))}
      </ul>
    </div>
  )
}

export function SubmitButton({
  pending,
  children,
}: {
  pending: boolean
  children: ReactNode
}) {
  return (
    <button
      type="submit"
      disabled={pending}
      aria-busy={pending}
      className="flex min-h-11 w-full items-center justify-center rounded-md bg-accent px-4 py-2.5 text-sm font-bold text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:bg-border-strong"
    >
      {pending ? 'Working…' : children}
    </button>
  )
}
