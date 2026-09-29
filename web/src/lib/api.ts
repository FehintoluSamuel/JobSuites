/**
 * Thin API client. Two things it does that hand-rolled fetch usually gets wrong:
 *
 *  1. Normalises the backend's error shapes (ProblemDetails, and the custom
 *     `errors` field we attach for per-field messages) into one ApiError.
 *  2. Attaches the bearer token, and clears the session if the server says the
 *     token is no longer valid.
 */

export type FieldErrors = Record<string, string[]>

// Note: no TS parameter properties here — the project sets
// `erasableSyntaxOnly`, which disallows them.
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: FieldErrors

  constructor(status: number, message: string, fieldErrors: FieldErrors = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }

  /** True when the session is gone and the user must sign in again. */
  get isUnauthorized() {
    return this.status === 401
  }

  get isConflict() {
    return this.status === 409
  }
}

const TOKEN_KEY = 'jobsuites.token'

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => localStorage.removeItem(TOKEN_KEY),
}

/** Endpoints reachable without a session. Everything else is authenticated. */
const PUBLIC_PATHS = ['/api/auth/login', '/api/auth/register']

/**
 * Every authenticated request is refused at the network boundary when the
 * session is gone. Without this, a query whose observer has not yet re-rendered
 * with `enabled: false` will still fire, and the server answers 401 — which
 * looks to the user like the app is broken rather than signed out. A local 401
 * is indistinguishable from a real one to the rest of the app, so the state
 * machine stays correct either way.
 */
function assertSession(path: string) {
  if (tokenStore.get()) return
  if (PUBLIC_PATHS.includes(path)) return
  throw new ApiError(401, 'Your session has ended. Please sign in again.')
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  assertSession(path)

  const token = tokenStore.get()

  const res = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  })

  if (res.status === 204) return undefined as T

  const body = await res.json().catch(() => null)

  if (!res.ok) {
    throw new ApiError(
      res.status,
      body?.detail ?? body?.title ?? 'Something went wrong. Please try again.',
      body?.errors ?? {},
    )
  }

  return body as T
}

/** Fetch that attaches the bearer token but never sets a Content-Type, so a
    multipart upload stays a multipart upload. */
async function authedFetch(path: string, init: RequestInit = {}): Promise<Response> {
  assertSession(path)

  const token = tokenStore.get()
  return fetch(path, {
    ...init,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers,
    },
  })
}

async function parseError(res: Response): Promise<ApiError> {
  const body = await res.json().catch(() => null)
  return new ApiError(
    res.status,
    body?.detail ?? body?.title ?? 'Something went wrong. Please try again.',
    body?.errors ?? {},
  )
}

export interface User {
  id: string
  email: string
  fullName: string
  createdAt: string
}

export interface AuthResponse {
  token: string
  expiresAt: string
  user: User
}

export const auth = {
  register: (body: { email: string; fullName: string; password: string }) =>
    request<AuthResponse>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  login: (body: { email: string; password: string }) =>
    request<AuthResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  me: () => request<{ id: string; email: string; name: string }>('/api/auth/me'),
}

export interface CandidateSkill {
  name: string
  normalized: string
  years: number
  isCore: boolean
  source?: 'cv' | 'manual'
}

export interface ProfileEducation {
  school: string | null
  degree: string | null
  fieldOfStudy: string | null
  startYear: number | null
  endYear: number | null
  details: string | null
  source?: 'cv' | 'manual'
}

export interface ProfileCertification {
  name: string
  issuer: string | null
  year: number | null
  source?: 'cv' | 'manual'
}

export interface ProfileLanguage {
  name: string
  proficiency: string
  source?: 'cv' | 'manual'
}

export interface ProfileLink {
  label: string
  url: string
  source?: 'cv' | 'manual'
}

export interface ProfileCompleteness {
  percent: number
  requiredMet: number
  requiredTotal: number
  optionalMet: number
  optionalTotal: number
  missing: string[]
}

