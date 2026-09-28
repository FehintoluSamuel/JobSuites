import { Link, Navigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export function DashboardPage() {
  const { user, isLoading, logout } = useAuth()

  if (isLoading) {
    return (
      <Shell>
        <p className="text-ink-muted" role="status">
          Loading your workspace…
        </p>
      </Shell>
    )
  }

  if (!user) return <Navigate to="/login" replace />

  return (
    <Shell>
      <div className="space-y-2">
        <h1 className="text-2xl font-extrabold tracking-tight text-ink">
          Welcome, {user.fullName.split(' ')[0]}
        </h1>
        <p className="text-ink-muted">
          Signed in as <span className="font-medium text-ink-body">{user.email}</span>
        </p>
      </div>

      <section
        aria-labelledby="next-sprint"
        className="rounded-lg border border-dashed border-border-strong bg-surface p-6"
      >
        <h2 id="next-sprint" className="text-sm font-bold uppercase tracking-wide text-ink-muted">
          Sprint 2
        </h2>
        <p className="mt-2 text-ink-body">
          The evidence base and live MyJobMag role feed land here. This panel is
          intentionally empty rather than filled with mock data — every number on
          it will be real.
        </p>
      </section>

      <div>
        <button
          onClick={logout}
          className="min-h-11 rounded-md border border-border-strong bg-surface px-4 py-2 text-sm font-semibold text-ink transition-colors hover:bg-canvas"
        >
          Sign out
        </button>
      </div>
    </Shell>
  )
}

function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-dvh bg-canvas">
      <header className="border-b border-border-subtle bg-surface">
        <div className="mx-auto flex max-w-3xl items-center justify-between px-6 py-4">
          <Link to="/app" className="text-base font-extrabold tracking-tight text-brand">
            JobSuites
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-3xl space-y-8 px-6 py-10">{children}</main>
    </div>
  )
}
