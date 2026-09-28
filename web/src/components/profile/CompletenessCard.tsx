import type { CompletenessChecks } from '../../lib/profile'

function CheckIcon() {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 20 20"
      fill="currentColor"
      className="h-4 w-4 shrink-0"
    >
      <path
        fillRule="evenodd"
        d="M16.7 5.3a1 1 0 010 1.4l-7 7a1 1 0 01-1.4 0l-3-3a1 1 0 111.4-1.4L9 11.6l6.3-6.3a1 1 0 011.4 0z"
        clipRule="evenodd"
      />
    </svg>
  )
}

/**
 * The live checklist. It recomputes from the in-progress draft, so it doubles
 * as the onboarding prompt (docs/PRODUCT.md): show what's missing, make each
 * gap a jump to the right compartment, and make the number impossible to
 * ignore. Sits at the very top of the mobile column for that reason.
 */
export function CompletenessCard({
  checks,
  percent,
  onNavigate,
}: {
  checks: CompletenessChecks
  percent: number
  onNavigate: (label: string) => void
}) {
  const missing = checks.required.filter((c) => !c.done)
  const optionalDone = checks.optional.filter((c) => c.done).length
  const optionalMissing = checks.optional.filter((c) => !c.done)

  return (
    <aside
      className="rounded-lg border border-accent/30 bg-accent-tint p-4 lg:p-5"
      aria-label="Profile completeness"
    >
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-sm font-bold uppercase tracking-wide text-ink">
          Profile completeness
        </h2>
        <span className="text-lg font-extrabold text-accent">{percent}%</span>
      </div>

      <div
        role="progressbar"
        aria-valuenow={percent}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="Profile completeness"
        className="mt-3 h-2 overflow-hidden rounded-full bg-accent/20"
      >
        <div
          className="h-full rounded-full bg-accent transition-all duration-300"
          style={{ width: `${Math.max(percent, 4)}%` }}
        />
      </div>

      {missing.length > 0 ? (
        <p className="mt-3 text-sm text-ink-body">
          Add <strong className="text-ink">{missing.length}</strong> core{' '}
          {missing.length === 1 ? 'item' : 'items'} for stronger matches.
        </p>
      ) : (
        <p className="mt-3 text-sm font-semibold text-accent">
          All core items done. Nice one.
        </p>
      )}

      <ul className="mt-3 space-y-1.5">
        {checks.required.map((item) =>
          item.done ? (
            <li key={item.label} className="flex items-center gap-2 text-sm text-ink-muted">
              <span className="flex h-5 w-5 items-center justify-center rounded-full bg-accent text-white">
                <CheckIcon />
              </span>
              <span className="line-through decoration-ink-faint">{item.label}</span>
            </li>
          ) : (
            <li key={item.label} className="flex items-center gap-2 text-sm">
              <span
                aria-hidden="true"
                className="flex h-5 w-5 items-center justify-center rounded-full border-2 border-ink-faint"
              />
              <button
                type="button"
                onClick={() => onNavigate(item.label)}
                className="min-h-9 text-start font-semibold text-ink transition-colors hover:text-accent focus-visible:outline-accent"
              >
                {item.label}
              </button>
            </li>
          ),
        )}
      </ul>

      <p className="mt-4 border-t border-accent/25 pt-3 text-sm text-ink-muted">
        Optional extras: <strong className="text-ink">{optionalDone}</strong>/{' '}
        {checks.optional.length}
        {optionalMissing.length > 0 && (
          <span className="block text-xs text-ink-faint">
            {optionalMissing.map((o) => o.label).join(', ')}
          </span>
        )}
      </p>
    </aside>
  )
}