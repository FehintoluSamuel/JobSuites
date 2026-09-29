import { useRef, type ChangeEvent, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthContext'
import {
  dashboard,
  applications as applicationsApi,
  profile as profileApi,
  type MatchedRole,
  type DashboardResponse,
  type IngestHealth,
  type SourceHealth,
} from '../lib/api'
import { AppColumns, AppShell, Card } from '../components/AppShell'
import {
  AlertIcon,
  BoardIcon,
  CheckIcon,
  MapPinIcon,
  UploadIcon,
} from '../components/icons'

const tierStyles: Record<string, { badge: string; score: string; dot: string }> = {
  Strong: {
    badge: 'bg-success-subtle text-success',
    score: 'text-success',
    dot: 'bg-success',
  },
  Good: {
    badge: 'bg-accent-subtle text-accent',
    score: 'text-accent',
    dot: 'bg-accent',
  },
  Possible: {
    badge: 'bg-sunken text-ink-body',
    score: 'text-ink-body',
    dot: 'bg-ink-muted',
  },
  Stretch: {
    badge: 'bg-sunken text-ink-muted',
    score: 'text-ink-muted',
    dot: 'bg-ink-faint',
  },
}

function TierBadge({ tier }: { tier: string }) {
  const style = tierStyles[tier] ?? tierStyles.Stretch
  return (
    <span
      className={`inline-flex min-h-6 items-center rounded-full px-2.5 text-xs font-semibold ${style.badge}`}
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

function formatDeadline(deadlineAt: string | null): string | null {
  const d = formatDate(deadlineAt)
  return d ? `Apply by ${d}` : null
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
        <div role="alert" className="rounded-lg border border-danger bg-danger-subtle p-5">
          <h1 className="text-sm font-bold text-danger">We could not load your dashboard</h1>
          <p className="mt-1 text-ink-body">
            That is a connection problem, not a problem with your matches.
          </p>
          <button
            onClick={() => void refetch()}
            className="mt-4 min-h-11 rounded-full bg-accent px-5 text-sm font-semibold text-white transition-colors hover:bg-accent-hover"
          >
            Try again
          </button>
        </div>
      </AppShell>
    )
  }

  if (!data.profile) {
    return (
      <AppShell>
        <AppColumns
          center={<UploadHero hasIngestedRoles={data.hasIngestedRoles} onUploaded={invalidate} />}
          right={
            <>
              <IdentityRail user={user} profile={null} hasProfile={false} />
              <FeedFooter summary={data.summary} ingest={data.ingest} />
            </>
          }
        />
      </AppShell>
    )
  }

  return (
    <AppShell>
      <AppColumns
        center={
          <>
            <h1 className="sr-only">Your dashboard</h1>
            <CompletionBanner profile={data.profile} />
            <FeedHealthBanner ingest={data.ingest} />
            <StatsRow summary={data.summary} />
            <MatchesSection matches={data.matches} />
          </>
        }
        right={
          <>
            <IdentityRail user={user} profile={data.profile} hasProfile />
            <FeedFooter summary={data.summary} ingest={data.ingest} />
          </>
        }
      />
    </AppShell>
  )

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }
}

/** The identity card, LinkedIn-style: photo, name, headline, and the two
    actions that matter. Sits in the right rail because the left is now the
    suite navigation — two left-hand columns would squeeze the feed into a
    column too narrow to read a match card in. */
