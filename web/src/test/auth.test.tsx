/**
 * Sprint 1 acceptance tests — the auth vertical slice.
 *
 * These assert the accessibility contract from docs/UX-AUTH.md, not just that
 * the happy path renders. A login screen that is keyboard-hostile is broken even
 * if every test here passes, so the a11y assertions are the point.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../auth/AuthContext'
import { LoginPage } from '../pages/LoginPage'
import { SignupPage } from '../pages/SignupPage'
import { tokenStore } from '../lib/api'

function renderPage(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <AuthProvider>{ui}</AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

describe('LoginPage', () => {
  it('labels every input — no placeholder-only fields', () => {
    renderPage(<LoginPage />)
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument()
  })

  it('uses autocomplete hints so password managers can fill it in', () => {
    renderPage(<LoginPage />)
    expect(screen.getByLabelText(/email/i)).toHaveAttribute('autocomplete', 'email')
    expect(screen.getByLabelText(/password/i)).toHaveAttribute('autocomplete', 'current-password')
  })

  it('allows pasting into the password field (WCAG 2.2 AA)', () => {
    renderPage(<LoginPage />)
    const pw = screen.getByLabelText(/password/i) as HTMLInputElement
    // No onpaste handler that blocks, and no readonly/maxlength trap.
    expect(pw).not.toHaveAttribute('readonly')
    expect(pw).not.toHaveAttribute('onpaste')
  })

  it('is fully operable with the keyboard', async () => {
    const user = userEvent.setup()
    renderPage(<LoginPage />)

    const email = screen.getByLabelText(/email/i)
    await user.click(email)
    await user.keyboard('ada@example.com')
    await user.tab()
    await user.keyboard('hunter2hunter2')
    await user.tab()

    expect(screen.getByRole('button', { name: /sign in/i })).toHaveFocus()
  })

  it('surfaces a focusable error summary when the server returns field errors', async () => {
    const user = userEvent.setup()

    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 400,
        json: async () => ({
          title: 'Check the form',
          detail: 'Some fields need attention.',
          errors: { email: ['Enter a valid email address.'] },
        }),
      }),
    )

    renderPage(<LoginPage />)
    await user.type(screen.getByLabelText(/email/i), 'nope')
    await user.type(screen.getByLabelText(/password/i), 'whatever12')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    const summary = await screen.findByTestId('error-summary')
    expect(summary).toHaveAttribute('role', 'alert')
    expect(summary).toHaveAttribute('tabindex', '-1')
    await waitFor(() => expect(summary).toHaveFocus())

    // The summary must link to the field, not just describe the problem.
    expect(screen.getByRole('link', { name: /valid email/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toHaveAttribute('aria-invalid', 'true')
  })

  it('does not leak whether an email is registered', async () => {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 401,
        json: async () => ({ title: 'Sign-in failed', detail: 'Email or password is incorrect.' }),
      }),
    )

    renderPage(<LoginPage />)
    await user.type(screen.getByLabelText(/email/i), 'nobody@example.com')
    await user.type(screen.getByLabelText(/password/i), 'wrongwrongwrong')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText(/email or password is incorrect/i)).toBeInTheDocument()
    expect(screen.queryByText(/no such user|unknown email|not registered/i)).toBeNull()
  })

  it('disables the button and announces progress while submitting', async () => {
    const user = userEvent.setup()
    let release: (v: unknown) => void = () => {}
    vi.stubGlobal(
      'fetch',
      vi.fn().mockReturnValue(new Promise((res) => (release = res))),
    )

    renderPage(<LoginPage />)
    await user.type(screen.getByLabelText(/email/i), 'ada@example.com')
    await user.type(screen.getByLabelText(/password/i), 'good-password-1!')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    const btn = await screen.findByRole('button', { name: /working/i })
    expect(btn).toBeDisabled()
    expect(btn).toHaveAttribute('aria-busy', 'true')

    release({ ok: true, status: 200, json: async () => ({}) })
  })

  it('shows a clear failure state when the API is unreachable', async () => {
    const user = userEvent.setup()
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    renderPage(<LoginPage />)
    await user.type(screen.getByLabelText(/email/i), 'ada@example.com')
    await user.type(screen.getByLabelText(/password/i), 'good-password-1!')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText(/could not reach the server/i)).toBeInTheDocument()
  })
})

describe('SignupPage', () => {
  it('states the password policy before the user types', () => {
    renderPage(<SignupPage />)
    expect(screen.getByText(/at least 10 characters/i)).toBeInTheDocument()
  })

  it('marks a newly created account as new-password, not current-password', () => {
    renderPage(<SignupPage />)
    expect(screen.getByLabelText(/^password$/i)).toHaveAttribute('autocomplete', 'new-password')
  })

  it('handles a duplicate email without a dead-end', async () => {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 409,
        json: async () => ({
          title: 'Account exists',
          detail: 'An account with that email already exists. Try signing in instead.',
        }),
      }),
    )

    renderPage(<SignupPage />)
    await user.type(screen.getByLabelText(/full name/i), 'Ada Lovelace')
    await user.type(screen.getByLabelText(/email/i), 'ada@example.com')
    await user.type(screen.getByLabelText(/^password$/i), 'good-password-1!')
    await user.click(screen.getByRole('button', { name: /create account/i }))

    expect(await screen.findByText(/already exists/i)).toBeInTheDocument()
    // The recovery path must be offered, not just the problem.
    expect(screen.getByRole('link', { name: /sign in/i })).toBeInTheDocument()
  })

  it('has exactly one h1 and a navigable heading structure', () => {
    renderPage(<SignupPage />)
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
  })
})
