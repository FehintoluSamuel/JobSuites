import { useRef, type ChangeEvent, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthContext'
import { dashboard, profile, type MatchedRole, type DashboardResponse } from '../lib/api'
import { AppShell } from '../components/AppShell'

const tierStyles: Record<string, { badge: string; score: string }> = {
  Strong: {
    badge: 'bg-success-subtle text-success',
    score: 'text-success',
  },
  Good: {
    badge: 'bg-accent-subtle text-accent',
    score: 'text-accent',
  },
  Possible: {
    badge: 'bg-canvas text-ink-body border border-border-strong',
    score: 'text-ink-body',
  },
  Stretch: {
    badge: 'bg-canvas text-ink-muted border border-border-subtle',
    score: 'text-ink-muted',
  },
}

function TierBadge({ tier }: { tier: string }) {
  const style = tierStyles[tier] ?? tierStyles.Stretch
  return (
    <span
      className={`inline-flex min-h-7 items-center rounded-full px-3 text-xs font-bold uppercase tracking-wide ${style.badge}`}
    >
      {tier}
    </span>
  )
}

function formatDate(value: string | null): string | null {
  if (!value) return null
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return null
  return d.toLocaleDateString('en-NG', { day: 'numeric', month: 'short', year: 'numeric' })
}

function initials(name: string | null): string {
  const parts = (name ?? 'You').split(/\s+/).filter(Boolean).slice(0, 2)
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || 'CV'
}

export function DashboardPage() {
  const { user, isLoading } = useAuth()
  const queryClient = useQueryClient()

  const { data, isLoading: loadingDashboard, isError, refetch } = useQuery({
    queryKey: ['dashboard'],
    queryFn: dashboard.get,
    enabled: Boolean(user),
  })

  if (isLoading || loadingDashboard) {
    return (
      <AppShell>
        <p className="text-ink-muted" role="status">
          Loading your dashboard…
        </p>
      </AppShell>
    )
  }

  if (!user) return <Navigate to="/login" replace />
  if (isError || !data) {
    return (
      <AppShell>
        <div role="alert" className="rounded-md border border-danger bg-danger-subtle p-5">
          <h1 className="text-sm font-bold text-danger">We could not load your dashboard</h1>
          <p className="mt-1 text-ink-body">
            That is a connection problem, not a problem with your matches.
          </p>
          <button
            onClick={() => void refetch()}
            className="mt-4 min-h-11 rounded-md bg-accent px-4 py-2 text-sm font-bold text-white transition-colors hover:bg-accent-hover"
          >
            Try again
          </button>
        </div>
      </AppShell>
    )
  }

  return (
    <AppShell>
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-extrabold tracking-tight text-ink">Your dashboard</h1>
          <p className="text-ink-muted">
            Signed in as <span className="font-medium text-ink-body">{user.email}</span>
          </p>
        </div>
      </div>

      {data.profile ? (
        <div className="gap-4 lg:grid lg:grid-cols-[minmax(0,16rem)_minmax(0,1fr)_minmax(0,18rem)] lg:items-start">
          {/* Left rail — profile summary: banner, photo, identity, CV actions. */}
          <aside aria-label="Your profile">
            <ProfileSummary profile={data.profile} onUploaded={invalidate} />
          </aside>

          {/* Middle — the dashboard itself. */}
          <main className="space-y-6">
            <CompletionBanner profile={data.profile} />
            <StatsRow summary={data.summary} />
            <MatchesSection matches={data.matches} />
          </main>

          {/* Right rail — reserved for upcoming features. */}
          <aside aria-hidden="true" className="hidden lg:min-h-72 lg:block" />
        </div>
      ) : (
        <div className="space-y-6">
          <UploadHero hasIngestedRoles={data.hasIngestedRoles} onUploaded={invalidate} />
        </div>
      )}
    </AppShell>
  )

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }
}

function CompletionBanner({ profile: p }: { profile: NonNullable<DashboardResponse['profile']> }) {
  const c = p.completeness
  if (c.requiredMet >= c.requiredTotal) return null

  return (
    <Link
      to="/profile"
      className="block rounded-lg border border-accent/30 bg-accent-tint p-4 transition-colors hover:border-accent"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-1">
          <p className="font-bold text-ink">Your profile is {c.percent}% complete</p>
          <p className="text-sm text-ink-muted">
            {c.requiredTotal - c.requiredMet} core{' '}
            {c.requiredTotal - c.requiredMet === 1 ? 'item is' : 'items are'} still missing.
            Filling them in gets better matches.
          </p>
        </div>
        <span className="text-sm font-bold text-accent">Edit profile →</span>
      </div>
      <div
        role="progressbar"
        aria-valuenow={c.percent}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="Profile completeness"
        className="mt-3 h-1.5 overflow-hidden rounded-full bg-accent/20"
      >
        <div className="h-full rounded-full bg-accent" style={{ width: `${Math.max(c.percent, 2)}%` }} />
      </div>
    </Link>
  )
}