function IdentityRail({
  user,
  profile,
  hasProfile,
}: {
  user: { fullName: string; email: string }
  profile: NonNullable<DashboardResponse['profile']> | null
  hasProfile: boolean
}) {
  const queryClient = useQueryClient()
  const onChange = () => {
    void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }
  const upload = useUploadCv(onChange)
  const remove = useMutation({ mutationFn: profileApi.remove, onSuccess: onChange })
  const inputRef = useRef<HTMLInputElement>(null)

  const photoUrl = profile ? profileApi.mediaUrl(profile.profilePicture) : null
  const bannerUrl = profile ? profileApi.mediaUrl(profile.bannerPicture) : null
  const name = profile?.fullName ?? user.fullName
  const headline = profile?.headline ?? (hasProfile ? 'CV uploaded' : 'Add your CV to get matches')

  function pick(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (file) upload.mutate(file)
    e.target.value = ''
  }

  return (
    <section aria-labelledby="profile-title" className="overflow-hidden rounded-lg border border-border-subtle bg-surface">
      {bannerUrl ? (
        <img src={bannerUrl} alt="" className="h-20 w-full object-cover" />
      ) : (
        <div aria-hidden="true" className="h-20 w-full bg-sunken" />
      )}

      <div className="px-4 pb-4">
        <div className="-mt-8 flex justify-center">
          {photoUrl ? (
            <img
              src={photoUrl}
              alt="Profile photo"
              className="h-16 w-16 rounded-full border-2 border-surface object-cover"
            />
          ) : (
            <div
              aria-hidden="true"
              className="flex h-16 w-16 items-center justify-center rounded-full border-2 border-surface bg-brand text-lg font-bold text-white"
            >
              {initials(name)}
            </div>
          )}
        </div>

        <h2 id="profile-title" className="mt-2 truncate text-center text-base font-semibold text-ink">
          {name}
        </h2>
        <p className="truncate text-center text-sm text-ink-muted">{headline}</p>

        {profile && (
          <p className="mt-1 flex items-center justify-center gap-1 truncate text-sm text-ink-muted">
            {profile.location && (
              <>
                <MapPinIcon className="h-3.5 w-3.5" />
                {profile.location}
              </>
            )}
          </p>
        )}

        <div className="mt-3 grid grid-cols-2 gap-2 border-t border-border-subtle pt-3 text-center">
          <div>
            <p className="tabular text-lg font-semibold text-ink">{profile?.skills.length ?? 0}</p>
            <p className="text-xs text-ink-muted">Skills</p>
          </div>
          <div>
            <p className="tabular text-lg font-semibold text-ink">
              {profile?.experiences.length ?? 0}
            </p>
            <p className="text-xs text-ink-muted">Roles</p>
          </div>
        </div>

        <Link
          to="/profile"
          className="mt-3 flex min-h-11 w-full items-center justify-center rounded-full border border-border-strong text-sm font-semibold text-ink transition-colors hover:bg-sunken"
        >
          Edit profile
        </Link>

        {hasProfile && (
          <div className="mt-2 grid grid-cols-2 gap-2">
            <label className="relative flex min-h-11 cursor-pointer items-center justify-center rounded-full border border-border-strong text-sm font-semibold text-ink transition-colors hover:bg-sunken has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-accent">
              <span>{upload.isPending ? 'Uploading…' : 'Replace CV'}</span>
              <input
                ref={inputRef}
                type="file"
                name="cv"
                accept=".pdf,.docx,.txt"
                className="absolute inset-0 h-full w-full cursor-pointer opacity-0"
                aria-label="Replace your CV file (PDF, DOCX or TXT)"
                disabled={upload.isPending}
                onChange={pick}
              />
            </label>
            <button
              onClick={() => remove.mutate()}
              disabled={remove.isPending}
              className="min-h-11 rounded-full border border-border-strong text-sm font-semibold text-ink transition-colors hover:bg-sunken"
            >
              Remove
            </button>
          </div>
        )}

        {(upload.error || remove.error) && (
          <p role="alert" className="mt-3 rounded-lg bg-danger-subtle px-3 py-2 text-sm text-danger">
            {(upload.error ?? remove.error)?.message}
          </p>
        )}

        {profile && profile.skills.filter((s) => s.isCore).length > 0 && (
          <ul className="mt-3 flex flex-wrap gap-1.5 border-t border-border-subtle pt-3">
            {profile.skills
              .filter((s) => s.isCore)
              .slice(0, 5)
              .map((s) => (
                <li
                  key={s.name}
                  className="rounded bg-sunken px-2 py-0.5 text-xs font-semibold text-ink-body"
                >
                  {s.name}
                </li>
              ))}
          </ul>
        )}
      </div>
    </section>
  )
}

