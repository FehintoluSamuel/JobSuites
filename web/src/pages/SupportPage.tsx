import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { support as api, SUPPORT_CATEGORIES, type SupportTicket } from '../lib/api'
import { CheckIcon, ChatIcon, LifeBuoyIcon } from '../components/icons'
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

const CATEGORY_LABEL: Record<string, string> = Object.fromEntries(
  SUPPORT_CATEGORIES.map((c) => [c.value, c.label]),
)

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
}

const STATUS_TONE = {
  open: 'info',
  awaiting_reply: 'warn',
  resolved: 'good',
} as const

function Thread({ ticket }: { ticket: SupportTicket }) {
  const queryClient = useQueryClient()
  const [body, setBody] = useState('')

  const reply = useMutation({
    mutationFn: () => api.reply(ticket.id, body),
    onSuccess: () => {
      setBody('')
      void queryClient.invalidateQueries({ queryKey: ['support'] })
    },
  })

  return (
    <Card
      title={ticket.subject}
      className="mb-4"
      actions={
        <div className="flex items-center gap-2">
          <Pill tone={STATUS_TONE[ticket.status as keyof typeof STATUS_TONE] ?? 'neutral'}>
            {ticket.status.replace('_', ' ')}
          </Pill>
          <Button variant="secondary" onClick={() => queryClient.invalidateQueries({ queryKey: ['support'] })}>
            Refresh
          </Button>
        </div>
      }
    >
      <ol className="space-y-3">
        {ticket.messages.map((m) => (
          <li
            key={m.id}
            className={`rounded-lg p-3 ${m.isMine ? 'bg-accent-tint' : 'bg-sunken'}`}
          >
            <p className="text-xs font-semibold text-ink-muted">
              {m.isMine ? 'You' : 'Support'} ·{' '}
              {new Date(m.createdAt).toLocaleString(undefined, {
                day: 'numeric',
                month: 'short',
                hour: '2-digit',
                minute: '2-digit',
              })}
            </p>
            <p className="mt-1 whitespace-pre-wrap text-sm text-ink-body">{m.body}</p>
          </li>
        ))}
      </ol>

      {ticket.status !== 'resolved' ? (
        <form
          className="mt-4 border-t border-border-subtle pt-4"
          onSubmit={(e) => {
            e.preventDefault()
            reply.mutate()
          }}
        >
          <label htmlFor={`reply-${ticket.id}`} className="sr-only">
            Your reply
          </label>
          <textarea
            id={`reply-${ticket.id}`}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={3}
            placeholder="Add anything that helps us answer faster."
            className="w-full rounded-lg border border-border-strong bg-surface p-3 text-sm text-ink-body placeholder:text-ink-faint"
          />
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <Button type="submit" disabled={reply.isPending || body.trim().length < 2}>
              Send reply
            </Button>
            {reply.isError && (
              <p role="alert" className="text-sm font-semibold text-danger">
                {reply.error.message}
              </p>
            )}
          </div>
        </form>
      ) : (
        <p className="mt-4 flex items-center gap-2 border-t border-border-subtle pt-4 text-sm text-ink-muted">
          <CheckIcon className="h-4 w-4" />
          This ticket is resolved. Open a new one if the problem came back.
        </p>
      )}
    </Card>
  )
}