export interface CandidateProfile {
  fileName: string
  fullName: string | null
  email: string | null
  phone: string | null
  location: string | null
  headline: string | null
  yearsExperience: number | null
  summary: string | null
  profilePicture: string | null
  bannerPicture: string | null
  photoConsentGiven: boolean
  desiredSalary: string | null
  availability: string | null
  targetRoles: string[]
  preferredStates: string[]
  desiredJobTypes: string[]
  skills: CandidateSkill[]
  experiences: {
    company: string
    title: string
    startDate: string | null
    endDate: string | null
    isCurrent: boolean
    highlights: string | null
    source?: 'cv' | 'manual'
  }[]
  education: ProfileEducation[]
  certifications: ProfileCertification[]
  languages: ProfileLanguage[]
  links: ProfileLink[]
  completeness: ProfileCompleteness
  updatedAt: string
}

export interface RolePosting {
  id: string
  url: string
  location: string | null
  state: string | null
  source: string
}

export interface MatchEvidence {
  kind: string
  label: string
  detail: string
  sourceUrl: string | null
}

export interface MatchedRole {
  roleId: string
  title: string
  company: string
  tier: string
  score: number
  states: string[]
  roleSkills: string[]
  salaryEstimate: string | null
  minYears: number | null
  maxYears: number | null
  postingCount: number
  description: string | null
  evidence: MatchEvidence[]
  gaps: string[]
  postings: RolePosting[]
  postedAt: string | null
  deadlineAt: string | null
}

export interface DashboardSummary {
  rolesIngested: number
  rolesConsidered: number
  strongMatches: number
  goodMatches: number
  possibleMatches: number
  stretchMatches: number
  rolesUpdatedAt: string | null
}

export interface SourceHealth {
  key: string
  name: string
  status: 'healthy' | 'degraded' | 'blocked' | string
  lastPolledAt: string | null
  lastYield: number | null
  consecutiveZeroRuns: number
}

/** Freshness of the role board. A source that has gone quiet is shown as
 *  quiet rather than left to look like a quiet day. */
export interface IngestHealth {
  lastPolledAt: string | null
  sourcesTotal: number
  sourcesDegraded: number
  consecutiveZeroRuns: number
  sources: SourceHealth[]
}

export interface DashboardResponse {
  profile: CandidateProfile | null
  summary: DashboardSummary
  matches: MatchedRole[]
  hasIngestedRoles: boolean
  ingest: IngestHealth | null
}

export interface UploadCvResponse {
  profile: CandidateProfile
  matchesComputed: number
  skillsFound: number
  experiencesFound: number
  unchanged: boolean
}

export interface UpdateProfilePayload {
  fullName?: string | null
  email?: string | null
  phone?: string | null
  location?: string | null
  headline?: string | null
  yearsExperience?: number | null
  summary?: string | null
  desiredSalary?: string | null
  availability?: string | null
  photoConsentGiven?: boolean
  targetRoles?: string[]
  preferredStates?: string[]
  desiredJobTypes?: string[]
  skills?: CandidateSkill[]
  experiences?: CandidateProfile['experiences']
  education?: ProfileEducation[]
  certifications?: ProfileCertification[]
  languages?: ProfileLanguage[]
  links?: ProfileLink[]
}

export interface UpdateProfileResponse {
  profile: CandidateProfile
  matchesComputed: number
}

export interface ProfileMediaResponse {
  profilePicture: string | null
  bannerPicture: string | null
}

export const dashboard = {
  get: () => request<DashboardResponse>('/api/dashboard'),
}

export const roles = {
  titles: () => request<{ titles: string[]; count: number }>('/api/roles/titles'),
}

export const profile = {
  uploadCv: async (file: File): Promise<UploadCvResponse> => {
    const form = new FormData()
    form.append('file', file)

    const res = await authedFetch('/api/profile/cv', { method: 'POST', body: form })
    if (!res.ok) throw await parseError(res)
    return res.json()
  },

  remove: () => request<void>('/api/profile', { method: 'DELETE' }),

  update: (payload: UpdateProfilePayload) =>
    request<UpdateProfileResponse>('/api/profile', {
      method: 'PUT',
      body: JSON.stringify(payload),
    }),

  uploadMedia: async (kind: 'picture' | 'banner', file: File): Promise<ProfileMediaResponse> => {
    const form = new FormData()
    form.append('kind', kind)
    form.append('file', file)

    const res = await authedFetch('/api/profile/media', { method: 'POST', body: form })
    if (!res.ok) throw await parseError(res)
    return res.json()
  },

  removeMedia: (file: string) => request<ProfileMediaResponse>(`/api/profile/media/${file}`, {
    method: 'DELETE',
  }),

  mediaUrl: (file: string | null) => (file ? `/api/profile/media/${file}` : null),
}

