import { useEffect, useRef, useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import {
  BellIcon,
  BriefcaseIcon,
  ChevronDownIcon,
  DocumentIcon,
  HomeIcon,
  SearchIcon,
  UserIcon,
} from './icons'

function initials(name: string | null, fallback = 'You'): string {
  const parts = (name ?? fallback).split(/\s+/).filter(Boolean).slice(0, 2)
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || 'JS'
}

/**
 * The top navigation. Icon-over-label, with the active item carrying the
 * crimson underline — one active state, one colour, so position is legible
 * without relying on the label alone.
 */
function TopNavItem({
  to,
  label,
  icon,
  end,
}: {
  to: string
  label: string
  icon: React.ReactNode
  end?: boolean
}) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `flex min-h-12 min-w-[3.5rem] flex-col items-center justify-center gap-0.5 border-b-2 px-2 pt-1 text-xs font-semibold transition-colors ${
          isActive
            ? 'border-accent text-ink'
            : 'border-transparent text-ink-muted hover:bg-sunken hover:text-ink-body'
        }`
      }
    >
      {icon}
      <span>{label}</span>
    </NavLink>
  )
}

/**
 * Shared page chrome: the top nav, and the three-column body every
 * authenticated screen sits in. Kept as one component so every screen gets the
 * same rail and the same address bar ergonomics.
 */
export function AppShell({ children }: { children: React.ReactNode }) {
  const { user, logout } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)

  // The account menu is a disclosure, so it closes on Escape and on a click
  // outside — and returns focus to the button that opened it.
  useEffect(() => {
    if (!menuOpen) return

    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setMenuOpen(false)
        menuButtonRef.current?.focus()
      }
    }
    function onPointerDown(e: MouseEvent) {
      if (!menuRef.current?.contains(e.target as Node)) setMenuOpen(false)
    }

    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', onPointerDown)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', onPointerDown)
    }
  }, [menuOpen])

  return (
    <div className="min-h-dvh bg-canvas">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-surface focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-accent"
      >
        Skip to content
      </a>

      <header className="sticky top-0 z-30 bg-surface shadow-[0_0_0_1px_rgba(0,0,0,0.08),0_2px_3px_rgba(0,0,0,0.08)]">
        <div className="mx-auto flex h-13 max-w-[1128px] items-center gap-2 px-4 lg:gap-4 lg:px-0">
          <Link
            to="/app"
            className="flex shrink-0 items-center gap-1.5 text-lg font-extrabold tracking-tight text-brand"
          >
            <span aria-hidden="true" className="text-accent">
              Job
            </span>
            Suites
          </Link>

          <form role="search" className="ml-1 hidden min-w-0 flex-1 sm:block lg:ml-4">
            <label htmlFor="site-search" className="sr-only">
              Search roles
            </label>
            <div className="relative">
              <SearchIcon className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-faint" />
              <input
                id="site-search"
                type="search"
                placeholder="Search roles, skills, companies"
                className="h-9 w-full rounded-full border border-border-strong bg-sunken pl-8 pr-3 text-sm text-ink placeholder:text-ink-faint focus:border-accent focus:bg-surface focus-visible:outline-accent"
              />
            </div>
          </form>

          <nav aria-label="Main" className="ml-auto flex items-stretch self-stretch">
            <TopNavItem to="/app" end label="Home" icon={<HomeIcon />} />
            <TopNavItem to="/app" label="My jobs" icon={<BriefcaseIcon />} />
            <TopNavItem to="/profile" label="Profile" icon={<UserIcon />} />

            <span className="mx-1 my-3 w-px bg-border-subtle" aria-hidden="true" />

            <button
              type="button"
              className="hidden min-h-12 min-w-[3.5rem] flex-col items-center justify-center gap-0.5 border-b-2 border-transparent px-2 pt-1 text-xs font-semibold text-ink-muted transition-colors hover:bg-sunken hover:text-ink-body sm:flex"
            >
              <BellIcon />
              <span>Alerts</span>
            </button>

            <div className="relative flex" ref={menuRef}>
              <button
                ref={menuButtonRef}
                type="button"
                onClick={() => setMenuOpen((v) => !v)}
                aria-expanded={menuOpen}
                aria-haspopup="menu"
                className="flex min-h-12 items-center gap-1.5 border-b-2 border-transparent px-2 pt-1 text-xs font-semibold text-ink-body transition-colors hover:bg-sunken"
              >
                <span
                  aria-hidden="true"
                  className="flex h-6 w-6 items-center justify-center rounded-full bg-brand text-[0.625rem] font-bold text-white"
                >
                  {initials(user?.fullName ?? null)}
                </span>
                <span className="hidden max-w-[6rem] truncate lg:inline">
                  {user?.fullName?.split(' ')[0] ?? 'Me'}
                </span>
                <ChevronDownIcon className="h-4 w-4 text-ink-muted" />
                <span className="sr-only">Account menu</span>
              </button>

              {menuOpen && (
                <div
                  role="menu"
                  className="absolute right-0 top-full z-40 mt-1 w-64 overflow-hidden rounded-lg border border-border-subtle bg-surface shadow-[0_2px_8px_rgba(0,0,0,0.12)]"
                >
                  <div className="border-b border-border-subtle px-4 py-3">
                    <p className="truncate text-sm font-semibold text-ink">
                      {user?.fullName ?? 'Signed in'}
                    </p>
                    <p className="truncate text-sm text-ink-muted">{user?.email}</p>
                  </div>
                  <Link
                    to="/profile"
                    role="menuitem"
                    onClick={() => setMenuOpen(false)}
                    className="flex min-h-11 items-center gap-2.5 px-4 text-sm font-semibold text-ink-body hover:bg-sunken"
                  >
                    <DocumentIcon className="h-4 w-4 text-ink-muted" />
                    View profile
                  </Link>
                  <button
                    type="button"
                    role="menuitem"
                    onClick={logout}
                    className="flex min-h-11 w-full items-center gap-2.5 px-4 text-left text-sm font-semibold text-ink-body hover:bg-sunken"
                  >
                    <UserIcon className="h-4 w-4 text-ink-muted" />
                    Sign out
                  </button>
                </div>
              )}
            </div>
          </nav>
        </div>
      </header>

      <main id="main" className="mx-auto max-w-[1128px] px-4 py-4 lg:px-0 lg:py-6">
        {children}
      </main>
    </div>
  )
}

