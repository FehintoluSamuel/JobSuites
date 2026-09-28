import { useEffect, useMemo, useRef, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthContext'
import { dashboard, profile, roles, type ApiError, type CandidateProfile } from '../lib/api'
import {
  completenessChecks,
  computeCompleteness,
  emptyProfile,
  serializeForSave,
  toEditable,
  type Profile,
} from '../lib/profile'
import { AppShell } from '../components/AppShell'
import { Panel } from '../components/Panel'
import { CompletenessCard } from '../components/profile/CompletenessCard'
import { ProfilePhotos } from '../components/profile/ProfilePhotos'
import { SkillsEditor } from '../components/profile/SkillsEditor'
import { ExperienceEditor } from '../components/profile/ExperienceEditor'
import { PreferencesEditor } from '../components/profile/PreferencesEditor'
import {
  CertificationEditor,
  EducationEditor,
  LanguageEditor,
  LinksEditor,
} from '../components/profile/ListEditors'
import { useFileInput } from '../hooks/useFileInput'

const labelClass = 'block text-sm font-semibold text-ink'
const inputClass =
  'min-h-11 w-full rounded-md border border-border-strong bg-surface px-3 text-sm text-ink placeholder:text-ink-faint transition-colors hover:border-ink-muted focus-visible:outline-accent'

/** Completeness labels → the compartment that fixes them. */
const PANEL_IDS: Record<string, string> = {
  'Your name': 'panel-hero',
  'Contact email': 'panel-hero',
  Location: 'panel-hero',
  'A CV to work from': 'panel-cv',
  Skills: 'panel-skills',
  'Years of experience': 'panel-hero',
  'Target roles': 'panel-preferences',
  'Preferred states': 'panel-preferences',
  'Desired job types': 'panel-preferences',
}

/**
 * The profile sheet. Every feature lives in its own compartment; on desktop the
 * grid gives the columns deliberately different sizes; on mobile the DOM order
 * is the priority order — completeness first, then hero, skills, preferences,
 * experience, education, extras.
 */
export function ProfilePage() {
  const { user, isLoading } = useAuth()
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState<Profile | null>(null)
  const [dirty, setDirty] = useState(false)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<{ message: string; fields: string[] } | null>(null)
  const seededRef = useRef(false)

  const dash = useQuery({
    queryKey: ['dashboard'],
    queryFn: dashboard.get,
    enabled: Boolean(user),
    retry: false,
  })

  const roleTitles = useQuery({
    queryKey: ['roles-titles'],
    queryFn: roles.titles,
    enabled: Boolean(dash.data),
    retry: false,
  })

  useEffect(() => {
    if (!dash.data || seededRef.current) return
    seededRef.current = true
    setDraft(toEditable(dash.data.profile ?? emptyProfile()))
  }, [dash.data])

  const update = (patch: (p: Profile) => Profile) => {
    setDraft((prev) => (prev ? patch(prev) : prev))
    setDirty(true)
  }

  const save = useMutation({
    mutationFn: () => profile.update(serializeForSave(draft!)),
    onSuccess: (res) => {
      setDraft(toEditable(res.profile))
      setDirty(false)
      setSavedAt(new Date().toLocaleTimeString())
      setSaveError(null)
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
    onError: (e) => {
      const err = e as ApiError
      const fields = Object.values(err.fieldErrors ?? {}).flat()
      setSaveError({ message: err.message, fields })
    },
  })

  const checks = useMemo(() => completenessChecks(draft ?? {}), [draft])
  const percent = useMemo(() => computeCompleteness(draft ?? {}).percent, [draft])

  if (isLoading) {
    return (
      <AppShell>
        <p className="py-16 text-center text-sm text-ink-muted">Loading…</p>
      </AppShell>
    )
  }

  if (!user) return <Navigate to="/login" replace />

  if (dash.isLoading || (!dash.isError && dash.data && draft === null)) {
    return (
      <AppShell>
        <p className="py-16 text-center text-sm text-ink-muted">Loading…</p>
      </AppShell>
    )
  }

  if (dash.isError) {
    return (
      <AppShell>
        <div role="alert" className="rounded-md border border-danger bg-danger-subtle p-5">
          <h1 className="text-sm font-bold text-danger">We could not load your profile</h1>
          <p className="mt-1 text-ink-body">
            That is a connection problem, not a problem with your profile.
          </p>
          <button
            onClick={() => void dash.refetch()}
            className="mt-4 min-h-11 rounded-md bg-accent px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-accent-hover"
          >
            Try again
          </button>
        </div>
      </AppShell>
    )
  }

  if (!draft) {
    return (
      <AppShell>
        <p className="py-16 text-center text-sm text-ink-muted">
          No profile yet. Upload a CV from the dashboard to get started.
        </p>
      </AppShell>
    )
  }

  const onNavigate = (label: string) => {
    const id = PANEL_IDS[label]
    if (!id) return
    const el = document.getElementById(id)
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    el?.focus({ preventScroll: true })
  }

  return (
    <AppShell>
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start">
        {/* Left rail — hero, skills, experience, education. */}
        <div className="contents lg:flex lg:min-w-0 lg:flex-1 lg:flex-col lg:gap-4">
          <div className="order-2">
            <HeroPanel
              profile={draft}
              onUpdate={update}
              onMedia={(p) =>
                setDraft((prev) => (prev ? { ...prev, ...p } : prev))
              }
              onCvUploaded={(p) => {
                setDraft(toEditable(p))
                setDirty(false)
                setSavedAt(new Date().toLocaleTimeString())
                void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
              }}
            />
          </div>

          <div className="order-3">
            <Panel id="panel-skills" title="Skills">
              <SkillsEditor
                skills={draft.skills}
                onChange={(skills) => update((p) => ({ ...p, skills }))}
              />
            </Panel>
          </div>

          <div className="order-4">
            <Panel id="panel-experience" title="Work experience">
              <ExperienceEditor
                experiences={draft.experiences}
                onChange={(experiences) => update((p) => ({ ...p, experiences }))}
              />
            </Panel>
          </div>

          <div className="order-5">
            <Panel id="panel-education" title="Education">
              <EducationEditor value={draft.education} onChange={(education) => update((p) => ({ ...p, education }))} />
            </Panel>
          </div>
        </div>

        {/* Right rail — completeness, preferences, certifications, languages,
        links. One column on desktop so every panel keeps the same 1rem gap and
        none stretches to a neighbouring row's height. `contents` on mobile
        keeps the panels in the order classes set below. */}
        <div className="contents lg:flex lg:w-80 lg:shrink-0 lg:flex-col lg:gap-4">
          <div className="order-1">
            <CompletenessCard checks={checks} percent={percent} onNavigate={onNavigate} />
          </div>

          <div className="order-6">
            <Panel id="panel-preferences" title="Job preferences">
              <PreferencesEditor
                targetRoles={draft.targetRoles}
                preferredStates={draft.preferredStates}
                desiredJobTypes={draft.desiredJobTypes}
                desiredSalary={draft.desiredSalary}
                availability={draft.availability}
                roleSuggestions={roleTitles.data?.titles ?? []}
                onChange={(patch) => update((p) => ({ ...p, ...patch }))}
              />
            </Panel>
          </div>

          <div className="order-7">
            <Panel title="Certifications">
              <CertificationEditor value={draft.certifications} onChange={(certifications) => update((p) => ({ ...p, certifications }))} />
            </Panel>
          </div>

          <div className="order-8">
            <Panel title="Languages">
              <LanguageEditor value={draft.languages} onChange={(languages) => update((p) => ({ ...p, languages }))} />
            </Panel>
          </div>

          <div className="order-9">
            <Panel title="Links">
              <LinksEditor value={draft.links} onChange={(links) => update((p) => ({ ...p, links }))} />
            </Panel>
          </div>
        </div>
      </div>

      <SaveBar
        dirty={dirty}
        saving={save.isPending}
        savedAt={savedAt}
        error={saveError}
        onSave={() => save.mutate()}
        onDiscard={() => {
          setDraft(toEditable(dash.data?.profile ?? emptyProfile()))
          setDirty(false)
          setSaveError(null)
        }}
      />
    </AppShell>
  )
}

function HeroPanel({
  profile: p,
  onUpdate,
  onMedia,
  onCvUploaded,
}: {
  profile: Profile
  onUpdate: (patch: (p: Profile) => Profile) => void
  onMedia: (p: { profilePicture: string | null; bannerPicture: string | null }) => void
  onCvUploaded: (p: CandidateProfile) => void
}) {
  const [cvBusy, setCvBusy] = useState(false)
  const [cvError, setCvError] = useState<string | null>(null)
  const [cvDone, setCvDone] = useState<string | null>(null)
  const { trigger, inputProps } = useFileInput({
    accept: '.pdf,.doc,.docx,.txt',
    onFile: async (file) => {
      setCvBusy(true)
      setCvError(null)
      setCvDone(null)
      try {
        const res = await profile.uploadCv(file)
        onCvUploaded(res.profile)
        setCvDone(res.unchanged ? 'Already current.' : `Matched ${res.skillsFound} skills, ${res.experiencesFound} roles.`)
      } catch (e) {
        setCvError(e instanceof Error ? e.message : 'Could not read that CV.')
      } finally {
        setCvBusy(false)
      }
    },
  })

  return (
    <Panel id="panel-hero" title="About you">
      <div className="space-y-4">
        <ProfilePhotos
          bannerPicture={p.bannerPicture}
          profilePicture={p.profilePicture}
          photoConsentGiven={p.photoConsentGiven}
          onMedia={onMedia}
          onConsent={(photoConsentGiven) => onUpdate((d) => ({ ...d, photoConsentGiven }))}
        />

        <div>
          <label>
            <span className={labelClass}>Full name</span>
            <input
              value={p.fullName ?? ''}
              onChange={(e) => onUpdate((d) => ({ ...d, fullName: e.target.value || null }))}
              placeholder="Your name"
              className={inputClass}
            />
          </label>
        </div>

        <div>
          <label>
            <span className={labelClass}>Headline</span>
            <input
              value={p.headline ?? ''}
              onChange={(e) => onUpdate((d) => ({ ...d, headline: e.target.value || null }))}
              placeholder="e.g. Logistics & supply chain professional"
              className={inputClass}
            />
          </label>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <label>
            <span className={labelClass}>Location</span>
            <input
              value={p.location ?? ''}
              onChange={(e) => onUpdate((d) => ({ ...d, location: e.target.value || null }))}
              placeholder="City or state, e.g. Lagos"
              className={inputClass}
            />
          </label>
          <label>
            <span className={labelClass}>Years of experience</span>
            <input
              type="number"
              min={0}
              max={60}
              value={p.yearsExperience ?? ''}
              onChange={(e) =>
                onUpdate((d) => ({
                  ...d,
                  yearsExperience: e.target.value === '' ? null : Math.max(0, Number(e.target.value) || 0),
                }))
              }
              className={inputClass}
            />
          </label>
          <label>
            <span className={labelClass}>Contact email</span>
            <input
              type="email"
              value={p.email ?? ''}
              onChange={(e) => onUpdate((d) => ({ ...d, email: e.target.value || null }))}
              className={inputClass}
            />
          </label>
          <label>
            <span className={labelClass}>Phone</span>
            <input
              value={p.phone ?? ''}
              onChange={(e) => onUpdate((d) => ({ ...d, phone: e.target.value || null }))}
              placeholder="Optional"
              className={inputClass}
            />
          </label>
        </div>

        <label>
          <span className={labelClass}>Summary</span>
          <textarea
            value={p.summary ?? ''}
            onChange={(e) => onUpdate((d) => ({ ...d, summary: e.target.value || null }))}
            rows={4}
            placeholder="Two or three sentences about what you do best."
            className={`${inputClass} resize-y`}
          />
        </label>

        {/* CV — the completeness checklist points here (panel-cv). */}
        <div id="panel-cv" className="border-t border-border-subtle pt-4">
          <h3 className="text-sm font-bold uppercase tracking-wide text-ink">Your CV</h3>
          <p className="mt-1 text-sm text-ink-muted">
            {p.fileName
              ? `Working from ${p.fileName}.`
              : 'No CV uploaded yet. This is what brings your whole profile to life.'}
          </p>
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={trigger}
              disabled={cvBusy}
              className="min-h-11 rounded-md border border-border-strong bg-surface px-4 text-sm font-bold text-ink transition-colors hover:bg-canvas disabled:text-ink-faint"
            >
              {cvBusy ? 'Reading…' : p.fileName ? 'Re-upload' : 'Upload CV'}
            </button>
            <input {...inputProps} />
            {cvDone && <span className="text-sm font-semibold text-accent">{cvDone}</span>}
          </div>
          {cvError && (
            <p role="alert" className="mt-2 rounded-md border border-danger bg-danger-subtle px-3 py-2 text-sm font-medium text-danger">
              {cvError}
            </p>
          )}
        </div>
      </div>
    </Panel>
  )
}

function SaveBar({
  dirty,
  saving,
  savedAt,
  error,
  onSave,
  onDiscard,
}: {
  dirty: boolean
  saving: boolean
  savedAt: string | null
  error: { message: string; fields: string[] } | null
  onSave: () => void
  onDiscard: () => void
}) {
  return (
    <div className="sticky bottom-3 z-10 mt-6">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border-strong bg-surface px-4 py-3">
        <div className="flex min-w-0 flex-wrap items-center gap-3 text-sm">
          {saving ? (
            <span className="text-ink-muted">Saving…</span>
          ) : dirty ? (
            <span className="flex items-center gap-2 font-bold text-accent">
              <span aria-hidden="true" className="h-2 w-2 rounded-full bg-accent" />
              Unsaved changes
            </span>
          ) : (
            <span className="text-ink-muted">All changes saved{savedAt ? ` · ${savedAt}` : ''}</span>
          )}
          {error && (
            <span role="alert" className="text-danger">
              {error.message}
              {error.fields.length > 0 && ` (${error.fields.join(', ')})`}
            </span>
          )}
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onDiscard}
            disabled={!dirty || saving}
            className="min-h-11 rounded-md border border-border-strong bg-surface px-4 text-sm font-bold text-ink transition-colors hover:bg-canvas disabled:cursor-not-allowed disabled:text-ink-faint"
          >
            Discard
          </button>
          <button
            type="button"
            onClick={onSave}
            disabled={!dirty || saving}
            className="min-h-11 rounded-md bg-accent px-5 text-sm font-bold text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
          >
            {saving ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </div>
    </div>
  )
}