function NewTicketForm({ onCreated }: { onCreated: (id: string) => void }) {
  const queryClient = useQueryClient()
  const [category, setCategory] = useState<string>('bug')
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')

  const create = useMutation({
    mutationFn: () => api.create({ category, subject, body }),
    onSuccess: (ticket) => {
      setSubject('')
      setBody('')
      void queryClient.invalidateQueries({ queryKey: ['support'] })
      onCreated(ticket.id)
    },
  })

  return (
    <Card title="Raise a ticket" className="mb-4">
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault()
          create.mutate()
        }}
      >
        <div>
          <label
            htmlFor="ticket-category"
            className="mb-1 block text-sm font-semibold text-ink-body"
          >
            What is this about?
          </label>
          <select
            id="ticket-category"
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            className="w-full rounded-lg border border-border-strong bg-surface p-2.5 text-sm text-ink-body"
          >
            {SUPPORT_CATEGORIES.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="ticket-subject" className="mb-1 block text-sm font-semibold text-ink-body">
            One-line summary
          </label>
          <input
            id="ticket-subject"
            value={subject}
            onChange={(e) => setSubject(e.target.value)}
            placeholder="Tailored CV for the Data Analyst role is missing my Excel row"
            className="w-full rounded-lg border border-border-strong bg-surface p-2.5 text-sm text-ink-body placeholder:text-ink-faint"
          />
        </div>

        <div>
          <label htmlFor="ticket-body" className="mb-1 block text-sm font-semibold text-ink-body">
            Details
          </label>
          <textarea
            id="ticket-body"
            value={body}
            onChange={(e) => setBody(e.target.value)}
            rows={4}
            placeholder="What did you expect, and what happened instead? Which role, if any, is involved?"
            className="w-full rounded-lg border border-border-strong bg-surface p-2.5 text-sm text-ink-body placeholder:text-ink-faint"
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            type="submit"
            disabled={create.isPending || subject.trim().length < 3 || body.trim().length < 10}
          >
            Send ticket
          </Button>
          {create.isError && (
            <p role="alert" className="text-sm font-semibold text-danger">
              {create.error.message}
            </p>
          )}
        </div>
      </form>
    </Card>
  )
}

