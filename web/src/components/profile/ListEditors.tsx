import type { ProfileCertification, ProfileEducation, ProfileLanguage, ProfileLink } from '../../lib/api'
import { LANGUAGE_PROFICIENCIES, LINK_LABELS } from '../../lib/profile'

const labelClass = 'block text-sm font-semibold text-ink'
const inputClass =
  'min-h-11 w-full rounded-lg border border-border-strong bg-surface px-3 text-sm text-ink placeholder:text-ink-faint transition-colors hover:border-ink-muted focus-visible:outline-accent'

function RowWrap({
  index,
  hint,
  onRemove,
  children,
}: {
  index: number
  hint?: string
  onRemove: () => void
  children: React.ReactNode
}) {
  return (
    <li className="space-y-3 rounded-lg border border-border-subtle bg-sunken/50 p-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-xs font-bold uppercase tracking-wide text-ink-muted">
            Entry {index + 1}
          </span>
          {hint && <span className="text-xs font-semibold uppercase tracking-wide text-ink-faint">{hint}</span>}
        </div>
        <button
          type="button"
          onClick={onRemove}
          className="min-h-11 rounded-lg px-2 text-sm font-bold text-danger transition-colors hover:bg-danger-subtle"
        >
          Remove
        </button>
      </div>
      {children}
    </li>
  )
}

function EmptyNote({ children }: { children: React.ReactNode }) {
  return <p className="rounded-lg bg-sunken px-3 py-3 text-sm text-ink-muted">{children}</p>
}

export function EducationEditor({
  value,
  onChange,
}: {
  value: ProfileEducation[]
  onChange: (value: ProfileEducation[]) => void
}) {
  const update = (index: number, patch: Partial<ProfileEducation>) =>
    onChange(
      value.map((e, i) =>
        i === index ? { ...e, ...patch, source: 'manual' as const } : e,
      ),
    )
  const year = (v: number | null) => v ?? ''

  return (
    <div className="space-y-4">
      {value.length === 0 && <EmptyNote>Add schools so employers can see your credentials.</EmptyNote>}
      <ul className="space-y-4">
        {value.map((e, i) => (
          <RowWrap
            key={i}
            index={i}
            hint={e.source === 'cv' ? 'From your CV' : undefined}
            onRemove={() => onChange(value.filter((_, x) => x !== i))}
          >
            <div className="grid gap-3 sm:grid-cols-2">
              <label>
                <span className={labelClass}>School</span>
                <input value={e.school ?? ''} onChange={(x) => update(i, { school: x.target.value || null })} placeholder="e.g. University of Lagos" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Degree</span>
                <input value={e.degree ?? ''} onChange={(x) => update(i, { degree: x.target.value || null })} placeholder="e.g. B.Sc. Computer Science" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Field of study</span>
                <input value={e.fieldOfStudy ?? ''} onChange={(x) => update(i, { fieldOfStudy: x.target.value || null })} placeholder="e.g. Software Engineering" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Details</span>
                <input value={e.details ?? ''} onChange={(x) => update(i, { details: x.target.value || null })} placeholder="Optional distinctions" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Start year</span>
                <input type="number" min={1960} max={2030} value={year(e.startYear)} onChange={(x) => update(i, { startYear: x.target.value ? Number(x.target.value) : null })} className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>End year</span>
                <input type="number" min={1960} max={2030} value={year(e.endYear)} onChange={(x) => update(i, { endYear: x.target.value ? Number(x.target.value) : null })} className={inputClass} />
              </label>
            </div>
          </RowWrap>
        ))}
      </ul>
      <AddButton
        onClick={() =>
          onChange([
            ...value,
            { school: null, degree: null, fieldOfStudy: null, startYear: null, endYear: null, details: null, source: 'manual' },
          ])
        }
      />
      <p className="text-xs text-ink-muted">
        Entries marked “from your CV” refresh when you re-upload. Anything you edit or add stays as you set it.
      </p>
    </div>
  )
}

