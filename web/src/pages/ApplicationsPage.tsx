import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { applications as api, APPLICATIONS_PIPELINE, type Application } from '../lib/api'
import { BoardIcon, CheckIcon, DocumentIcon, ShieldIcon } from '../components/icons'
import {
  AppColumns,
  Button,
  Card,
  EmptyState,
  PageHeader,
  Pill,
  RailNote,
  StatTiles,
} from '../components/AppShell'

const STATUS_TONE: Record<string, 'neutral' | 'good' | 'warn' | 'info'> = {
  Shortlisted: 'neutral',
  Applied: 'info',
  Interviewing: 'warn',
  Closed: 'good',
}

/** The next step in the pipeline, or null when the role is already Closed.
    Forward-only on the server too, so offering a backwards move here would just
    produce a 409. */
function nextStatus(status: string): string | null {
  const i = APPLICATIONS_PIPELINE.indexOf(status as (typeof APPLICATIONS_PIPELINE)[number])
  if (i < 0 || i === APPLICATIONS_PIPELINE.length - 1) return null
  return APPLICATIONS_PIPELINE[i + 1]
}

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

/** Inline notes and next-action date. Both save on blur, so there is no
    Save button to forget to press and no state to lose on navigation. */
function ApplicationRow({ application }: { application: Application }) {
  const queryClient = useQueryClient()
  const [notes, setNotes] = useState(application.notes ?? '')
  const [nextAction, setNextAction] = useState(
    application.nextActionAt ? application.nextActionAt.slice(0, 10) : '',
  )

  useEffect(() => {
    setNotes(application.notes ?? '')
    setNextAction(application.nextActionAt ? application.nextActionAt.slice(0, 10) : '')
  }, [application.id, application.notes, application.nextActionAt])

  const patch = useMutation({
    mutationFn: (body: { notes?: string | null; nextActionAt?: string | null }) =>
      api.update(application.id, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['applications'] }),
  })

  const advance = useMutation({
    mutationFn: () => {
      const to = nextStatus(application.status)
      if (!to) throw new Error('This application is already closed.')
      return api.update(application.id, { status: to })
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['applications'] }),
  })

  const upcoming = application.nextActionAt
    ? new Date(application.nextActionAt) < new Date()
    : false

  return (
    <li className="border-b border-border-subtle p-4 last:border-b-0">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="text-base font-semibold text-ink">
            {application.roleUrl ? (
              <a
                href={application.roleUrl}
                target="_blank"
                rel="noreferrer noopener"
                className="text-link hover:underline"
              >
                {application.roleTitle}
              </a>
            ) : (
              application.roleTitle
            )}
          </h3>
          <p className="text-sm text-ink-body">{application.company}</p>

          <div className="mt-2 flex flex-wrap items-center gap-1.5">
            <Pill tone={STATUS_TONE[application.status] ?? 'neutral'}>{application.status}</Pill>
            {application.matchTier && <Pill tone="neutral">{application.matchTier} fit</Pill>}
            {application.hasTailoredDocument && (
              <Pill tone="info">
                <DocumentIcon className="h-3 w-3" />
                Tailored
              </Pill>
            )}
            {application.appliedAt && (
              <span className="text-xs text-ink-muted">
                Applied {formatDate(application.appliedAt)}
              </span>
            )}
          </div>
        </div>

        {nextStatus(application.status) && (
          <Button
            variant="secondary"
            onClick={() => advance.mutate()}
            disabled={advance.isPending}
            className="shrink-0"
          >
            <CheckIcon className="h-4 w-4" />
            Mark {nextStatus(application.status)}
          </Button>
        )}
      </div>

      <div className="mt-3 grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto]">
        <div>
          <label
            htmlFor={`notes-${application.id}`}
            className="mb-1 block text-xs font-semibold text-ink-muted"
          >
            Notes
          </label>
          <textarea
            id={`notes-${application.id}`}
            value={notes}
            rows={2}
            placeholder="Who you spoke to, what they said, what to follow up on."
            onChange={(e) => setNotes(e.target.value)}
            onBlur={() => {
              const next = notes.trim() || null
              if (next !== (application.notes ?? null)) patch.mutate({ notes: next })
            }}
            className="w-full rounded-lg border border-border-strong bg-surface p-2.5 text-sm text-ink-body placeholder:text-ink-faint"
          />
        </div>

        <div>
          <label
            htmlFor={`next-${application.id}`}
            className="mb-1 block text-xs font-semibold text-ink-muted"
          >
            Next action
          </label>
          <input
            id={`next-${application.id}`}
            type="date"
            value={nextAction}
            onChange={(e) => setNextAction(e.target.value)}
            onBlur={() => {
              const next = nextAction ? new Date(`${nextAction}T09:00:00Z`).toISOString() : null
              if (next !== (application.nextActionAt ?? null)) patch.mutate({ nextActionAt: next })
            }}
            className={`w-full rounded-lg border bg-surface p-2.5 text-sm sm:w-auto ${
              upcoming ? 'border-danger/50 text-danger' : 'border-border-strong text-ink-body'
            }`}
          />
        </div>
      </div>

      {upcoming && (
        <p className="mt-2 text-sm font-semibold text-danger">
          The date you set has passed. Follow up or clear it.
        </p>
      )}

      {(patch.isError || advance.isError) && (
        <p role="alert" className="mt-2 text-sm font-semibold text-danger">
          {(patch.error ?? advance.error)?.message}
        </p>
      )}
    </li>
  )
}

