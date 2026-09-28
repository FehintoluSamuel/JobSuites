import type { CandidateProfile } from '../../lib/api'

type Experience = CandidateProfile['experiences'][number]

const labelClass = 'block text-sm font-semibold text-ink'
const inputClass =
  'min-h-11 w-full rounded-lg border border-border-strong bg-surface px-3 text-sm text-ink placeholder:text-ink-faint transition-colors hover:border-ink-muted focus-visible:outline-accent'

function blankExperience(): Experience {
  return { company: '', title: '', startDate: null, endDate: null, isCurrent: false, highlights: null, source: 'manual' }
}

/**
 * Work history editor. Entries arriving from the CV carry source "cv" and are
 * replaced on re-upload; as soon as any field is touched — or an entry is
 * added — it becomes manual and survives.
 */
export function ExperienceEditor({
  experiences,
  onChange,
}: {
  experiences: Experience[]
  onChange: (experiences: Experience[]) => void
}) {
  const update = (index: number, patch: Partial<Experience>) =>
    onChange(
      experiences.map(
        (e, i) => (i === index ? { ...e, ...patch, source: 'manual' as const } : e),
      ),
    )

  const remove = (index: number) => onChange(experiences.filter((_, i) => i !== index))
  const add = () => onChange([...experiences, blankExperience()])

  return (
    <div className="space-y-4">
      {experiences.length === 0 && (
        <p className="rounded-lg bg-sunken px-3 py-3 text-sm text-ink-muted">
          Upload a CV to extract your work history, or add your first role below.
        </p>
      )}

      <ul className="space-y-4">
        {experiences.map((exp, i) => (
          <li
            key={i}
            className="space-y-3 rounded-lg border border-border-subtle bg-sunken/50 p-4"
          >
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-bold uppercase tracking-wide text-ink-muted">
                Role {i + 1}
              </span>
              <div className="flex items-center gap-2">
                {exp.source !== 'manual' && (
                  <span className="text-xs font-semibold uppercase tracking-wide text-ink-faint">
                    from your CV
                  </span>
                )}
                <button
                  type="button"
                  onClick={() => remove(i)}
                  className="min-h-11 rounded-lg px-2 text-sm font-bold text-danger transition-colors hover:bg-danger-subtle"
                >
                  Remove
                </button>
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <label>
                <span className={labelClass}>Job title</span>
                <input
                  value={exp.title}
                  onChange={(e) => update(i, { title: e.target.value })}
                  placeholder="e.g. Logistics Coordinator"
                  className={inputClass}
                />
              </label>
              <label>
                <span className={labelClass}>Company</span>
                <input
                  value={exp.company}
                  onChange={(e) => update(i, { company: e.target.value })}
                  placeholder="e.g. TPS Terminal Services"
                  className={inputClass}
                />
              </label>
              <label>
                <span className={labelClass}>Start</span>
                <input
                  value={exp.startDate ?? ''}
                  onChange={(e) => update(i, { startDate: e.target.value || null })}
                  placeholder="Mar 2022"
                  className={inputClass}
                />
              </label>
              <label>
                <span className={labelClass}>End</span>
                <div className="flex items-end gap-2">
                  <input
                    value={exp.endDate ?? ''}
                    onChange={(e) => update(i, { endDate: e.target.value || null })}
                    placeholder="Present"
                    disabled={exp.isCurrent}
                    className={`${inputClass} disabled:cursor-not-allowed disabled:bg-sunken disabled:text-ink-faint`}
                  />
                </div>
              </label>
            </div>

            <label className="flex min-h-11 items-center gap-2 text-sm text-ink-body">
              <input
                type="checkbox"
                checked={exp.isCurrent}
                onChange={(e) => update(i, { isCurrent: e.target.checked, endDate: e.target.checked ? null : exp.endDate })}
                className="h-4 w-4 accent-[var(--color-accent)]"
              />
              I currently work here
            </label>

            <label>
              <span className={labelClass}>Highlights</span>
              <textarea
                value={exp.highlights ?? ''}
                onChange={(e) => update(i, { highlights: e.target.value || null })}
                rows={3}
                placeholder="A summary of what you did and the results."
                className={`${inputClass} resize-y`}
              />
            </label>
          </li>
        ))}
      </ul>

      <button
        type="button"
        onClick={add}
        className="min-h-11 rounded-lg border border-dashed border-border-strong px-4 text-sm font-bold text-accent transition-colors hover:border-accent hover:bg-accent-subtle"
      >
        + Add role
      </button>
    </div>
  )
}