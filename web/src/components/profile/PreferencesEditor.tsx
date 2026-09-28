import { useId, useState } from 'react'
import { JOB_TYPES, NIGERIAN_STATES } from '../../lib/profile'

const labelClass = 'block text-sm font-semibold text-ink'
const inputClass =
  'min-h-11 w-full rounded-md border border-border-strong bg-surface px-3 text-sm text-ink placeholder:text-ink-faint transition-colors hover:border-ink-muted focus-visible:outline-accent'

/**
 * What the matching engine actually filters on (docs/PRODUCT.md §4): target
 * roles, preferred states, and job types. These are the fields that turn a
 * vague "CV in a box" into a profile the product can rank by.
 */
export function PreferencesEditor({
  targetRoles,
  preferredStates,
  desiredJobTypes,
  desiredSalary,
  availability,
  roleSuggestions,
  onChange,
}: {
  targetRoles: string[]
  preferredStates: string[]
  desiredJobTypes: string[]
  desiredSalary: string | null
  availability: string | null
  roleSuggestions: string[]
  onChange: (patch: {
    targetRoles?: string[]
    preferredStates?: string[]
    desiredJobTypes?: string[]
    desiredSalary?: string | null
    availability?: string | null
  }) => void
}) {
  return (
    <div className="space-y-5">
      <TargetRolesEditor value={targetRoles} suggestions={roleSuggestions} onChange={(targetRoles) => onChange({ targetRoles })} />
      <StatePicker value={preferredStates} onChange={(preferredStates) => onChange({ preferredStates })} />

      <div>
        <span className={labelClass}>Desired job types</span>
        <div className="mt-2 flex flex-wrap gap-2">
          {JOB_TYPES.map((type) => {
            const active = desiredJobTypes.includes(type)
            return (
              <button
                key={type}
                type="button"
                aria-pressed={active}
                onClick={() =>
                  onChange({
                    desiredJobTypes: active
                      ? desiredJobTypes.filter((t) => t !== type)
                      : [...desiredJobTypes, type],
                  })
                }
                className={`min-h-11 rounded-full border px-4 text-sm font-semibold transition-colors ${
                  active
                    ? 'border-accent bg-accent text-white'
                    : 'border-border-strong bg-surface text-ink-body hover:border-ink-muted hover:bg-canvas'
                }`}
              >
                {type}
              </button>
            )
          })}
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <label>
          <span className={labelClass}>Desired salary</span>
          <input
            value={desiredSalary ?? ''}
            onChange={(e) => onChange({ desiredSalary: e.target.value || null })}
            placeholder="e.g. ₦600k/month"
            className={inputClass}
          />
        </label>
        <label>
          <span className={labelClass}>Availability</span>
          <input
            value={availability ?? ''}
            onChange={(e) => onChange({ availability: e.target.value || null })}
            placeholder="e.g. 2 weeks notice"
            className={inputClass}
          />
        </label>
      </div>
    </div>
  )
}

function TargetRolesEditor({
  value,
  suggestions,
  onChange,
}: {
  value: string[]
  suggestions: string[]
  onChange: (value: string[]) => void
}) {
  const [draft, setDraft] = useState('')
  const listId = useId()

  const add = (raw: string) => {
    const role = raw.trim()
    if (!role) return
    if (value.some((r) => r.toLowerCase() === role.toLowerCase())) return
    onChange([...value, role])
  }

  return (
    <div>
      <label htmlFor="target-roles" className={labelClass}>
        Target roles
      </label>
      <p className="mt-1 text-sm text-ink-muted">
        Pick the roles you are aiming for. We suggest titles already on the board.
      </p>
      <div className="mt-2 flex items-end gap-2">
        <input
          id="target-roles"
          value={draft}
          list={listId}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              add(draft)
              setDraft('')
            }
          }}
          placeholder="e.g. Supply Chain Manager"
          className={inputClass}
        />
        <button
          type="button"
          onClick={() => {
            add(draft)
            setDraft('')
          }}
          className="min-h-11 rounded-md border border-border-strong bg-surface px-4 text-sm font-bold text-ink transition-colors hover:bg-canvas"
        >
          Add
        </button>
      </div>
      <datalist id={listId}>
        {suggestions.map((s) => (
          <option key={s} value={s} />
        ))}
      </datalist>

      {value.length > 0 && (
        <ul className="mt-3 flex flex-wrap gap-2">
          {value.map((role) => (
            <li
              key={role}
              className="inline-flex min-h-9 items-center gap-1.5 rounded-full bg-accent-subtle px-3 text-sm font-semibold text-accent"
            >
              {role}
              <button
                type="button"
                onClick={() => onChange(value.filter((r) => r !== role))}
                aria-label={`Remove ${role}`}
                className="rounded-full px-1 font-bold hover:bg-accent-tint"
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function StatePicker({
  value,
  onChange,
}: {
  value: string[]
  onChange: (value: string[]) => void
}) {
  const [query, setQuery] = useState('')
  const shown = NIGERIAN_STATES.filter((s) =>
    s.toLowerCase().includes(query.trim().toLowerCase()),
  )

  return (
    <div>
      <span className={labelClass}>Preferred states</span>
      <p className="mt-1 text-sm text-ink-muted">
        The 36 states plus the FCT. Roles posted outside these won’t rank as
        highly for you.
      </p>

      <div className="mt-2">
        <label htmlFor="state-search" className="sr-only">
          Search states
        </label>
        <input
          id="state-search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Search states…"
          className={inputClass}
        />
      </div>

      {shown.length > 0 && (
        <ul className="mt-3 grid max-h-56 grid-cols-2 gap-x-3 gap-y-1 overflow-y-auto rounded-md border border-border-subtle p-3 sm:grid-cols-3">
          {shown.map((state) => {
            const active = value.includes(state)
            return (
              <li key={state}>
                <label className="flex min-h-9 cursor-pointer items-center gap-2 text-sm text-ink-body hover:text-ink">
                  <input
                    type="checkbox"
                    checked={active}
                    onChange={() =>
                      onChange(active ? value.filter((s) => s !== state) : [...value, state])
                    }
                    className="h-4 w-4 accent-[var(--color-accent)]"
                  />
                  {state}
                </label>
              </li>
            )
          })}
        </ul>
      )}

      {value.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-2">
          {value.map((state) => (
            <li
              key={state}
              className="inline-flex min-h-9 items-center gap-1.5 rounded-full bg-brand px-3 text-sm font-semibold text-white"
            >
              {state}
              <button
                type="button"
                onClick={() => onChange(value.filter((s) => s !== state))}
                aria-label={`Remove ${state}`}
                className="rounded-full px-1 font-bold hover:bg-brand-deep"
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}