/* ------------------------------------------------------------------ *
 * Tailoring — closed-set generation plus the fabrication check.
 * ------------------------------------------------------------------ */

export interface TailoredSection {
  heading: string
  body: string
  source: string
}

export interface TailoredChange {
  kind: string
  detail: string
  factRef: string | null
}

export interface RequirementCoverage {
  requirement: string
  mustHave: boolean
  covered: boolean
  supportingFact: string | null
}

export interface FabricatedEntity {
  text: string
  kind: string
  context: string
}

export interface FabricationResult {
  passed: boolean
  claimsChecked: number
  fabrications: FabricatedEntity[]
}

/** An AI rewrite the fabrication check refused. Diagnosis only — never the
 * stored document, and never exportable. */
export interface RejectedAiRewrite {
  document: TailoredSection[]
  verification: FabricationResult
}

export interface TailoredDocument {
  id: string
  roleId: string
  roleTitle: string
  roleCompany: string
  version: number
  status: 'draft' | 'needs_review' | 'approved' | 'rejected'
  document: TailoredSection[]
  plainText: string
  diff: TailoredChange[]
  coverage: RequirementCoverage[]
  verification: FabricationResult
  rejectedRewrite: RejectedAiRewrite | null
  factRefs: string[]
  uncoveredMustHave: number
  coveredRequirements: number
  isStale: boolean
  approvedAt: string | null
  createdAt: string
}

export interface TailoringListItem {
  id: string
  roleId: string
  roleTitle: string
  roleCompany: string
  version: number
  status: string
  passedVerification: boolean
  uncoveredMustHave: number
  isStale: boolean
  approvedAt: string | null
  createdAt: string
}

export const tailoring = {
  list: () => request<TailoringListItem[]>('/api/tailoring'),

  generate: (roleId: string) =>
    request<TailoredDocument>('/api/tailoring', {
      method: 'POST',
      body: JSON.stringify({ roleId }),
    }),

  get: (id: string) => request<TailoredDocument>(`/api/tailoring/${id}`),

  approve: (id: string, approve: boolean) =>
    request<TailoredDocument>(`/api/tailoring/${id}/approve`, {
      method: 'POST',
      body: JSON.stringify({ approve }),
    }),
}

/* ------------------------------------------------------------------ *
 * Interview prep — gap-driven questions plus the aptitude bank.
 * ------------------------------------------------------------------ */

export interface PrepQuestion {
  id: string
  kind: 'gap' | 'strength' | 'general'
  prompt: string
  gap: string
  honestFraming: string
  adjacentEvidence: string | null
}

export interface PrepResponse {
  roleId: string
  roleTitle: string
  roleCompany: string
  questions: PrepQuestion[]
  coverage: RequirementCoverage[]
  uncovered: string[]
  cached: boolean
  createdAt: string
}

export interface AssessmentItem {
  id: string
  domain: string
  difficulty: number
  prompt: string
  options: string[]
}

export interface DomainScore {
  domain: string
  correct: number
  attempted: number
  verdict: string
}

export interface PrepSession {
  id: string
  roleId: string
  roleTitle: string
  company: string
  mode: 'gap_driven' | 'aptitude'
  items: AssessmentItem[]
  scores: DomainScore[]
  startedAt: string
  finishedAt: string | null
}

export interface AptitudeItemResult {
  itemId: string
  domain: string
  correct: boolean
  answerKey: number
  workedSteps: string
}

export interface SubmitAnswerResponse {
  verdict: string
  honestFramingUsed: boolean
  item: AptitudeItemResult | null
}

