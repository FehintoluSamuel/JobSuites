import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

const navClass = ({ isActive }: { isActive: boolean }) =>
  `inline-flex min-h-11 items-center rounded-md px-3 text-sm font-semibold transition-colors ${
    isActive
      ? 'bg-accent-subtle text-accent'
      : 'text-ink-body hover:bg-canvas hover:text-ink'
  }`

/** Shared page chrome: brand, primary nav, and sign-out. Kept as one component
    so every authenticated screen gets the same rail and the same address bar
    ergonomics. */
export function AppShell({ children }: { children: React.ReactNode }) {
  const { logout } = useAuth()

  return (
    <div className="min-h-dvh bg-canvas">
      <header className="sticky top-0 z-20 border-b border-border-subtle bg-surface">
        <div className="mx-auto flex h-16 max-w-6xl items-center gap-2 px-4 lg:gap-4 lg:px-6">
          <Link to="/app" className="shrink-0 text-base font-extrabold tracking-tight text-brand">
            JobSuites
          </Link>

          <nav aria-label="Main" className="ml-1 flex lg:ml-2">
            <NavLink to="/app" end className={navClass}>
              Dashboard
            </NavLink>
            <NavLink to="/profile" className={navClass}>
              Profile
            </NavLink>
          </nav>

          <div className="ml-auto flex items-center">
            <button
              onClick={logout}
              className="min-h-11 rounded-md border border-border-strong bg-surface px-3 text-sm font-semibold text-ink transition-colors hover:bg-canvas"
            >
              Sign out
            </button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-6 lg:px-6 lg:py-8">{children}</main>
    </div>
  )
}