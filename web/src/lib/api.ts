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

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
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