export function ApplicationsPage() {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['applications'],
    // Wrapped, not passed by reference: React Query hands the query function a
    // context object, and `api.list` expects a status string, so passing it
    // directly both fails to typecheck and would send the context as a filter.
    queryFn: () => api.list(),
  })

  if (isLoading) {
    return (
      <>
        <PageHeader title="Applications" lede="Loading your tracker…" />
        <Card bodyClassName="p-4">
          <p className="text-sm text-ink-muted">Loading…</p>
        </Card>
      </>
    )
  }

  if (isError) {
    return (
      <>
        <PageHeader title="Applications" lede="Everything you have applied for." />
        <Card bodyClassName="p-4">
          <h2 className="text-sm font-bold text-danger">We could not load your tracker</h2>
          <p className="mt-1 text-sm text-ink-body">{error.message}</p>
        </Card>
      </>
    )
  }

  const summary = data!
  const overdue = summary.nextActions.filter((a) => new Date(a.nextActionAt!) < new Date())

  return (
    <>
      <PageHeader
        title="Applications"
        lede="One record per role, not per posting. JobSuites never submits anything for you — you move a role forward yourself, and we keep the history."
      />

      <AppColumns
        center={
          <div className="space-y-4">
            <StatTiles
              label="Pipeline"
              tiles={[
                { label: 'Shortlisted', value: summary.shortlisted },
                { label: 'Applied', value: summary.applied, tone: 'info' },
                { label: 'Interviewing', value: summary.interviewing, tone: 'good' },
                { label: 'Closed', value: summary.closed, tone: 'muted' },
              ]}
              footnote={
                summary.total === 0
                  ? 'Nothing tracked yet.'
                  : `${summary.total} role${summary.total === 1 ? '' : 's'} across the pipeline${
                      overdue.length > 0 ? `, ${overdue.length} needing a follow-up` : ''
                    }.`
              }
            />

            {summary.nextActions.length > 0 && (
              <Card
                title={overdue.length > 0 ? 'Needs your attention' : 'Next actions'}
                className={overdue.length > 0 ? 'border-danger/30' : ''}
                bodyClassName="p-0"
              >
                <ul>
                  {summary.nextActions.map((a) => {
                    const isOverdue = new Date(a.nextActionAt!) < new Date()
                    return (
                      <li
                        key={a.id}
                        className="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle p-4 last:border-b-0"
                      >
                        <span className="min-w-0 text-sm font-semibold text-ink">
                          {a.roleTitle}
                          <span className="font-normal text-ink-muted"> · {a.company}</span>
                        </span>
                        <span
                          className={`shrink-0 text-sm ${
                            isOverdue ? 'font-semibold text-danger' : 'text-ink-muted'
                          }`}
                        >
                          {isOverdue ? 'Overdue · ' : ''}
                          {formatDate(a.nextActionAt)}
                        </span>
                      </li>
                    )
                  })}
                </ul>
              </Card>
            )}

            <Card
              title={summary.total === 0 ? 'Tracked roles' : `Tracked roles (${summary.total})`}
              bodyClassName="p-0"
            >
              {summary.total === 0 ? (
                <EmptyState
                  icon={<BoardIcon className="h-9 w-9" />}
                  title="No roles tracked yet"
                  action={
                    <Link to="/app">
                      <Button>Go to your matches</Button>
                    </Link>
                  }
                >
                  Open your dashboard and choose <span className="font-semibold">Track this
                  role</span> on anything you are considering. Tracked roles collect their
                  tailored CV, your notes and the follow-up dates you set, so nothing sits in
                  a tab you forgot about.
                </EmptyState>
              ) : (
                <ul>
                  {summary.items.map((a) => (
                    <ApplicationRow key={a.id} application={a} />
                  ))}
                </ul>
              )}
            </Card>
          </div>
        }
        right={
          <>
            <RailNote title="How this works" icon={<CheckIcon className="h-4 w-4" />}>
              <p>
                A role moves <span className="font-semibold">Shortlisted → Applied →
                Interviewing → Closed</span>. We do not let it move backwards, because a
                tracker that quietly rewrites history is worse than one that makes you
                correct it.
              </p>
              <p>
                One record per role, even when it is posted on several boards. Applying twice
                to the same job is the most common way trackers become noise.
              </p>
            </RailNote>

            <RailNote title="Notes and follow-ups" icon={<CheckIcon className="h-4 w-4" />}>
              <p>
                Notes and dates save when you click away from the field — there is no Save
                button to forget.
              </p>
              <p>
                A past-due date turns red so an overdue follow-up is visible without
                opening each role.
              </p>
            </RailNote>

            <RailNote title="We never apply for you" icon={<ShieldIcon className="h-4 w-4" />}>
              <p>
                There is no submission step here on purpose. Sending an application with a
                candidate's name attached is a decision only they can make, and any tool
                that automates it is making it for them.
              </p>
            </RailNote>
          </>
        }
      />
    </>
  )
}
