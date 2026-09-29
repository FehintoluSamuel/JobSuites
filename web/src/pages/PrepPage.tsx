import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  dashboard as dashboardApi,
  prep as api,
  type PrepQuestion,
  type PrepResponse,
  type PrepSession,
  type SubmitAnswerResponse,
} from '../lib/api'
import { AlertIcon, CheckIcon, ChatIcon, ShieldIcon } from '../components/icons'
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

/** Written practice for the gap questions. Scoring finishes the session the
    candidate actually answered, rather than opening a second one. */
function GapPractice({
  session,
  questions,
  onFinished,
}: {
  session: PrepSession
  questions: PrepQuestion[]
  onFinished: (s: PrepSession) => void
}) {
  const [answers, setAnswers] = useState<Record<string, SubmitAnswerResponse>>({})
  const finish = useMutation({
    mutationFn: () => api.finish(session.id),
    onSuccess: onFinished,
  })

  const answered = Object.keys(answers).length
  const flagged = Object.values(answers).filter((r) => !r.honestFramingUsed).length

  return (
    <Card
      title="Practise your answers"
      className="mt-4"
      bodyClassName="p-0"
      actions={
        <Pill tone={answered === questions.length ? 'good' : 'neutral'}>
          {answered} of {questions.length} written
        </Pill>
      }
    >
      <ul>
        {questions.map((q) => (
          <GapQuestion
            key={q.id}
            question={q}
            sessionId={session.id}
            onAnswered={(r) => setAnswers((a) => ({ ...a, [q.id]: r }))}
          />
        ))}
      </ul>

      <div className="border-t border-border-subtle p-4">
        {finish.data ? (
          <div>
            <h3 className="text-sm font-semibold text-ink">Session scored</h3>
            <ul className="mt-2 space-y-2">
              {finish.data.scores.map((s) => (
                <li
                  key={s.domain}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border-subtle p-3"
                >
                  <span className="text-sm font-semibold capitalize text-ink">{s.domain}</span>
                  <span className="text-sm text-ink-body">
                    {s.correct}/{s.attempted}{' '}
                    <Pill
                      tone={
                        s.verdict === 'Strong' ? 'good' : s.verdict === 'Stretch' ? 'warn' : 'neutral'
                      }
                    >
                      {s.verdict}
                    </Pill>
                  </span>
                </li>
              ))}
            </ul>
            <p className="mt-3 text-sm text-ink-muted">
              {flagged > 0
                ? `${flagged} answer${flagged === 1 ? '' : 's'} claimed the missing skill. Rewrite ${
                    flagged === 1 ? 'it' : 'them'
                  } before the interview — an interviewer who checks will find it.`
                : 'Nothing flagged. You described what you have done rather than claiming what you have not.'}
            </p>
          </div>
        ) : (
          <div className="flex flex-wrap items-center gap-2">
            <Button onClick={() => finish.mutate()} disabled={finish.isPending || answered === 0}>
              {finish.isPending ? 'Scoring…' : 'Score this session'}
            </Button>
            {answered === 0 && (
              <span className="text-sm text-ink-muted">Write at least one answer first.</span>
            )}
            {finish.isError && (
              <span role="alert" className="text-sm font-semibold text-danger">
                {finish.error.message}
              </span>
            )}
          </div>
        )}
      </div>
    </Card>
  )
}

/**
 * One gap question.
 *
 * The honest-framing note is rendered above the textarea, not hidden behind a
 * disclosure. The guardrail in docs/PRODUCT.md §7.4 is that a candidate coached
 * into claiming experience they do not have loses the interview, so the advice
 * against it has to be in front of them while they write, not after.
 */