function UploadHero({
  hasIngestedRoles,
  onUploaded,
}: {
  hasIngestedRoles: boolean
  onUploaded: () => void
}) {
  const upload = useUploadCv(onUploaded)
  const inputRef = useRef<HTMLInputElement>(null)

  const pick = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) upload.mutate(file)
    e.target.value = ''
  }

  return (
    <section
      aria-labelledby="upload-title"
      className="rounded-lg border border-border-strong bg-surface p-8"
    >
      <h2 id="upload-title" className="text-xl font-extrabold tracking-tight text-ink">
        Upload your CV
      </h2>
      <p className="mt-2 max-w-2xl text-ink-body">
        Match your CV against live roles from MyJobMag. Every score is explained:
        you see the facts it came from and the source posting it points to, never
        an unexplained number.
      </p>

      {!hasIngestedRoles && (
        <p className="mt-3 rounded-md bg-canvas px-4 py-3 text-sm text-ink-muted">
          We are still filling the role feed. Upload your CV now and your matches
          will appear here the moment they are ready.
        </p>
      )}

      <form
        onSubmit={(e: FormEvent) => {
          e.preventDefault()
          inputRef.current?.click()
        }}
      >
        <label
          className="relative mt-6 flex max-w-md cursor-pointer items-center justify-center rounded-md min-h-14 border-2 border-dashed border-border-strong bg-canvas px-4 text-sm font-bold text-accent transition-colors hover:border-accent hover:bg-accent-subtle has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-accent"
        >
          <span>{upload.isPending ? 'Working…' : 'Choose your CV'}</span>
          <input
            ref={inputRef}
            type="file"
            name="cv"
            accept=".pdf,.docx,.txt"
            className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
            aria-label="Choose your CV file (PDF, DOCX or TXT)"
            disabled={upload.isPending}
            onChange={pick}
          />
        </label>
      </form>

      <p className="mt-3 text-sm text-ink-muted">
        PDF, DOCX or text up to 10 MB. Your CV is not stored on any server; only
        the facts we read from it are saved.
      </p>

      {upload.error && (
        <p role="alert" className="mt-4 rounded-md border border-danger bg-danger-subtle px-4 py-3 text-sm font-medium text-danger">
          {upload.error.message}
        </p>
      )}

      <h3 className="mt-8 text-sm font-bold uppercase tracking-wide text-ink-muted">What happens next</h3>
      <ol className="mt-3 grid gap-4 sm:grid-cols-3">
        {[
          ['We read your CV', 'Skills, experience, qualifications and location are extracted, nothing is invented.'],
          ['We score live roles', 'Each role is scored against your profile with the reasons attached to the score.'],
          ['You apply with an edge', 'See what you match, what to fix, and apply straight to the source posting.'],
        ].map(([title, body], i) => (
          <li key={title} className="rounded-md border border-border-subtle bg-surface p-4">
            <span className="text-xs font-bold text-accent">Step {i + 1}</span>
            <p className="mt-1 text-sm font-bold text-ink">{title}</p>
            <p className="mt-1 text-sm text-ink-muted">{body}</p>
          </li>
        ))}
      </ol>
    </section>
  )
}

