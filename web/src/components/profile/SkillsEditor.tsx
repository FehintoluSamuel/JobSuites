import { useState } from 'react'
import type { CandidateSkill } from '../../lib/api'

const inputClass =
  'min-h-11 w-full rounded-md border border-border-strong bg-surface px-3 text-sm text-ink placeholder:text-ink-faint transition-colors hover:border-ink-muted focus-visible:outline-accent'

/**
 * The correction surface for extraction. Every CV-derived skill starts unedited
 * (source "cv"); the moment the user touches a field it becomes "manual", which
 * is what makes it survive a CV re-upload server-side. The UI says so in plain
 * language rather than hiding the contract.
 */
export function SkillsEditor({
  skills,
  onChange,
}: {
  skills: CandidateSkill[]
  onChange: (skills: CandidateSkill[]) => void
}) {
  const [draft, setDraft] = useState('')

  const add = () => {
    const name = draft.trim()
    if (!name) return
    if (skills.some((s) => s.name.toLowerCase() === name.toLowerCase())) {
      setDraft('')
      return
    }
    onChange([...skills, { name, normalized: name.toLowerCase(), years: 0, isCore: false, source: 'manual' }])
    setDraft('')
  }

  const update = (index: number, patch: Partial<CandidateSkill>) => {
    onChange(
      skills.map((s, i) =>
        i === index
          ? { ...s, ...patch, source: 'manual' as const }
          : s,
      ),
    )
  }

  const remove = (index: number) => onChange(skills.filter((_, i) => i !== index))

  return (
    <div className="space-y-3">
      {skills.length === 0 && (
        <p className="rounded-md bg-canvas px-3 py-3 text-sm text-ink-muted">
          No skills yet. Upload a CV to extract them, or add your first one below.
        </p>
      )}

      <ul className="space-y-2">
        {skills.map((skill, i) => (
          <li
            key={`${skill.name}-${i}`}
            className="flex flex-wrap items-center gap-2 rounded-md border border-border-subtle bg-canvas/50 px-3 py-2"
          >
            <input
              value={skill.name}
              onChange={(e) => update(i, { name: e.target.value, normalized: e.target.value.toLowerCase().trim() })}
              aria-label={`Skill ${i + 1} name`}
              className={`${inputClass} w-40 min-w-0 flex-1`}
            />
            <input
              type="number"
              min={0}
              max={60}
              value={skill.years}
              onChange={(e) => update(i, { years: Math.max(0, Number(e.target.value) || 0) })}
              aria-label={`Years of ${skill.name}`}
              className={`${inputClass} w-20`}
            />
            <label className="flex min-h-11 items-center gap-2 px-1 text-sm text-ink-body">
              <input
                type="checkbox"
                checked={skill.isCore}
                onChange={(e) => update(i, { isCore: e.target.checked })}
                className="h-4 w-4 accent-[var(--color-accent)]"
              />
              Core
            </label>
            <span className="hidden text-xs font-semibold uppercase tracking-wide text-ink-faint sm:inline">
              {skill.source === 'manual' ? 'added by you' : 'from your CV'}
            </span>
            <button
              type="button"
              onClick={() => remove(i)}
              aria-label={`Remove ${skill.name}`}
              className="min-h-11 rounded-md px-2 text-sm font-bold text-danger transition-colors hover:bg-danger-subtle"
            >
              Remove
            </button>
          </li>
        ))}
      </ul>

      <div className="flex flex-wrap items-end gap-2">
        <label className="flex-1">
          <span className="sr-only">Add a skill</span>
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                add()
              }
            }}
            placeholder="Add a skill, e.g. Power BI"
            className={inputClass}
          />
        </label>
        <button
          type="button"
          onClick={add}
          className="min-h-11 rounded-md border border-border-strong bg-surface px-4 text-sm font-bold text-ink transition-colors hover:bg-canvas"
        >
          Add
        </button>
      </div>

      <p className="text-xs text-ink-muted">
        Skills marked “from your CV” refresh when you re-upload. Anything you
        edit or add stays as you set it.
      </p>
    </div>
  )
}