function CompletionBanner({ profile: p }: { profile: NonNullable<DashboardResponse['profile']> }) {
  const c = p.completeness
  if (c.requiredMet >= c.requiredTotal) return null

  return (
    <Link
      to="/profile"
      className="block rounded-lg border border-border-subtle bg-surface p-4 transition-colors hover:bg-sunken"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="space-y-0.5">
          <p className="font-semibold text-ink">Your profile is {c.percent}% complete</p>
          <p className="text-sm text-ink-muted">
            {c.requiredTotal - c.requiredMet} core{' '}
            {c.requiredTotal - c.requiredMet === 1 ? 'item is' : 'items are'} still missing.
            Filling them in gets better matches.
          </p>
        </div>
        <span className="text-sm font-semibold text-link">Edit profile</span>
      </div>
      <div
        role="progressbar"
        aria-valuenow={c.percent}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="Profile completeness"
        className="mt-3 h-1.5 overflow-hidden rounded-full bg-border-subtle"
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
    <Card bodyClassName="p-6">
      <h1 id="upload-title" className="text-xl font-bold tracking-tight text-ink">
        Upload your CV
      </h1>
      <p className="mt-2 text-ink-body">
        Match your CV against live roles from MyJobMag. Every score is explained:
        you see the facts it came from and the source posting it points to, never
        an unexplained number.
      </p>

      {!hasIngestedRoles && (
        <p className="mt-3 rounded-lg bg-sunken px-4 py-3 text-sm text-ink-muted">
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
        <label className="relative mt-5 flex min-h-14 w-full max-w-md cursor-pointer items-center justify-center rounded-lg border-2 border-dashed border-border-strong bg-sunken px-4 text-sm font-semibold text-link transition-colors hover:border-accent hover:bg-accent-subtle has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-accent">
          <span className="flex items-center gap-2">
            <UploadIcon className="h-5 w-5" />
            {upload.isPending ? 'Working…' : 'Choose your CV'}
          </span>
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
        <p role="alert" className="mt-4 flex items-start gap-2 rounded-lg border border-danger bg-danger-subtle px-4 py-3 text-sm text-danger">
          <AlertIcon className="mt-0.5 h-4 w-4 shrink-0" />
          {upload.error.message}
        </p>
      )}

      <h2 className="mt-8 text-sm font-semibold text-ink">What happens next</h2>
      <ol className="mt-3 space-y-3">
        {[
          ['We read your CV', 'Skills, experience, qualifications and location are extracted, nothing is invented.'],
          ['We score live roles', 'Each role is scored against your profile with the reasons attached to the score.'],
          ['You apply with an edge', 'See what you match, what to fix, and apply straight to the source posting.'],
        ].map(([title, body], i) => (
          <li key={title} className="flex gap-3">
            <span
              aria-hidden="true"
              className="tabular flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-sunken text-xs font-semibold text-ink-body"
            >
              {i + 1}
            </span>
            <span>
              <span className="block text-sm font-semibold text-ink">{title}</span>
              <span className="block text-sm text-ink-muted">{body}</span>
            </span>
          </li>
        ))}
      </ol>
    </Card>
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
    <Card bodyClassName="p-4">
      <h2 className="text-base font-semibold text-ink">Your match summary</h2>
      <dl aria-label="Match summary by tier" className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {tiles.map((t) => (
          <div key={t.label} className="rounded-lg bg-sunken p-3">
            <dt className="text-xs text-ink-muted">{t.label}</dt>
            <dd className={`tabular mt-0.5 text-2xl font-bold ${t.tone}`}>{t.value}</dd>
          </div>
        ))}
      </dl>
      <p className="mt-3 text-sm text-ink-muted">
        {summary.rolesConsidered === 0
          ? 'No roles have been scored against your CV yet.'
          : `Scored against ${summary.rolesConsidered} of ${summary.rolesIngested} live roles.`}
      </p>
    </Card>
  )
}