function GapQuestion({
  question,
  sessionId,
  onAnswered,
}: {
  question: PrepQuestion
  sessionId: string
  onAnswered: (r: SubmitAnswerResponse) => void
}) {
  const [body, setBody] = useState('')
  const [result, setResult] = useState<SubmitAnswerResponse | null>(null)

  const answer = useMutation({
    mutationFn: () => api.answerGap(sessionId, question.id, body),
    onSuccess: (r) => {
      setResult(r)
      onAnswered(r)
    },
  })

  return (
    <li className="border-b border-border-subtle p-4 last:border-b-0">
      <div className="flex flex-wrap items-center gap-2">
        <Pill tone={question.kind === 'gap' ? 'warn' : 'good'}>
          {question.kind === 'gap' ? 'A gap they will probe' : 'What you can lead with'}
        </Pill>
      </div>

      <h3 className="mt-2 text-base font-semibold text-ink">{question.prompt}</h3>

      <p className="mt-1 text-sm text-ink-muted">{question.gap}</p>

      <div className="mt-3 flex items-start gap-2.5 rounded-lg border border-link/25 bg-canvas p-3">
        <ShieldIcon className="mt-0.5 h-4 w-4 shrink-0 text-link" />
        <p className="text-sm text-ink-body">
          {question.honestFraming}
          {question.adjacentEvidence && (
            <>
              {' '}
              <span className="font-semibold text-ink">
                Closest thing on your CV: {question.adjacentEvidence}.
              </span>
            </>
          )}
        </p>
      </div>

      {result ? (
        <div
          className={`mt-3 rounded-lg p-3 text-sm ${
            result.honestFramingUsed ? 'bg-success-tint text-success' : 'bg-warn-tint text-warn'
          }`}
        >
          <p className="font-semibold">{result.verdict}</p>
          {!result.honestFramingUsed && (
            <p className="mt-1 text-ink-body">
              Your answer claims the missing skill. Rewrite it to lead with what you have
              actually done — an interviewer who checks will find the gap, and it costs you
              the role.
            </p>
          )}
        </div>
      ) : (
        <form
          className="mt-3"
          onSubmit={(e) => {
            e.preventDefault()
            answer.mutate()
          }}
        >
          <label htmlFor={`answer-${question.id}`} className="sr-only">
            Your answer
          </label>
          <textarea
            id={`answer-${question.id}`}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={4}
            placeholder="Answer it out loud, roughly as you would in the room."
            className="w-full rounded-lg border border-border-strong bg-surface p-3 text-sm text-ink-body placeholder:text-ink-faint"
          />
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <Button type="submit" disabled={answer.isPending || body.trim().length < 10}>
              Check this answer
            </Button>
            {answer.isError && (
              <p role="alert" className="text-sm font-semibold text-danger">
                {answer.error.message}
              </p>
            )}
          </div>
        </form>
      )}
    </li>
  )
}

