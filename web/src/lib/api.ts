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

export interface DashboardResponse {
  profile: CandidateProfile | null
  summary: DashboardSummary
  matches: MatchedRole[]
  hasIngestedRoles: boolean
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