function MatchesSection({ matches }: { matches: MatchedRole[] }) {
  const queryClient = useQueryClient()

  // Tracked roles come from the applications tracker, so the dashboard has to
  // know about it — otherwise "Track" would be a control that silently does
  // nothing visible and the Applications page would look empty for no reason.
  const tracked = useQuery({ queryKey: ['applications'], queryFn: () => applicationsApi.list() })
  const trackedRoles = new Set((tracked.data?.items ?? []).map((a) => a.roleId))

  if (matches.length === 0) {
    return (
      <Card>
        <h2 className="text-base font-semibold text-ink">Matched roles</h2>
        <p className="mt-2 text-ink-body">
          Your profile is saved but no roles matched yet. Check back after the next
          role feed arrives, and your scores re-run automatically.
        </p>
      </Card>
    )
  }

  return (
    <section aria-labelledby="matches-title">
      <h2 id="matches-title" className="text-lg font-semibold text-ink">
        Matched roles
      </h2>
      <p className="mt-0.5 text-sm text-ink-muted">{matches.length} ranked by fit</p>
      <ul className="mt-3 space-y-3">
        {matches.map((m) => (
          <MatchCard
            key={m.roleId}
            match={m}
            isTracked={trackedRoles.has(m.roleId)}
            onTracked={() => queryClient.invalidateQueries({ queryKey: ['applications'] })}
          />
        ))}
      </ul>
    </section>
  )
}