export function CertificationEditor({
  value,
  onChange,
}: {
  value: ProfileCertification[]
  onChange: (value: ProfileCertification[]) => void
}) {
  const update = (index: number, patch: Partial<ProfileCertification>) =>
    onChange(
      value.map((e, i) =>
        i === index ? { ...e, ...patch, source: 'manual' as const } : e,
      ),
    )

  return (
    <div className="space-y-4">
      {value.length === 0 && <EmptyNote>Certifications strengthen matching evidence notes.</EmptyNote>}
      <ul className="space-y-4">
        {value.map((c, i) => (
          <RowWrap
            key={i}
            index={i}
            hint={c.source === 'cv' ? 'From your CV' : undefined}
            onRemove={() => onChange(value.filter((_, x) => x !== i))}
          >
            <div className="grid gap-3 sm:grid-cols-3">
              <label>
                <span className={labelClass}>Name</span>
                <input value={c.name} onChange={(x) => update(i, { name: x.target.value })} placeholder="e.g. HSE Level 3" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Issuer</span>
                <input value={c.issuer ?? ''} onChange={(x) => update(i, { issuer: x.target.value || null })} placeholder="e.g. NEBOSH" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Year</span>
                <input type="number" min={1980} max={2030} value={c.year ?? ''} onChange={(x) => update(i, { year: x.target.value ? Number(x.target.value) : null })} className={inputClass} />
              </label>
            </div>
          </RowWrap>
        ))}
      </ul>
      <AddButton onClick={() => onChange([...value, { name: '', issuer: null, year: null, source: 'manual' }])} />
      <p className="text-xs text-ink-muted">
        Entries marked “from your CV” refresh when you re-upload. Anything you edit or add stays as you set it.
      </p>
    </div>
  )
}

export function LanguageEditor({
  value,
  onChange,
}: {
  value: ProfileLanguage[]
  onChange: (value: ProfileLanguage[]) => void
}) {
  const update = (index: number, patch: Partial<ProfileLanguage>) =>
    onChange(
      value.map((e, i) =>
        i === index ? { ...e, ...patch, source: 'manual' as const } : e,
      ),
    )

  return (
    <div className="space-y-4">
      {value.length === 0 && <EmptyNote>List the languages you work in.</EmptyNote>}
      <ul className="space-y-4">
        {value.map((l, i) => (
          <RowWrap
            key={i}
            index={i}
            hint={l.source === 'cv' ? 'From your CV' : undefined}
            onRemove={() => onChange(value.filter((_, x) => x !== i))}
          >
            <div className="grid gap-3 sm:grid-cols-2">
              <label>
                <span className={labelClass}>Language</span>
                <input value={l.name} onChange={(x) => update(i, { name: x.target.value })} placeholder="e.g. English" className={inputClass} />
              </label>
              <label>
                <span className={labelClass}>Proficiency</span>
                <select
                  value={l.proficiency}
                  onChange={(x) => update(i, { proficiency: x.target.value })}
                  className={inputClass}
                >
                  {LANGUAGE_PROFICIENCIES.map((p) => (
                    <option key={p} value={p}>
                      {p}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </RowWrap>
        ))}
      </ul>
      <AddButton onClick={() => onChange([...value, { name: '', proficiency: 'professional', source: 'manual' }])} />
      <p className="text-xs text-ink-muted">
        Entries marked “from your CV” refresh when you re-upload. Anything you edit or add stays as you set it.
      </p>
    </div>
  )
}

export function LinksEditor({
  value,
  onChange,
}: {
  value: ProfileLink[]
  onChange: (value: ProfileLink[]) => void
}) {
  const update = (index: number, patch: Partial<ProfileLink>) =>
    onChange(
      value.map((e, i) =>
        i === index ? { ...e, ...patch, source: 'manual' as const } : e,
      ),
    )

  return (
    <div className="space-y-4">
      {value.length === 0 && <EmptyNote>Here you keep professional links.</EmptyNote>}
      <ul className="space-y-4">
        {value.map((l, i) => (
          <RowWrap
            key={i}
            index={i}
            hint={l.source === 'cv' ? 'From your CV' : undefined}
            onRemove={() => onChange(value.filter((_, x) => x !== i))}
          >
            <div className="grid gap-3 sm:grid-cols-[10rem_1fr]">
              <label>
                <span className={labelClass}>Type</span>
                <select value={l.label} onChange={(x) => update(i, { label: x.target.value })} className={inputClass}>
                  {LINK_LABELS.map((label) => (
                    <option key={label} value={label}>
                      {label}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                <span className={labelClass}>URL</span>
                <input
                  type="url"
                  value={l.url}
                  onChange={(x) => update(i, { url: x.target.value })}
                  placeholder="https://linkedin.com/in/you"
                  className={inputClass}
                />
              </label>
            </div>
          </RowWrap>
        ))}
      </ul>
      <AddButton onClick={() => onChange([...value, { label: 'LinkedIn', url: '', source: 'manual' }])} />
      <p className="text-xs text-ink-muted">
        Entries marked “from your CV” refresh when you re-upload. Anything you edit or add stays as you set it.
      </p>
    </div>
  )
}

function AddButton({ onClick, children = '+ Add' }: { onClick: () => void; children?: React.ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="min-h-11 rounded-lg border border-dashed border-border-strong px-4 text-sm font-bold text-accent transition-colors hover:border-accent hover:bg-accent-subtle"
    >
      {children}
    </button>
  )
}