function AptitudeSession({ session, roleTitle }: { session: PrepSession; roleTitle: string }) {
  const [index, setIndex] = useState(0)
  const [picked, setPicked] = useState<number | null>(null)
  const [result, setResult] = useState<SubmitAnswerResponse | null>(null)
  const [answers, setAnswers] = useState<Record<string, SubmitAnswerResponse>>({})

  const finish = useMutation({ mutationFn: () => api.finish(session.id) })

  const item = session.items[index]

  const submit = useMutation({
    mutationFn: (option: number) => api.answerAptitude(session.id, item.id, option),
    onSuccess: (r) => {
      setResult(r)
      setAnswers((a) => ({ ...a, [item.id]: r }))
    },
  })

  if (session.items.length === 0) {
    return (
      <Card title="Aptitude practice">
        <p className="text-sm text-ink-body">No items were served for this session.</p>
      </Card>
    )
  }

  const isLast = index === session.items.length - 1
  const scored = Object.keys(answers).length

  return (
    <Card
      title={`Aptitude practice — ${roleTitle}`}
      actions={<Pill tone="neutral">{index + 1} of {session.items.length}</Pill>}
    >
      {!finish.data && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <Pill tone="info">{item.domain}</Pill>
            <Pill tone="neutral">
              {['Foundational', 'Intermediate', 'Advanced'][item.difficulty - 1] ?? 'Advanced'}
            </Pill>
          </div>

          <p className="mt-3 text-base font-semibold text-ink">{item.prompt}</p>

          <ul className="mt-3 space-y-2">
            {item.options.map((option, i) => {
              const isAnswer = result?.item?.answerKey === i
              const isPicked = picked === i
              return (
                <li key={i}>
                  <button
                    type="button"
                    disabled={Boolean(result)}
                    onClick={() => setPicked(i)}
                    className={`flex w-full items-center gap-3 rounded-lg border p-3 text-left text-sm transition-colors disabled:cursor-not-allowed ${
                      isAnswer
                        ? 'border-success bg-success-tint font-semibold text-success'
                        : isPicked
                          ? 'border-accent bg-accent-tint text-ink'
                          : 'border-border-strong bg-surface text-ink-body hover:bg-sunken'
                    }`}
                  >
                    <span
                      className={`flex h-6 w-6 shrink-0 items-center justify-center rounded-full border text-xs font-bold ${
                        isAnswer
                          ? 'border-success bg-success text-white'
                          : 'border-border-strong text-ink-muted'
                      }`}
                    >
                      {String.fromCharCode(65 + i)}
                    </span>
                    {option}
                  </button>
                </li>
              )
            })}
          </ul>

          {result && (
            <div
              className={`mt-3 rounded-lg p-3 text-sm ${
                result.item?.correct ? 'bg-success-tint text-success' : 'bg-danger-tint text-danger'
              }`}
            >
              <p className="font-semibold">
                {result.item?.correct ? 'Correct' : 'Not quite'}
              </p>
              <p className="mt-1 text-ink-body">{result.item?.workedSteps}</p>
            </div>
          )}

          <div className="mt-4 flex flex-wrap gap-2">
            {!result ? (
              <Button onClick={() => picked !== null && submit.mutate(picked)} disabled={picked === null}>
                Check answer
              </Button>
            ) : isLast ? (
              <Button onClick={() => finish.mutate()}>Finish and score</Button>
            ) : (
              <Button onClick={() => setIndex((i) => i + 1)}>Next question</Button>
            )}
            {finish.data && <Pill tone="good">Session scored</Pill>}
          </div>
        </>
      )}

      {finish.data && (
        <div>
          <h3 className="text-sm font-semibold text-ink">Result</h3>
          <p className="mt-1 text-sm text-ink-body">
            You answered {scored} of {session.items.length}.
          </p>
          <ul className="mt-3 space-y-2">
            {finish.data.scores.map((s) => (
              <li
                key={s.domain}
                className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border-subtle p-3"
              >
                <span className="text-sm font-semibold capitalize text-ink">{s.domain}</span>
                <span className="text-sm text-ink-body">
                  {s.correct}/{s.attempted} · <Pill tone="neutral">{s.verdict}</Pill>
                </span>
              </li>
            ))}
          </ul>
          <p className="mt-3 text-sm text-ink-muted">
            We score these as Strong / Good / Possible / Stretch rather than a percentage. The
            bank is small and hand-written, so a number off it would be pretending to a
            precision we do not have.
          </p>
        </div>
      )}
    </Card>
  )
}

