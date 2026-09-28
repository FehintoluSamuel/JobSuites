/**
 * Sign-out regression tests.
 *
 * Signing out used to leave the session half-torn-down: the `me` query stayed
 * populated, so the route guard never fired, and the queries that were cleared
 * got rebuilt and refetched by their still-mounted observers — producing a
 * 401 per query (plus a retry) in the console while the dashboard stayed on
 * screen. These assert the whole teardown, not just that the token is gone.
 *
 * Sign-out lives in the account menu, as it does in the LinkedIn layout this
 * app follows, so each test opens that menu first.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../auth/AuthContext'
import { DashboardPage } from '../pages/DashboardPage'
import { LoginPage } from '../pages/LoginPage'
import { tokenStore, type DashboardResponse } from '../lib/api'

const me = { id: '1', email: 'ada@example.com', name: 'Ada Obi' }

const emptyDashboard: DashboardResponse = {
  profile: null,
  summary: {
    rolesIngested: 23,
    rolesConsidered: 0,
    strongMatches: 0,
    goodMatches: 0,
    possibleMatches: 0,
    stretchMatches: 0,
    rolesUpdatedAt: '2026-09-28T10:00:00Z',
  },
  matches: [],
  hasIngestedRoles: true,
}

type Call = { url: string; hadToken: boolean }

function setup() {
  tokenStore.set('test-token')

  const client = new QueryClient({
    defaultOptions: { queries: { retry: 1, refetchOnWindowFocus: false }, mutations: { retry: false } },
  })

  const calls: Call[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      const hadToken = Boolean(tokenStore.get())
      calls.push({ url, hadToken })

      const authed = hadToken && !url.includes('/api/auth/login')
      if (url.includes('/api/auth/me') || url.includes('/api/dashboard')) {
        if (!authed) return { ok: false, status: 401, json: async () => ({ title: 'No' }) }
        return {
          ok: true,
          status: 200,
          json: async () => (url.includes('/api/auth/me') ? me : emptyDashboard),
        }
      }
      return { ok: false, status: 404, json: async () => null }
    }),
  )

  const view = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/app']}>
        <AuthProvider>
          <Routes>
            <Route path="/app" element={<DashboardPage />} />
            <Route path="/login" element={<LoginPage />} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )

  return { calls, view, client }
}

beforeEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

/** Opens the account menu, which is where sign-out lives. */
async function openAccountMenu(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /account menu/i }))
  return screen.findByRole('menu')
}

describe('sign out', () => {
  it('clears the token and the cached user, and returns to the login screen', async () => {
    const user = userEvent.setup()
    setup()

    await screen.findByRole('heading', { name: /upload your cv/i })

    const menu = await openAccountMenu(user)
    expect(menu).toHaveTextContent('ada@example.com')

    await user.click(screen.getByRole('menuitem', { name: /sign out/i }))

    await waitFor(() => expect(tokenStore.get()).toBeNull())
    await waitFor(() => expect(screen.getByRole('button', { name: /^sign in$/i })).toBeInTheDocument())
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('fires no request that can 401 after the session is gone', async () => {
    const user = userEvent.setup()
    const { calls } = setup()

    await screen.findByRole('heading', { name: /upload your cv/i })

    await openAccountMenu(user)
    await user.click(screen.getByRole('menuitem', { name: /sign out/i }))
    await waitFor(() => expect(tokenStore.get()).toBeNull())
    await new Promise((r) => setTimeout(r, 250))

    // Any request issued once the token is gone would be answered 401.
    const unauthenticated = calls.filter((c) => !c.hadToken)
    expect(unauthenticated).toEqual([])
  })

  it('leaves nothing behind in the cache for the next person to sign in', async () => {
    const user = userEvent.setup()
    const { client } = setup()

    await screen.findByRole('heading', { name: /upload your cv/i })

    await openAccountMenu(user)
    await user.click(screen.getByRole('menuitem', { name: /sign out/i }))
    await waitFor(() => expect(tokenStore.get()).toBeNull())

    expect(client.getQueryData(['me'])).toBeNull()
    expect(client.getQueryData(['dashboard'])).toBeUndefined()
  })

  it('closes the account menu on Escape and returns focus to its button', async () => {
    const user = userEvent.setup()
    setup()

    await screen.findByRole('heading', { name: /upload your cv/i })

    await openAccountMenu(user)
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('menu')).toBeNull()
    expect(screen.getByRole('button', { name: /account menu/i })).toHaveFocus()
  })

  it('blocks an authenticated call at the network boundary when the session is gone', async () => {
    const { dashboard } = await import('../lib/api')
    tokenStore.clear()

    await expect(dashboard.get()).rejects.toMatchObject({ status: 401 })
  })
})
