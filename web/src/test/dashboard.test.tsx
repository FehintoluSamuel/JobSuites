/**
 * Sprint 2 acceptance tests — the dashboard vertical slice.
 *
 * The dashboard is where a user spends real time, so these assert the states
 * that usually come out half-finished: the empty state before a CV exists, the
 * explained-match state once it does, and an accessible failure when a CV
 * cannot be read.
 */
import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../auth/AuthContext'
import { DashboardPage } from '../pages/DashboardPage'
import { tokenStore } from '../lib/api'
import type { DashboardResponse } from '../lib/api'

const me = { id: '1', email: 'ada@example.com', name: 'Ada Obi' }

function emptyDashboard(): DashboardResponse {
  return {
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
    ingest: null,
  }
}

function matchedDashboard(): DashboardResponse {
  return {
    profile: {
      fileName: 'ada-obi.pdf',
      fullName: 'Ada Obi',
      email: 'ada@example.com',
      phone: null,
      location: 'Lagos',
      headline: 'Senior Data Engineer',
      yearsExperience: 6,
      summary: null,
      profilePicture: null,
      bannerPicture: null,
      photoConsentGiven: false,
      desiredSalary: null,
      availability: null,
      targetRoles: ['Data Engineer'],
      preferredStates: ['Lagos'],
      desiredJobTypes: ['full-time'],
      skills: [
        { name: 'Python', normalized: 'python', years: 6, isCore: true },
        { name: 'SQL', normalized: 'sql', years: 6, isCore: true },
      ],
      experiences: [],
      education: [],
      certifications: [],
      languages: [],
      links: [],
      completeness: {
        percent: 78,
        requiredMet: 8,
        requiredTotal: 9,
        optionalMet: 2,
        optionalTotal: 10,
        missing: ['Contact email'],
      },
      updatedAt: '2026-09-28T10:00:00Z',
    },
    summary: {
      rolesIngested: 23,
      rolesConsidered: 23,
      strongMatches: 1,
      goodMatches: 2,
      possibleMatches: 0,
      stretchMatches: 0,
      rolesUpdatedAt: '2026-09-28T10:00:00Z',
    },
    matches: [
      {
        roleId: 'r1',
        title: 'Senior Data Engineer',
        company: 'Kuda Bank',
        tier: 'Strong',
        score: 88,
        states: ['Lagos'],
        roleSkills: ['Python', 'SQL', 'Airflow'],
        salaryEstimate: '₦15m',
        minYears: 4,
        maxYears: 8,
        postingCount: 2,
        description: null,
        evidence: [
          {
            kind: 'Skill',
            label: 'Python',
            detail: 'you have 6y, role asks for it',
            sourceUrl: 'https://www.myjobmag.com/jobs/1',
          },
          {
            kind: 'Experience',
            label: 'Data Engineering',
            detail: '4 of your 6 years are in this area',
            sourceUrl: null,
          },
        ],
        gaps: ['You have no cloud certification'],
        postings: [
          {
            id: 'p1',
            url: 'https://www.myjobmag.com/jobs/1',
            location: 'Lagos',
            state: 'Lagos',
            source: 'MyJobMag',
          },
          {
            id: 'p2',
            url: 'https://www.myjobmag.com/jobs/2',
            location: null,
            state: null,
            source: 'MyJobMag',
          },
        ],
        postedAt: '2026-09-20T00:00:00Z',
        deadlineAt: '2026-10-10T00:00:00Z',
      },
    ],
    hasIngestedRoles: true,
    ingest: null,
  }
}

function renderDashboard(dashboardResponse: DashboardResponse | (() => DashboardResponse)) {
  tokenStore.set('test-token')
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })

  let dashboardCalls = 0
  const respond: () => DashboardResponse =
    typeof dashboardResponse === 'function' ? dashboardResponse : () => dashboardResponse

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.includes('/api/auth/me')) {
        return { ok: true, status: 200, json: async () => me }
      }
      if (url.includes('/api/dashboard')) {
        dashboardCalls += 1
        return { ok: true, status: 200, json: async () => respond() }
      }
      if (url.includes('/api/profile/cv')) {
        if (init?.method === 'DELETE') {
          return { ok: true, status: 204, json: async () => null }
        }
        return { ok: true, status: 200, json: async () => null }
      }
      return { ok: false, status: 404, json: async () => ({ title: 'Not found' }) }
    }),
  )
  void dashboardCalls

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <AuthProvider>
          <DashboardPage />
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