export function PrepPage() {
  const [roleId, setRoleId] = useState<string | null>(null)
  const [session, setSession] = useState<PrepSession | null>(null)

  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: dashboardApi.get })
  const matches = dashboard.data?.matches ?? []

  const questions = useQuery({
    queryKey: ['prep', roleId],
    queryFn: () => api.forRole(roleId!),
    enabled: Boolean(roleId),
  })

  const startSession = useMutation({
    mutationFn: (mode: 'gap_driven' | 'aptitude') => api.start(roleId!, mode),
    onSuccess: setSession,
  })

  const history = useQuery({ queryKey: ['prep-sessions'], queryFn: () => api.sessions() })

  const selected = matches.find((m) => m.roleId === roleId)
  const prep: PrepResponse | undefined = questions.data
  const sessions = history.data ?? []
  const gapCount = prep?.questions.filter((q) => q.kind === 'gap').length ?? 0
  const done = sessions.filter((s) => s.finishedAt).length

  return (
    <>
      <PageHeader
        title="Interview prep"
        lede="Questions built from the gap between this job description and your CV. A generic prep site has the posting; this also has your CV and the computed difference, which is the only thing that makes a question specific."
      />

      <AppColumns
        center={
          <div className="space-y-4">
            {roleId && (
              <Button
                variant="secondary"
                onClick={() => {
                  setRoleId(null)
                  setSession(null)
                }}
              >
                ← All roles
              </Button>
            )}

            {!roleId && (
              <Card title="Choose a role" bodyClassName="p-4">
                {matches.length === 0 ? (
                  <EmptyState
                    icon={<ChatIcon className="h-9 w-9" />}
                    title="No roles to prepare for yet"
                  >
                    {dashboard.isLoading
                      ? 'Loading your roles…'
                      : 'Upload your CV and we will rank live roles against it. Prep is built per role, from what that posting wants and what your CV shows.'}
                  </EmptyState>
                ) : (
                  <ul className="grid gap-2 sm:grid-cols-2">
                    {matches.map((m) => (
                      <li key={m.roleId}>
                        <button
                          type="button"
                          onClick={() => {
                            setRoleId(m.roleId)
                            setSession(null)
                          }}
                          className="w-full rounded-lg border border-border-subtle bg-surface p-3 text-left transition-colors hover:border-border-strong hover:bg-sunken"
                        >
                          <span className="block truncate text-sm font-semibold text-ink">
                            {m.title}
                          </span>
                          <span className="mt-0.5 block truncate text-sm text-ink-muted">
                            {m.company}
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            )}

            {roleId && questions.isLoading && (
              <Card bodyClassName="p-4">
                <p className="text-sm text-ink-muted">Working out the gaps…</p>
              </Card>
            )}

            {roleId && questions.isError && (
              <Card bodyClassName="p-4">
                <p role="alert" className="text-sm font-semibold text-danger">
                  {questions.error.message}
                </p>
              </Card>
            )}

            {prep && (
              <>
                <StatTiles
                  label={`Prep for ${prep.roleTitle}`}
                  tiles={[
                    { label: 'Questions', value: prep.questions.length },
                    { label: 'Gaps to probe', value: gapCount, tone: gapCount > 0 ? 'info' : 'good' },
                    { label: 'Strengths', value: prep.questions.length - gapCount, tone: 'good' },
                    { label: 'Uncovered asks', value: prep.uncovered.length, tone: 'muted' },
                  ]}
                  footnote={
                    prep.cached
                      ? 'Built for you on an earlier visit and served from cache.'
                      : 'Built for you just now from the difference between this posting and your CV.'
                  }
                />

                <Card
                  title="What this role will probe"
                  actions={
                    <div className="flex gap-2">
                      <Button
                        variant="secondary"
                        onClick={() => startSession.mutate('aptitude')}
                        disabled={startSession.isPending}
                      >
                        Aptitude test
                      </Button>
                      <Button
                        onClick={() => startSession.mutate('gap_driven')}
                        disabled={startSession.isPending}
                      >
                        Practise writing
                      </Button>
                    </div>
                  }
                  bodyClassName="p-4"
                >
                  {prep.uncovered.length > 0 ? (
                    <div className="rounded-lg border border-warn/40 bg-warn-tint p-3">
                      <h3 className="flex items-center gap-1.5 text-sm font-semibold text-warn">
                        <AlertIcon className="h-4 w-4" />
                        {prep.uncovered.length} thing
                        {prep.uncovered.length === 1 ? '' : 's'} this role asks for that your CV
                        does not show
                      </h3>
                      <ul className="mt-1.5 list-disc space-y-0.5 pl-5 text-sm text-ink-body">
                        {prep.uncovered.map((u) => (
                          <li key={u}>{u}</li>
                        ))}
                      </ul>
                      <p className="mt-2 text-sm text-ink-body">
                        Prepare to be asked about {prep.uncovered.length === 1 ? 'it' : 'them'}.
                        Answer from what you have actually done and say plainly where the
                        experience is thin — interviewers probe gaps, and an answer that
                        claims the missing skill collapses under one follow-up question.
                      </p>
                    </div>
                  ) : (
                    <div className="rounded-lg border border-success/30 bg-success-tint p-3">
                      <h3 className="flex items-center gap-1.5 text-sm font-semibold text-success">
                        <CheckIcon className="h-4 w-4" />
                        Your CV evidences everything this posting asks for
                      </h3>
                    </div>
                  )}

                  {startSession.isError && (
                    <p role="alert" className="mt-3 text-sm font-semibold text-danger">
                      {startSession.error.message}
                    </p>
                  )}
                </Card>

                {session && session.mode === 'aptitude' ? (
                  <AptitudeSession
                    session={session}
                    roleTitle={selected?.title ?? prep.roleTitle}
                  />
                ) : (
                  <Card title="Your questions" bodyClassName="p-0">
                    <ul>
                      {prep.questions.map((q) => (
                        <li key={q.id} className="border-b border-border-subtle p-4 last:border-b-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <Pill tone={q.kind === 'gap' ? 'warn' : 'good'}>
                              {q.kind === 'gap' ? 'Gap they will probe' : 'Lead with this'}
                            </Pill>
                          </div>
                          <p className="mt-2 text-sm font-semibold text-ink">{q.prompt}</p>
                          <p className="mt-1 text-sm text-ink-muted">{q.gap}</p>
                          <p className="mt-2 rounded-lg bg-canvas p-3 text-sm text-ink-body">
                            <span className="font-semibold text-ink">Answer it honestly: </span>
                            {q.honestFraming}
                          </p>
                        </li>
                      ))}
                    </ul>
                  </Card>
                )}

                {session && session.mode === 'gap_driven' && (
                  <GapPractice
                    session={session}
                    questions={prep.questions}
                    onFinished={(s) => {
                      setSession(s)
                      void history.refetch()
                    }}
                  />
                )}
              </>
            )}
          </div>
        }
        right={
          <>
            <StatTiles
              label="Practice"
              tiles={[
                { label: 'Sessions', value: sessions.length },
                { label: 'Finished', value: done, tone: 'good' },
                {
                  label: 'In progress',
                  value: sessions.length - done,
                  tone: sessions.length > done ? 'info' : 'muted',
                },
                { label: 'Roles', value: matches.length, tone: 'muted' },
              ]}
              footnote={
                sessions.length === 0
                  ? 'No practice sessions yet.'
                  : `${sessions.length} session${sessions.length === 1 ? '' : 's'}, ${done} finished.`
              }
            />

            <RailNote title="The honesty rule" icon={<ShieldIcon className="h-4 w-4" />} tone="good">
              <p>
                Every question carries the answer shape we want you to use: name the adjacent
                work, explain what transfers, and state the gap. This is a hard rule in the
                product, not a style preference.
              </p>
              <p>
                Written practice is checked against it. An answer that claims the missing
                skill is flagged so you can rewrite it before an interviewer does it for you.
              </p>
            </RailNote>

            <RailNote title="Aptitude test" icon={<CheckIcon className="h-4 w-4" />}>
              <p>
                A hand-written bank across four domains, scored as Strong / Good / Possible /
                Stretch. We do not give you a percentage, because a small hand-written bank
                does not justify one.
              </p>
              <p>
                Scoring uses the same vocabulary as your match tiers on purpose — one scale to
                learn, not two.
              </p>
            </RailNote>

            {sessions.length > 0 && (
              <Card title="Past sessions" bodyClassName="p-4">
                <ul className="space-y-2">
                  {sessions.slice(0, 8).map((s) => (
                    <li key={s.id} className="text-sm">
                      <p className="font-semibold text-ink">{s.roleTitle}</p>
                      <p className="text-xs text-ink-muted">
                        {s.mode === 'aptitude' ? 'Aptitude' : 'Written practice'} ·{' '}
                        {s.finishedAt
                          ? s.scores.map((sc) => `${sc.domain}: ${sc.verdict}`).join(' · ')
                          : 'In progress'}
                      </p>
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </>
        }
      />
    </>
  )
}