function ProfileSummary({
  profile: p,
  onUploaded,
}: {
  profile: NonNullable<DashboardResponse['profile']>
  onUploaded: () => void
}) {
  const upload = useUploadCv(onUploaded)
  const remove = useMutation({ mutationFn: profile.remove, onSuccess: onUploaded })
  const inputRef = useRef<HTMLInputElement>(null)

  const coreSkills = p.skills.filter((s) => s.isCore).slice(0, 5)
  const moreSkills = p.skills.length - coreSkills.length
  const bannerUrl = profile.mediaUrl(p.bannerPicture)
  const photoUrl = profile.mediaUrl(p.profilePicture)

  const pick = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) upload.mutate(file)
    e.target.value = ''
  }

  return (
    <section aria-labelledby="profile-title" className="overflow-hidden rounded-lg border border-border-strong bg-surface">
      {bannerUrl ? (
        <img src={bannerUrl} alt="" className="h-24 w-full object-cover" />
      ) : (
        <div aria-hidden="true" className="h-24 w-full bg-accent-tint" />
      )}

      <div className="px-5 pb-5">
        <div className="-mt-10 flex items-end">
          {photoUrl ? (
            <img
              src={photoUrl}
              alt="Profile photo"
              className="h-20 w-20 rounded-full border-4 border-surface object-cover shadow"
            />
          ) : (
            <div
              aria-hidden="true"
              className="flex h-20 w-20 items-center justify-center rounded-full border-4 border-surface bg-brand text-lg font-extrabold text-white shadow"
            >
              {initials(p.fullName)}
            </div>
          )}
        </div>

        <h2 id="profile-title" className="mt-3 text-lg font-extrabold tracking-tight text-ink">
          {p.fullName ?? 'Your profile'}
        </h2>
        <p className="truncate text-sm text-ink-body">{p.headline ?? p.summary ?? 'CV uploaded'}</p>
        <p className="mt-1 text-sm text-ink-muted">
          {[p.location, p.yearsExperience != null ? `${p.yearsExperience} yrs experience` : null]
            .filter(Boolean)
            .join(' · ') || 'Profile ready'}
        </p>

        <Link
          to="/profile"
          className="mt-4 inline-flex min-h-11 w-full items-center justify-center rounded-md bg-accent px-4 text-sm font-bold text-white transition-colors hover:bg-accent-hover"
        >
          Edit profile
        </Link>

        <div className="mt-2 grid grid-cols-[minmax(0,1fr)_auto] gap-2">
          <label className="relative inline-flex min-h-11 cursor-pointer items-center justify-center rounded-md border border-border-strong bg-surface px-3 text-sm font-semibold text-ink transition-colors hover:bg-canvas has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-accent">
            <span>{upload.isPending ? 'Uploading…' : 'Replace CV'}</span>
            <input
              ref={inputRef}
              type="file"
              name="cv"
              accept=".pdf,.docx,.txt"
              className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
              aria-label="Replace CV file (PDF, DOCX or TXT)"
              disabled={upload.isPending}
              onChange={pick}
            />
          </label>
          <button
            onClick={() => remove.mutate()}
            disabled={remove.isPending}
            className="min-h-11 rounded-md border border-border-strong bg-surface px-3 text-sm font-semibold text-ink transition-colors hover:bg-canvas"
          >
            Remove
          </button>
        </div>

        {upload.error && (
          <p role="alert" className="mt-4 rounded-md border border-danger bg-danger-subtle px-3 py-2 text-sm font-medium text-danger">
            {upload.error.message}
          </p>
        )}
        {remove.error && (
          <p role="alert" className="mt-4 rounded-md border border-danger bg-danger-subtle px-3 py-2 text-sm font-medium text-danger">
            {remove.error.message}
          </p>
        )}

        {coreSkills.length > 0 && (
          <ul className="mt-4 flex flex-wrap items-center gap-1.5 border-t border-border-subtle pt-4">
            {coreSkills.map((s) => (
              <li
                key={s.name}
                className="inline-flex min-h-7 items-center gap-1 rounded-full bg-accent-subtle px-2.5 text-xs font-semibold text-accent"
              >
                {s.name}
                {s.years > 0 && <span className="text-ink-muted">{s.years}y</span>}
              </li>
            ))}
            {moreSkills > 0 && (
              <li className="text-xs text-ink-muted">+{moreSkills} more from your CV</li>
            )}
          </ul>
        )}
      </div>
    </section>
  )
}

function StatsRow({ summary }: { summary: DashboardResponse['summary'] }) {
  const tiles = [
    { label: 'Strong match', value: summary.strongMatches, tone: 'text-success' },
    { label: 'Good match', value: summary.goodMatches, tone: 'text-accent' },
    { label: 'Possible match', value: summary.possibleMatches, tone: 'text-ink-body' },
    { label: 'Stretch', value: summary.stretchMatches, tone: 'text-ink-muted' },
  ]

  return (
    <section aria-label="Match summary by tier">
      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {tiles.map((t) => (
          <div key={t.label} className="rounded-lg border border-border-subtle bg-surface p-4">
            <dt className="text-sm text-ink-muted">{t.label}</dt>
            <dd className={`tabular mt-1 text-3xl font-extrabold tracking-tight ${t.tone}`}>
              {t.value}
            </dd>
          </div>
        ))}
      </dl>
      <p className="mt-3 text-sm text-ink-muted">
        {summary.rolesConsidered === 0
          ? 'No roles have been scored against your CV yet.'
          : `Scored against ${summary.rolesConsidered} of ${summary.rolesIngested} live roles.`}
      </p>
    </section>
  )
}