describe('DashboardPage', () => {
  it('invites a CV upload when the profile is empty, with a labelled file input', async () => {
    renderDashboard(emptyDashboard)
    expect(await screen.findByRole('heading', { name: /upload your cv/i })).toBeInTheDocument()
    expect(screen.getByLabelText(/choose your cv file/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /what happens next/i })).toBeInTheDocument()
  })

  it('tells the user the feed is still filling when no roles are ingested yet', async () => {
    renderDashboard(() => ({ ...emptyDashboard(), hasIngestedRoles: false }))
    expect(
      await screen.findByText(/we are still filling the role feed/i),
    ).toBeInTheDocument()
  })

  it('shows the profile, tier counts, and ranked matches with their evidence', async () => {
    renderDashboard(matchedDashboard)
    expect(await screen.findByRole('heading', { name: /ada obi/i })).toBeInTheDocument()
    expect(screen.getAllByText(/python/i).length).toBeGreaterThan(0)
    expect(screen.getByLabelText(/match summary by tier/i)).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()

    expect(screen.getByRole('heading', { name: /senior data engineer/i })).toBeInTheDocument()
    expect(screen.getByText('88')).toBeInTheDocument()
    expect(screen.getByText(/why it matches/i)).toBeInTheDocument()
    expect(screen.getByText(/4 of your 6 years are in this area/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /before you apply/i })).toBeInTheDocument()
    const apply = screen.getByRole('link', { name: /apply on myjobmag/i })
    expect(apply).toHaveAttribute('href', 'https://www.myjobmag.com/jobs/1')
    expect(apply).toHaveAttribute('target', '_blank')
  })

  it('surfaces an accessible error when the CV cannot be read', async () => {
    const user = userEvent.setup()
    renderDashboard(emptyDashboard)
    await screen.findByRole('heading', { name: /upload your cv/i })

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.includes('/api/auth/me')) return { ok: true, status: 200, json: async () => me }
        if (url.includes('/api/dashboard')) {
          return { ok: true, status: 200, json: async () => emptyDashboard() }
        }
        if (url.includes('/api/profile/cv')) {
          return {
            ok: false,
            status: 400,
            json: async () => ({
              title: 'CV rejected',
              detail: 'We could not read skills or experience from this file.',
              errors: { file: ['No skills or experience detected.'] },
            }),
          }
        }
        return { ok: false, status: 404, json: async () => null }
      }),
    )

    const file = new File(['hello'], 'resume.txt', { type: 'text/plain' })
    await user.upload(screen.getByLabelText(/choose your cv file/i), file)
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/could not read skills or experience/i)
  })

  it('refetches matches after a successful upload replaces the profile', async () => {
    const user = userEvent.setup()
    let empty = true
    renderDashboard(() => (empty ? emptyDashboard() : matchedDashboard()))

    await screen.findByRole('heading', { name: /upload your cv/i })

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
        const url = String(input)
        if (url.includes('/api/auth/me')) return { ok: true, status: 200, json: async () => me }
        if (url.includes('/api/dashboard')) {
          return { ok: true, status: 200, json: async () => (empty ? emptyDashboard() : matchedDashboard()) }
        }
        if (url.includes('/api/profile/cv')) {
          empty = false
          return { ok: true, status: 200, json: async () => ({ profile: {}, matchesComputed: 23 }) }
        }
        return { ok: false, status: 404, json: async () => null }
      }),
    )

    const file = new File(['cv text'], 'resume.txt', { type: 'text/plain' })
    await user.upload(screen.getByLabelText(/choose your cv file/i), file)

    expect(await screen.findByRole('heading', { name: /ada obi/i })).toBeInTheDocument()
    await waitFor(() => {
      expect(screen.getByRole('heading', { name: /senior data engineer/i })).toBeInTheDocument()
    })
  })
})