export function SupportPage() {
  const [selected, setSelected] = useState<string | null>(null)
  const [showForm, setShowForm] = useState(false)

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['support'],
    queryFn: api.list,
  })

  const tickets = data ?? []
  const open = tickets.filter((t) => t.status !== 'resolved')
  const closed = tickets.filter((t) => t.status === 'resolved')
  const shown = selected ? tickets.filter((t) => t.id === selected) : []
  const answered = tickets.filter((t) => t.messages.some((m) => !m.isMine))

  return (
    <>
      <PageHeader
        title="Help"
        lede="Tell us what is wrong and we will read the actual record behind it. For a bad match or a missing job, the ticket is the fastest route to a fix."
      />

      {isError && (
        <Card className="mb-4" bodyClassName="p-4">
          <p role="alert" className="text-sm font-semibold text-danger">
            {error.message}
          </p>
        </Card>
      )}

      {selected ? (
        <>
          <Button variant="secondary" onClick={() => setSelected(null)} className="mb-3">
            ← All tickets
          </Button>
          {shown.map((t) => (
            <Thread key={t.id} ticket={t} />
          ))}
        </>
      ) : (
        <AppColumns
          center={
            <div className="space-y-4">
              {showForm ? (
                <NewTicketForm
                  onCreated={(id) => {
                    setSelected(id)
                    setShowForm(false)
                  }}
                />
              ) : (
                <Card bodyClassName="p-4">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <h2 className="text-base font-semibold text-ink">Something not right?</h2>
                      <p className="mt-0.5 text-sm text-ink-body">
                        A ticket keeps the conversation attached to the record, so you do not
                        have to re-explain it.
                      </p>
                    </div>
                    <Button onClick={() => setShowForm(true)}>
                      <LifeBuoyIcon className="h-4 w-4" />
                      Raise a ticket
                    </Button>
                  </div>
                </Card>
              )}

              {isLoading ? (
                <Card bodyClassName="p-4">
                  <p className="text-sm text-ink-muted">Loading your tickets…</p>
                </Card>
              ) : tickets.length === 0 ? (
                <Card title="Your tickets" bodyClassName="p-0">
                  <EmptyState
                    icon={<ChatIcon className="h-9 w-9" />}
                    title="No tickets yet"
                    action={
                      <Button variant="secondary" onClick={() => setShowForm(true)}>
                        Raise your first ticket
                      </Button>
                    }
                  >
                    If a role is missing, a match makes no sense, or something in your profile
                    is being read incorrectly, raise one and we will look at the underlying
                    data rather than guessing from your description.
                  </EmptyState>
                </Card>
              ) : (
                <>
                  {open.length > 0 && (
                    <Card title={`Open (${open.length})`} bodyClassName="p-0">
                      <ul>
                        {open.map((t) => (
                          <li
                            key={t.id}
                            className="border-b border-border-subtle last:border-b-0"
                          >
                            <button
                              type="button"
                              onClick={() => setSelected(t.id)}
                              className="block w-full p-4 text-left transition-colors hover:bg-sunken"
                            >
                              <p className="text-sm font-semibold text-ink">{t.subject}</p>
                              <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                                <Pill
                                  tone={
                                    STATUS_TONE[t.status as keyof typeof STATUS_TONE] ?? 'neutral'
                                  }
                                >
                                  {t.status.replace('_', ' ')}
                                </Pill>
                                <Pill tone="neutral">
                                  {CATEGORY_LABEL[t.category] ?? t.category}
                                </Pill>
                                <span className="text-xs text-ink-muted">
                                  {t.messages.length} message{t.messages.length === 1 ? '' : 's'} ·
                                  opened{' '}
                                  {new Date(t.createdAt).toLocaleDateString(undefined, {
                                    day: 'numeric',
                                    month: 'short',
                                  })}
                                </span>
                              </div>
                            </button>
                          </li>
                        ))}
                      </ul>
                    </Card>
                  )}

                  {closed.length > 0 && (
                    <Card title={`Resolved (${closed.length})`} bodyClassName="p-0">
                      <ul>
                        {closed.map((t) => (
                          <li
                            key={t.id}
                            className="border-b border-border-subtle last:border-b-0"
                          >
                            <button
                              type="button"
                              onClick={() => setSelected(t.id)}
                              className="block w-full p-4 text-left transition-colors hover:bg-sunken"
                            >
                              <p className="text-sm font-semibold text-ink-muted">
                                {t.subject}
                              </p>
                              <p className="mt-0.5 text-xs text-ink-muted">
                                Resolved {t.resolvedAt ? formatDate(t.resolvedAt) : ''}
                              </p>
                            </button>
                          </li>
                        ))}
                      </ul>
                    </Card>
                  )}
                </>
              )}
            </div>
          }
          right={
            <>
              <StatTiles
                label="Your tickets"
                tiles={[
                  { label: 'Open', value: open.length, tone: 'info' },
                  { label: 'Resolved', value: closed.length, tone: 'muted' },
                  { label: 'All', value: tickets.length },
                  {
                    label: 'Answered',
                    value: answered.length,
                    tone: answered.length > 0 ? 'good' : 'muted',
                  },
                ]}
                footnote={
                  tickets.length === 0
                    ? 'Nothing raised yet.'
                    : `${tickets.length} ticket${tickets.length === 1 ? '' : 's'}${
                        open.length > 0 ? `, ${open.length} still open` : ', all resolved'
                      }.`
                }
              />

              <RailNote title="What to expect" icon={<ChatIcon className="h-4 w-4" />}>
                <p>
                  Every ticket is read by a person with access to your actual records — the
                  posting, the parsed CV and the match score — so include the role or the
                  wording that looks wrong.
                </p>
                <p>
                  Replies land on the ticket. If something is fixed on our side we will say
                  what changed rather than closing it silently.
                </p>
              </RailNote>

              <RailNote title="Before you write" icon={<CheckIcon className="h-4 w-4" />}>
                <p>
                  Match scores re-run automatically when your CV changes, so a score that
                  looks stale usually means an upload did not finish.
                </p>
                <p>
                  A role that is genuinely missing from your matches is worth a ticket with
                  the title and the board you found it on.
                </p>
              </RailNote>
            </>
          }
        />
      )}
    </>
  )
}