function MatchesSection({ matches }: { matches: MatchedRole[] }) {
  if (matches.length === 0) {
    return (
      <section aria-labelledby="matches-empty" className="rounded-lg border border-dashed border-border-strong bg-surface p-6">
        <h2 id="matches-empty" className="text-sm font-bold uppercase tracking-wide text-ink-muted">
          Matched roles
        </h2>
        <p className="mt-2 text-ink-body">
          Your profile is saved but no roles matched yet. Check back after the next
          role feed arrives, and your scores re-run automatically.
        </p>
      </section>
    )
  }

  return (
    <section aria-labelledby="matches-title">
      <div className="flex items-baseline justify-between">
        <h2 id="matches-title" className="text-lg font-extrabold tracking-tight text-ink">
          Matched roles
        </h2>
        <span className="text-sm text-ink-muted">{matches.length} ranked by fit</span>
      </div>
      <ul className="mt-4 space-y-4">
        {matches.map((m) => (
          <MatchCard key={m.roleId} match={m} />
        ))}
      </ul>
    </section>
  )
}

function MatchCard({ match: m }: { match: MatchedRole }) {
  const style = tierStyles[m.tier] ?? tierStyles.Stretch
  const applyUrl = m.postings[0]?.url
  const otherPostings = m.postings.length - 1

  return (
    <li className="rounded-lg border border-border-strong bg-surface">
      <article className="p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0 space-y-1">
            <h3 className="text-lg font-extrabold tracking-tight text-ink">{m.title}</h3>
            <p className="text-ink-body">{m.company}</p>
            <p className="text-sm text-ink-muted">
              {[
                m.states.length > 0 ? m.states.join(', ') : null,
                m.salaryEstimate || null,
                formatDeadline(m.deadlineAt),
                m.postingCount > 1 ? `${m.postingCount} postings` : null,
              ]
                .filter(Boolean)
                .join(' · ')}
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-3">
            <span className={`tabular text-4xl font-extrabold tracking-tight ${style.score}`}>
              {m.score}
            </span>
            <TierBadge tier={m.tier} />
          </div>
        </div>

        {m.evidence.length > 0 && (
          <div className="mt-5">
            <h4 className="text-sm font-bold uppercase tracking-wide text-ink-muted">Why it matches</h4>
            <ul className="mt-2 space-y-2">
              {m.evidence.slice(0, 4).map((e, i) => (
                <li key={i} className="flex items-start gap-2 text-sm text-ink-body">
                  <span aria-hidden="true" className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-success" />
                  <span>
                    <span className="font-semibold text-ink">{e.label}: </span>
                    {e.detail}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {m.gaps.length > 0 && (
          <div className="mt-4 rounded-md border border-border-subtle bg-canvas p-4">
            <h4 className="text-sm font-bold uppercase tracking-wide text-ink-muted">Before you apply</h4>
            <ul className="mt-2 space-y-1.5">
              {m.gaps.slice(0, 3).map((g, i) => (
                <li key={i} className="flex items-start gap-2 text-sm text-ink-body">
                  <span aria-hidden="true" className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-ink-faint" />
                  {g}
                </li>
              ))}
            </ul>
          </div>
        )}

        {m.roleSkills.length > 0 && (
          <ul className="mt-4 flex flex-wrap items-center gap-2">
            {m.roleSkills.slice(0, 8).map((s) => (
              <li key={s} className="rounded-full bg-canvas px-3 py-1 text-xs font-semibold text-ink-body">
                {s}
              </li>
            ))}
          </ul>
        )}

        <div className="mt-5 flex flex-wrap items-center gap-3 border-t border-border-subtle pt-4">
          {applyUrl && (
            <a
              href={applyUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex min-h-11 items-center rounded-md bg-accent px-4 text-sm font-bold text-white transition-colors hover:bg-accent-hover"
            >
              Apply on {m.postings[0]?.source}
            </a>
          )}
          {otherPostings > 0 && (
            <details className="text-sm">
              <summary className="min-h-11 cursor-pointer list-none px-1 font-semibold text-accent">
                {otherPostings} more posting{otherPostings > 1 ? 's' : ''}
              </summary>
              <ul className="mt-2 space-y-1 rounded-md border border-border-subtle bg-canvas p-3">
                {m.postings.map((p) => (
                  <li key={p.id}>
                    <a href={p.url} target="_blank" rel="noopener noreferrer" className="text-sm font-medium text-accent underline underline-offset-2">
                      {[p.location, p.state].filter(Boolean).join(', ') || p.url}
                    </a>
                  </li>
                ))}
              </ul>
            </details>
          )}
        </div>
      </article>
    </li>
  )
}

function formatDeadline(deadlineAt: string | null): string | null {
  const d = formatDate(deadlineAt)
  return d ? `Apply by ${d}` : null
}

function useUploadCv(onUploaded: () => void) {
  return useMutation({
    mutationFn: profile.uploadCv,
    onSuccess: onUploaded,
  })
}