function MatchCard({
  match: m,
  isTracked,
  onTracked,
}: {
  match: MatchedRole
  isTracked: boolean
  onTracked: () => void
}) {
  const style = tierStyles[m.tier] ?? tierStyles.Stretch
  const applyUrl = m.postings[0]?.url
  const otherPostings = m.postings.length - 1

  const track = useMutation({
    mutationFn: () => applicationsApi.create(m.roleId, { status: 'Shortlisted' }),
    onSuccess: onTracked,
  })

  return (
    <li className="overflow-hidden rounded-lg border border-border-subtle bg-surface">
      <article className="p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-ink">{m.title}</h3>
            <p className="text-sm text-ink-body">{m.company}</p>
            <p className="mt-0.5 text-sm text-ink-muted">
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
          <div className="flex shrink-0 items-center gap-2">
            <span className={`tabular text-3xl font-bold ${style.score}`}>{m.score}</span>
            <TierBadge tier={m.tier} />
          </div>
        </div>

        {m.evidence.length > 0 && (
          <div className="mt-4">
            <h4 className="text-sm font-semibold text-ink">Why it matches</h4>
            <ul className="mt-2 space-y-1.5">
              {m.evidence.slice(0, 4).map((e, i) => (
                <li key={i} className="flex items-start gap-2 text-sm text-ink-body">
                  <CheckIcon className="mt-0.5 h-4 w-4 shrink-0 text-success" />
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
          <div className="mt-4 rounded-lg bg-sunken p-3">
            <h4 className="flex items-center gap-1.5 text-sm font-semibold text-ink">
              <AlertIcon className="h-4 w-4 text-ink-muted" />
              Before you apply
            </h4>
            <ul className="mt-1.5 space-y-1">
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
          <ul className="mt-4 flex flex-wrap gap-1.5">
            {m.roleSkills.slice(0, 8).map((s) => (
              <li key={s} className="rounded bg-sunken px-2.5 py-1 text-xs font-semibold text-ink-body">
                {s}
              </li>
            ))}
          </ul>
        )}

        <div className="mt-4 flex flex-wrap items-center gap-3 border-t border-border-subtle pt-3">
          {applyUrl && (
            <a
              href={applyUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="flex min-h-11 items-center rounded-full bg-accent px-5 text-sm font-semibold text-white transition-colors hover:bg-accent-hover"
            >
              Apply on {m.postings[0]?.source}
            </a>
          )}
          {isTracked ? (
            <Link
              to="/applications"
              className="flex min-h-11 items-center gap-1.5 rounded-full border border-border-strong bg-surface px-4 text-sm font-semibold text-ink-body transition-colors hover:bg-sunken"
            >
              <CheckIcon className="h-4 w-4 text-success" />
              Tracked
            </Link>
          ) : (
            <button
              type="button"
              onClick={() => track.mutate()}
              disabled={track.isPending}
              className="flex min-h-11 items-center gap-1.5 rounded-full border border-border-strong bg-surface px-4 text-sm font-semibold text-ink-body transition-colors hover:bg-sunken disabled:opacity-60"
            >
              <BoardIcon className="h-4 w-4" />
              {track.isPending ? 'Tracking…' : 'Track this role'}
            </button>
          )}
          {track.isError && (
            <span role="alert" className="text-sm font-semibold text-danger">
              {track.error.message}
            </span>
          )}
          {otherPostings > 0 && (
            <details className="text-sm">
              <summary className="min-h-11 cursor-pointer list-none py-2 font-semibold text-link">
                {otherPostings} more posting{otherPostings > 1 ? 's' : ''}
              </summary>
              <ul className="mt-1 space-y-1 rounded-lg bg-sunken p-3">
                {m.postings.map((p) => (
                  <li key={p.id}>
                    <a
                      href={p.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="font-medium text-link underline underline-offset-2"
                    >
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

/** Right rail. The feed's provenance, stated rather than implied. */
function FeedFooter({ summary, ingest }: { summary: DashboardResponse['summary']; ingest: IngestHealth | null }) {
  const worst = worstSource(ingest)

  return (
    <aside aria-label="About this feed" className="rounded-lg border border-border-subtle bg-surface p-4">
      <h2 className="text-sm font-semibold text-ink">About this feed</h2>
      <p className="mt-2 text-sm text-ink-muted">
        {summary.rolesIngested} roles ingested from MyJobMag. Roles are clustered,
        so one job posted in many states appears once.
      </p>
      {summary.rolesUpdatedAt && (
        <p className="mt-2 text-sm text-ink-muted">
          Last updated {formatDate(summary.rolesUpdatedAt)}
        </p>
      )}

      {/* The poll time, not the role timestamp: a board can be up to a day
          stale by design, and saying so is more useful than "updated 4pm"
          implying data that is already there. */}
      {ingest?.lastPolledAt ? (
        <p className="mt-2 text-sm text-ink-muted">
          Source checked {formatDate(ingest.lastPolledAt)}
          {ingest.sourcesTotal > 0 && ` · ${ingest.sourcesTotal} source${ingest.sourcesTotal === 1 ? '' : 's'}`}
        </p>
      ) : null}

      {worst && <p className="mt-2 text-sm text-warn">{sourceReason(worst)}</p>}

      <p className="mt-3 border-t border-border-subtle pt-3 text-xs text-ink-muted">
        Every score shows the evidence behind it. Nothing here is invented.
      </p>
    </aside>
  )
}

/** The source most in need of an explanation, or null when all are healthy. A
    source is not "fine" merely because it did not throw: a poll that quietly
    returns nothing looks identical to a slow week unless it is called out. */
function worstSource(ingest: IngestHealth | null): SourceHealth | null {
  if (!ingest || ingest.sources.length === 0) return null
  return ingest.sources.find((s) => s.status === 'blocked') ?? ingest.sources.find((s) => s.status !== 'healthy') ?? null
}

function sourceReason(source: SourceHealth): string {
  if (source.status === 'blocked') return `${source.name} is refusing our requests. Roles may be out of date.`
  if (source.consecutiveZeroRuns > 0)
    return `${source.name} returned no new roles ${source.consecutiveZeroRuns} poll${source.consecutiveZeroRuns === 1 ? '' : 's'} in a row.`
  return `${source.name} is not polling normally. Roles may be out of date.`
}

/** Shown only when the feed is not telling the truth. A quiet board and a
    broken board look the same on screen, and a user who trusts a stale list is
    worse off than one who is told not to. */
function FeedHealthBanner({ ingest }: { ingest: IngestHealth | null }) {
  if (!ingest) return null

  const source = worstSource(ingest)
  if (!source) return null

  return (
    <div
      role="status"
      className="rounded-lg border border-warn/40 bg-warn-tint p-4 text-sm text-ink-body"
    >
      <h2 className="text-sm font-semibold text-ink">This feed may be out of date</h2>
      <p className="mt-1">{sourceReason(source)}</p>
      <p className="mt-1 text-xs text-ink-muted">
        Roles already listed are still real. New ones may be missing until the next successful check.
      </p>
    </div>
  )
}

function useUploadCv(onUploaded: () => void) {
  return useMutation({
    mutationFn: profileApi.uploadCv,
    onSuccess: onUploaded,
  })
}