/**
 * The three-column body: a narrow identity rail, a content feed, and a right
 * rail. Columns collapse to one on narrow screens, and the left rail drops
 * first, because the feed is the reason the user is here.
 */
export function AppColumns({
  left,
  center,
  right,
}: {
  left?: React.ReactNode
  center: React.ReactNode
  right?: React.ReactNode
}) {
  return (
    <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-[minmax(0,1fr)_225px] lg:grid-cols-[225px_minmax(0,1fr)_300px]">
      {left && <div className="hidden md:block">{left}</div>}
      <div className="min-w-0 space-y-4">{center}</div>
      {right && <div className="hidden md:block">{right}</div>}
    </div>
  )
}

/** A card. Border, not shadow — the hairline is what separates one from the
    next in a dense feed. */
export function Card({
  title,
  actions,
  children,
  className = '',
  bodyClassName = 'p-4',
}: {
  title?: React.ReactNode
  actions?: React.ReactNode
  children: React.ReactNode
  className?: string
  bodyClassName?: string
}) {
  return (
    <section className={`overflow-hidden rounded-lg border border-border-subtle bg-surface ${className}`}>
      {title && (
        <header className="flex flex-wrap items-center justify-between gap-2 border-b border-border-subtle px-4 py-3">
          <h2 className="text-base font-semibold text-ink">{title}</h2>
          {actions}
        </header>
      )}
      <div className={bodyClassName}>{children}</div>
    </section>
  )
}