export interface PrepSessionSummary {
  id: string
  roleId: string
  roleTitle: string
  company: string
  mode: string
  scores: DomainScore[]
  startedAt: string
  finishedAt: string | null
}

export const prep = {
  forRole: (roleId: string) => request<PrepResponse>(`/api/prep/roles/${roleId}`),

  sessions: () => request<PrepSessionSummary[]>('/api/prep/sessions'),

  start: (roleId: string, mode: 'gap_driven' | 'aptitude', itemCount?: number) =>
    request<PrepSession>('/api/prep/sessions', {
      method: 'POST',
      body: JSON.stringify({ roleId, mode, itemCount }),
    }),

  answerGap: (sessionId: string, questionId: string, body: string) =>
    request<SubmitAnswerResponse>(`/api/prep/sessions/${sessionId}/answers`, {
      method: 'POST',
      body: JSON.stringify({ questionId, body }),
    }),

  answerAptitude: (sessionId: string, itemId: string, selectedOption: number) =>
    request<SubmitAnswerResponse>(`/api/prep/sessions/${sessionId}/answers`, {
      method: 'POST',
      body: JSON.stringify({ itemId, selectedOption }),
    }),

  finish: (sessionId: string) =>
    request<PrepSession>(`/api/prep/sessions/${sessionId}/finish`, { method: 'POST' }),
}

/* ------------------------------------------------------------------ *
 * Applications — one record per role, never per posting.
 * ------------------------------------------------------------------ */

export interface Application {
  id: string
  roleId: string
  roleTitle: string
  company: string
  roleUrl: string
  status: 'Shortlisted' | 'Applied' | 'Interviewing' | 'Closed'
  appliedAt: string | null
  notes: string | null
  nextActionAt: string | null
  matchTier: string | null
  hasTailoredDocument: boolean
  createdAt: string
  updatedAt: string
}

export interface ApplicationSummary {
  shortlisted: number
  applied: number
  interviewing: number
  closed: number
  total: number
  items: Application[]
  nextActions: Application[]
}

export const APPLICATIONS_PIPELINE = [
  'Shortlisted',
  'Applied',
  'Interviewing',
  'Closed',
] as const

export const applications = {
  list: (status?: string) =>
    request<ApplicationSummary>(
      status ? `/api/applications?status=${encodeURIComponent(status)}` : '/api/applications',
    ),

  create: (roleId: string, body: { status?: string; notes?: string | null; nextActionAt?: string | null } = {}) =>
    request<Application>('/api/applications', {
      method: 'POST',
      body: JSON.stringify({ roleId, ...body }),
    }),

  update: (id: string, body: { status?: string; notes?: string | null; nextActionAt?: string | null }) =>
    request<Application>(`/api/applications/${id}`, {
      method: 'PATCH',
      body: JSON.stringify(body),
    }),
}

/* ------------------------------------------------------------------ *
 * Support — a data_issue ticket is a feed into adapter health.
 * ------------------------------------------------------------------ */

export interface SupportMessage {
  id: string
  authorId: string
  isMine: boolean
  body: string
  createdAt: string
}

export interface SupportTicket {
  id: string
  category: string
  subject: string
  status: string
  createdAt: string
  resolvedAt: string | null
  messages: SupportMessage[]
}

export const SUPPORT_CATEGORIES = [
  { value: 'bug', label: 'Something is broken' },
  { value: 'data_issue', label: 'A job is wrong or missing' },
  { value: 'account', label: 'Account or sign-in' },
  { value: 'feature_request', label: 'I want a feature' },
  { value: 'other', label: 'Something else' },
] as const

export const support = {
  list: () => request<SupportTicket[]>('/api/support/tickets'),

  get: (id: string) => request<SupportTicket>(`/api/support/tickets/${id}`),

  create: (body: { category: string; subject: string; body: string }) =>
    request<SupportTicket>('/api/support/tickets', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  reply: (id: string, body: string) =>
    request<SupportTicket>(`/api/support/tickets/${id}/messages`, {
      method: 'POST',
      body: JSON.stringify({ body }),
    }),
}
