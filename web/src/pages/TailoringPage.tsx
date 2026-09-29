import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  dashboard as dashboardApi,
  tailoring as api,
  type TailoredDocument,
} from '../lib/api'
import {
  AlertIcon,
  CheckIcon,
  DocumentIcon,
  ShieldIcon,
  SparkIcon,
} from '../components/icons'
import { exportDocx, exportPdf } from '../lib/export'
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

const STATUS_TONE = {
  draft: 'neutral',
  needs_review: 'bad',
  approved: 'good',
  rejected: 'warn',
} as const

/**
 * The fabrication verdict, given the prominence it needs.
 *
 * This block sits above the document text on purpose. The check is the feature
 * (docs/PRODUCT.md §6.2), and a green tick the user has to scroll to find is
 * decoration. When it fails, the offending strings are listed before the
 * document, not after.
 */
function VerificationPanel({ document }: { document: TailoredDocument }) {
  const { verification } = document

  if (verification.passed) {
    return (
      <div className="flex items-start gap-3 rounded-lg border border-success/30 bg-success-tint p-4">
        <CheckIcon className="mt-0.5 h-5 w-5 shrink-0 text-success" />
        <div>
          <h3 className="text-sm font-semibold text-success">Nothing invented</h3>
          <p className="mt-0.5 text-sm text-ink-body">
            We checked {verification.claimsChecked} names, employers and figures in this
            document against your CV. Every one of them is in there.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div
      role="alert"
      className="rounded-lg border border-danger/40 bg-danger-tint p-4"
    >
      <div className="flex items-start gap-3">
        <AlertIcon className="mt-0.5 h-5 w-5 shrink-0 text-danger" />
        <div className="min-w-0">
          <h3 className="text-sm font-semibold text-danger">
            {verification.fabrications.length} thing
            {verification.fabrications.length === 1 ? '' : 's'} in this document could not be
            found in your CV
          </h3>
          <p className="mt-0.5 text-sm text-ink-body">
            We build tailored CVs by selecting from facts you have already given us, so this
            should not happen. It usually means a profile field holds something your CV does
            not — correct it, then regenerate. This document cannot be approved as it is.
          </p>
          <ul className="mt-3 space-y-2">
            {verification.fabrications.map((f) => (
              <li key={`${f.kind}-${f.text}`} className="rounded bg-surface p-2.5">
                <p className="text-sm font-semibold text-ink">
                  <span className="font-normal text-ink-muted">{f.kind}: </span>
                  {f.text}
                </p>
                <p className="mt-0.5 text-xs text-ink-muted">{f.context}</p>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </div>
  )
}

/**
 * An AI rewrite the check refused, shown so the rejection is legible.
 *
 * The stored document is always the deterministic build; this is the text that
 * was thrown away and why. Collapsed by default because it is a diagnosis, not
 * the thing the user came to read — but it is never hidden silently either: the
 * count is on the summary line above.
 */
function RejectedRewritePanel({ document }: { document: TailoredDocument }) {
  const [open, setOpen] = useState(false)
  const rejected = document.rejectedRewrite
  if (!rejected) return null

  const { fabrications } = rejected.verification

  return (
    <div className="rounded-lg border border-warn/40 bg-warn-tint p-4">
      <div className="flex items-start gap-3">
        <SparkIcon className="mt-0.5 h-5 w-5 shrink-0 text-warn" />
        <div className="min-w-0 flex-1">
          <h3 className="text-sm font-semibold text-warn">
            An AI rewrite was rejected — {fabrications.length} claim
            {fabrications.length === 1 ? '' : 's'} not in your CV
          </h3>
          <p className="mt-0.5 text-sm text-ink-body">
            We tried a second pass to sharpen the wording for this posting. It introduced
            something your CV does not say, so we threw it away. The document below is the
            version built only from your own facts.
          </p>

          <ul className="mt-3 space-y-2">
            {fabrications.map((f) => (
              <li key={`${f.kind}-${f.text}`} className="rounded bg-surface p-2.5">
                <p className="text-sm font-semibold text-ink">
                  <span className="font-normal text-ink-muted">{f.kind}: </span>
                  {f.text}
                </p>
                <p className="mt-0.5 text-xs text-ink-muted">{f.context}</p>
              </li>
            ))}
          </ul>

          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            aria-expanded={open}
            className="mt-3 text-sm font-semibold text-warn underline underline-offset-2"
          >
            {open ? 'Hide the rejected text' : 'Read the rejected text'}
          </button>

          {open && (
            <div className="mt-2 space-y-3 rounded bg-surface p-3">
              {rejected.document.map((section) => (
                <section key={section.heading}>
                  <h4 className="text-xs font-bold uppercase tracking-wide text-ink-muted">
                    {section.heading}
                  </h4>
                  <p className="mt-1 whitespace-pre-wrap text-sm leading-relaxed text-ink-body">
                    {section.body}
                  </p>
                </section>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function CoveragePanel({ document }: { document: TailoredDocument }) {
  const covered = document.coverage.filter((c) => c.covered)
  const uncovered = document.coverage.filter((c) => !c.covered)
  const uncoveredMust = uncovered.filter((c) => c.mustHave)

  return (
    <Card title="What this role asks for">
      <p className="mb-3 text-sm text-ink-body">
        {document.coveredRequirements} of {document.coverage.length} requirement
        {document.coverage.length === 1 ? '' : 's'} have supporting evidence in your CV.
      </p>

      {uncoveredMust.length > 0 && (
        <div className="mb-3 rounded-lg border border-warn/40 bg-warn-tint p-3">
          <h3 className="text-sm font-semibold text-warn">
            {uncoveredMust.length} required item{uncoveredMust.length === 1 ? '' : 's'} we
            could not evidence
          </h3>
          <p className="mt-0.5 text-sm text-ink-body">
            We did not write about {uncoveredMust.length === 1 ? 'it' : 'them'}, because
            inventing an answer would be worse than a gap you can see. Add the experience if
            you have it.
          </p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-ink-body">
            {uncoveredMust.map((c) => (
              <li key={c.requirement}>{c.requirement}</li>
            ))}
          </ul>
        </div>
      )}

      <ul className="space-y-1.5">
        {[...covered, ...uncovered].map((c) => (
          <li key={c.requirement} className="flex items-start gap-2.5 text-sm">
            <span
              className={`mt-1 h-2 w-2 shrink-0 rounded-full ${c.covered ? 'bg-success' : 'bg-warn'}`}
              aria-hidden="true"
            />
            <span className="min-w-0">
              <span className={c.covered ? 'text-ink-body' : 'font-semibold text-ink'}>
                {c.requirement}
              </span>
              {c.mustHave && (
                <span className="ml-1.5 text-xs text-ink-muted">(required)</span>
              )}
              {c.supportingFact && (
                <span className="block text-xs text-ink-muted">{c.supportingFact}</span>
              )}
            </span>
          </li>
        ))}
      </ul>
    </Card>
  )
}

function ExportButtons({ document }: { document: TailoredDocument }) {
  const approved = document.status === 'approved'
  const stem = `${document.roleTitle.replace(/[^\w\- ]/g, '')}-${document.roleCompany.replace(
    /[^\w\- ]/g,
    '',
  )}-v${document.version}`

  return (
    <>
      <div className="flex gap-2">
        <Button variant="secondary" disabled={!approved} onClick={() => exportPdf(stem, document.document)}>
          Export PDF
        </Button>
        <Button variant="secondary" disabled={!approved} onClick={() => exportDocx(stem, document.document)}>
          Export Word
        </Button>
      </div>
      {!approved && (
        <span className="w-full text-xs text-ink-muted">
          Approve this version first — nothing is exported before you approve it.
        </span>
      )}
    </>
  )
}

function DocumentView({ document }: { document: TailoredDocument }) {
  const queryClient = useQueryClient()

  const decide = useMutation({
    mutationFn: (approve: boolean) => api.approve(document.id, approve),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['tailoring'] })
      void queryClient.invalidateQueries({ queryKey: ['tailoring', document.id] })
    },
  })

  return (
    <div className="space-y-4">
      <Card
        title={`${document.roleTitle} — ${document.roleCompany}`}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Pill tone={STATUS_TONE[document.status] ?? 'neutral'}>
              {document.status === 'needs_review' ? 'Needs review' : document.status}
            </Pill>
            <Pill tone="neutral">v{document.version}</Pill>
          </div>
        }
      >
        <div className="space-y-3">
          {document.isStale && (
            <p className="rounded-lg bg-warn-tint p-3 text-sm font-semibold text-warn">
              Your CV changed since this was generated. Regenerate before you send it.
            </p>
          )}

          <VerificationPanel document={document} />

          <RejectedRewritePanel document={document} />
        </div>

        <div className="mt-4 flex flex-wrap gap-2 border-t border-border-subtle pt-4">
          <ExportButtons document={document} />
          {document.status === 'approved' ? (
            <>
              <Pill tone="good">
                <CheckIcon className="h-3 w-3" />
                Approved {document.approvedAt ? new Date(document.approvedAt).toLocaleDateString() : ''}
              </Pill>
              <Button variant="secondary" onClick={() => decide.mutate(false)}>
                Withdraw approval
              </Button>
            </>
          ) : (
            <>
              <Button
                onClick={() => decide.mutate(true)}
                disabled={decide.isPending || !document.verification.passed}
              >
                <CheckIcon className="h-4 w-4" />
                Approve this version
              </Button>
              <Button variant="secondary" onClick={() => decide.mutate(false)}>
                Reject
              </Button>
            </>
          )}
          {decide.isError && (
            <p role="alert" className="w-full text-sm font-semibold text-danger">
              {decide.error.message}
            </p>
          )}
        </div>
      </Card>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_300px] lg:items-start">
        <Card title="The tailored CV">
          <div className="space-y-4">
            {document.document.map((section) => (
              <section key={section.heading}>
                <h3 className="text-xs font-bold uppercase tracking-wide text-ink-muted">
                  {section.heading}
                </h3>
                <p className="mt-1 whitespace-pre-wrap text-sm leading-relaxed text-ink-body">
                  {section.body}
                </p>
              </section>
            ))}
          </div>
        </Card>

        <div className="space-y-4">
          {document.diff.length > 0 && (
            <Card title="What changed, and why">
              <ul className="space-y-3">
                {document.diff.map((change, i) => (
                  <li key={i}>
                    <Pill
                      tone={
                        change.kind === 'gap'
                          ? 'warn'
                          : change.kind === 'reordered' || change.kind === 'reworded'
                            ? 'info'
                            : 'neutral'
                      }
                    >
                      {change.kind}
                    </Pill>
                    <p className="mt-1 text-sm text-ink-body">{change.detail}</p>
                  </li>
                ))}
              </ul>
            </Card>
          )}

          <CoveragePanel document={document} />

          <Card title="Facts used">
            <p className="mb-2 text-sm text-ink-body">
              Every line above comes from one of these. Nothing here was written fresh.
            </p>
            <ul className="space-y-1">
              {document.factRefs.map((ref) => (
                <li key={ref} className="text-xs text-ink-muted">
                  {ref}
                </li>
              ))}
            </ul>
          </Card>
        </div>
      </div>
    </div>
  )
}

export function TailoringPage() {
  const [selected, setSelected] = useState<string | null>(null)

  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: dashboardApi.get })
  const list = useQuery({ queryKey: ['tailoring'], queryFn: () => api.list() })

  const generate = useMutation({
    mutationFn: (roleId: string) => api.generate(roleId),
    onSuccess: (doc) => {
      setSelected(doc.id)
      void list.refetch()
    },
  })

  const detail = useQuery({
    queryKey: ['tailoring', selected],
    queryFn: () => api.get(selected!),
    enabled: Boolean(selected),
  })

  const matches = dashboard.data?.matches ?? []
  const existing = list.data ?? []
  const byRole = new Map(existing.map((e) => [e.roleId, e]))

  const approved = existing.filter((e) => e.status === 'approved').length
  const failedCheck = existing.filter((e) => !e.passedVerification).length
  const withGaps = existing.reduce((n, e) => n + e.uncoveredMustHave, 0)

  return (
    <>
      <PageHeader
        title="CV tailoring"
        lede="We rebuild your CV for one role by choosing, ordering and rewording what you have already written. We cannot add anything, and we check the result for anything we could not find in your CV."
      />

      <AppColumns
        center={
          <div className="space-y-4">
            <StatTiles
              label="Your tailored documents"
              tiles={[
                { label: 'Documents', value: existing.length },
                { label: 'Approved', value: approved, tone: 'good' },
                { label: 'Failed check', value: failedCheck, tone: failedCheck > 0 ? 'info' : 'muted' },
                { label: 'Unevidenced asks', value: withGaps, tone: withGaps > 0 ? 'info' : 'muted' },
              ]}
              footnote={
                existing.length === 0
                  ? 'Nothing tailored yet — pick a role below.'
                  : `${existing.length} role${existing.length === 1 ? '' : 's'} with a document${
                      withGaps > 0 ? `, ${withGaps} required item${withGaps === 1 ? '' : 's'} we could not evidence` : ''
                    }.`
              }
            />

            {selected && (
              <>
                <Button variant="secondary" onClick={() => setSelected(null)}>
                  ← All documents
                </Button>

                {detail.isLoading ? (
                  <Card bodyClassName="p-4">
                    <p className="text-sm text-ink-muted">Loading document…</p>
                  </Card>
                ) : detail.data ? (
                  <DocumentView document={detail.data} />
                ) : (
                  <Card bodyClassName="p-4">
                    <p className="text-sm text-ink-body">
                      {detail.error?.message ?? 'That document could not be opened.'}
                    </p>
                  </Card>
                )}
              </>
            )}

            {!selected && (
              <>
                {list.isLoading ? (
                  <Card bodyClassName="p-4">
                    <p className="text-sm text-ink-muted">Loading your documents…</p>
                  </Card>
                ) : existing.length === 0 ? (
                  <Card title="Your documents" bodyClassName="p-0">
                    <EmptyState
                      icon={<DocumentIcon className="h-9 w-9" />}
                      title="No tailored documents yet"
                    >
                      Choose a role below and we will rebuild your CV for it. The result is a
                      draft you read and approve — nothing is used anywhere until you do.
                    </EmptyState>
                  </Card>
                ) : (
                  <Card title={`Your documents (${existing.length})`} bodyClassName="p-0">
                    <ul>
                      {existing.map((item) => (
                        <li
                          key={item.id}
                          className="border-b border-border-subtle last:border-b-0"
                        >
                          <div className="flex flex-wrap items-center justify-between gap-3 p-4">
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-ink">
                                {item.roleTitle}
                                <span className="font-normal text-ink-muted">
                                  {' '}
                                  · {item.roleCompany}
                                </span>
                              </p>
                              <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                                <Pill
                                  tone={
                                    item.passedVerification
                                      ? item.status === 'approved'
                                        ? 'good'
                                        : 'info'
                                      : 'bad'
                                  }
                                >
                                  {item.passedVerification ? 'Checked' : 'Failed check'}
                                </Pill>
                                {item.uncoveredMustHave > 0 && (
                                  <Pill tone="warn">
                                    {item.uncoveredMustHave} gap
                                    {item.uncoveredMustHave === 1 ? '' : 's'}
                                  </Pill>
                                )}
                                <Pill tone="neutral">v{item.version}</Pill>
                                {item.isStale && <Pill tone="warn">CV changed</Pill>}
                              </div>
                            </div>
                            <Button
                              variant="secondary"
                              onClick={() => setSelected(item.id)}
                              className="shrink-0"
                            >
                              Open
                            </Button>
                          </div>
                        </li>
                      ))}
                    </ul>
                  </Card>
                )}

                <Card title="Tailor for a role" bodyClassName="p-4">
                  {matches.length === 0 ? (
                    <EmptyState
                      icon={<DocumentIcon className="h-9 w-9" />}
                      title="No roles to tailor for yet"
                    >
                      {dashboard.isLoading
                        ? 'Loading your roles…'
                        : 'Upload your CV and we will rank live roles against it. Tailoring opens once there is something to tailor for.'}
                    </EmptyState>
                  ) : (
                    <ul className="divide-y divide-border-subtle">
                      {matches.map((m) => {
                        const prior = byRole.get(m.roleId)
                        return (
                          <li
                            key={m.roleId}
                            className="flex flex-wrap items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
                          >
                            <div className="min-w-0">
                              <p className="truncate text-sm font-semibold text-ink">
                                {m.title}
                              </p>
                              <p className="truncate text-sm text-ink-muted">
                                {m.company}
                                {prior && (
                                  <span className="text-ink-faint"> · you have v{prior.version}</span>
                                )}
                              </p>
                            </div>
                            <div className="flex shrink-0 gap-2">
                              {prior && (
                                <Button variant="secondary" onClick={() => setSelected(prior.id)}>
                                  Open v{prior.version}
                                </Button>
                              )}
                              <Button
                                variant={prior ? 'secondary' : 'primary'}
                                onClick={() => generate.mutate(m.roleId)}
                                disabled={generate.isPending}
                              >
                                <SparkIcon className="h-4 w-4" />
                                {generate.isPending
                                  ? 'Generating…'
                                  : prior
                                    ? 'Regenerate'
                                    : 'Tailor'}
                              </Button>
                            </div>
                          </li>
                        )
                      })}
                    </ul>
                  )}

                  {generate.isError && (
                    <p role="alert" className="mt-3 text-sm font-semibold text-danger">
                      {generate.error.message}
                    </p>
                  )}
                </Card>
              </>
            )}
          </div>
        }
        right={
          <>
            <RailNote title="Nothing is invented" icon={<ShieldIcon className="h-4 w-4" />} tone="good">
              <p>
                A tailored CV is assembled from facts already in your profile. Every name,
                employer and number in the output is checked against your CV, and a document
                that fails the check cannot be approved.
              </p>
              <p>
                The practical effect: you will never send a document claiming a skill you do
                not have, because we could not write it.
              </p>
            </RailNote>

            <RailNote title="Gaps stay visible" icon={<AlertIcon className="h-4 w-4" />}>
              <p>
                When a role asks for something your CV does not show, we leave it out and say
                so, rather than writing something plausible. An empty gap you know about is
                recoverable; a claim you cannot defend is not.
              </p>
            </RailNote>

            <RailNote title="Approval is yours" icon={<CheckIcon className="h-4 w-4" />}>
              <p>
                Generation produces a draft. Nothing is treated as final until you approve
                that version, and if your CV changes afterwards the document is marked stale
                so you cannot send it by accident.
              </p>
              <p>Nothing here is dispatched anywhere. You take the text and you send it.</p>
            </RailNote>
          </>
        }
      />
    </>
  )
}
