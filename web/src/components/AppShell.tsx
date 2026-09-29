import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import {
  BoardIcon,
  BriefcaseIcon,
  ChatIcon,
  ChevronDownIcon,
  CloseIcon,
  DocumentIcon,
  HomeIcon,
  LifeBuoyIcon,
  MenuIcon,
  TargetIcon,
  UserIcon,
} from './icons'

function initials(name: string | null, fallback = 'You'): string {
  const parts = (name ?? fallback).split(/\s+/).filter(Boolean).slice(0, 2)
  return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || 'JS'
}

/**
 * Every feature the product promises, in the order the suite runs.
 *
 * docs/PRODUCT.md opens with the sequence ingest → match → tailor → prep →
 * track, and the rail follows it. Matching is "My jobs", because that is what
 * the user is looking at, not the mechanism. Support sits last: it is a real
 * destination but not part of the workflow.
 *
 * `hint` is the one-line reason to visit. It is only rendered in the drawer,
 * where there is room, so the desktop rail stays scannable.
 */
const SUITE_NAV = [
  {
    to: '/app',
    label: 'My jobs',
    icon: BriefcaseIcon,
    end: true,
    hint: 'Roles ranked by fit, with the evidence',
  },
  {
    to: '/tailoring',
    label: 'CV tailoring',
    icon: DocumentIcon,
    end: false,
    hint: 'Rewrite your CV for one role',
  },
  {
    to: '/prep',
    label: 'Interview prep',
    icon: ChatIcon,
    end: false,
    hint: 'Questions from this role’s gaps',
  },
  {
    to: '/applications',
    label: 'Applications',
    icon: BoardIcon,
    end: false,
    hint: 'Track what you have applied for',
  },
  {
    to: '/profile',
    label: 'Profile',
    icon: UserIcon,
    end: false,
    hint: 'Your CV and the facts we read from it',
  },
  {
    to: '/support',
    label: 'Help',
    icon: LifeBuoyIcon,
    end: false,
    hint: 'Report a job that is wrong or missing',
  },
] as const

/** Shared link body. The active treatment is a filled rail plus a left bar, so
    position is carried by two signals rather than colour alone. */
function RailLink({
  to,
  label,
  icon: Icon,
  end,
  variant,
  onNavigate,
}: {
  to: string
  label: string
  icon: typeof HomeIcon
  end: boolean
  variant: 'rail' | 'drawer'
  onNavigate?: () => void
}) {
  const base =
    variant === 'rail'
      ? 'flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-semibold transition-colors duration-(--duration-ui) ease-(--ease-ui)'
      : 'flex items-center gap-3 rounded-lg px-3 py-3 text-base font-semibold transition-colors duration-(--duration-ui) ease-(--ease-ui)'

  return (
    <NavLink
      to={to}
      end={end}
      onClick={onNavigate}
      className={({ isActive }) =>
        `${base} ${
          isActive
            ? 'bg-accent-tint text-ink shadow-[inset_3px_0_0_0_var(--color-accent)]'
            : 'text-ink-body hover:bg-sunken'
        }`
      }
    >
      {({ isActive }) => (
        <>
          <Icon className={isActive ? 'h-5 w-5 shrink-0 text-accent' : 'h-5 w-5 shrink-0 text-ink-muted'} />
          <span className="truncate">{label}</span>
        </>
      )}
    </NavLink>
  )
}

/**
 * The mobile drawer.
 *
 * A modal drawer on small screens because the six destinations do not fit in a
 * bar, and because a menu button that silently expands a list in place would
 * push the page content down under the user's finger as they scroll toward it.
 *
 * Focus is trapped while it is open and returned to the button on close, and
 * the page behind it does not scroll — a drawer that lets the page scroll
 * underneath reads as a glitch.
 */
function NavDrawer({
  open,
  onClose,
  buttonRef,
}: {
  open: boolean
  onClose: () => void
  buttonRef: React.RefObject<HTMLButtonElement | null>
}) {
  const panelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return

    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        onClose()
        buttonRef.current?.focus()
        return
      }

      if (e.key !== 'Tab') return

      // Keep Tab inside the panel. Without this, tabbing walks into the page
      // behind the overlay, which is still in the tab order and still visible
      // to a screen reader.
      const focusable = panelRef.current?.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled])',
      )
      if (!focusable || focusable.length === 0) return

      const first = focusable[0]
      const last = focusable[focusable.length - 1]

      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKey)
    // Move focus into the panel so a keyboard user's next Tab is inside it.
    panelRef.current?.querySelector<HTMLElement>('a[href], button')?.focus()

    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = previous
    }
  }, [open, onClose, buttonRef])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-(--z-drawer) lg:hidden">
      <button
        type="button"
        aria-label="Close menu"
        onClick={onClose}
        className="absolute inset-0 h-full w-full cursor-default bg-ink/40"
      />

      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label="Menu"
        className="absolute inset-y-0 left-0 flex w-[min(20rem,85vw)] flex-col bg-surface shadow-[2px_0_8px_rgba(0,0,0,0.16)]"
      >
        <div className="flex h-13 shrink-0 items-center justify-between border-b border-border-subtle px-4">
          <span className="text-lg font-bold tracking-tight text-brand">
            <span className="text-accent">Job</span>Suites
          </span>
          <button
            type="button"
            onClick={onClose}
            className="-mr-1 flex h-11 w-11 items-center justify-center rounded-full text-ink-muted hover:bg-sunken hover:text-ink"
          >
            <CloseIcon className="h-5 w-5" />
            <span className="sr-only">Close menu</span>
          </button>
        </div>

        <nav aria-label="Main" className="flex-1 space-y-1 overflow-y-auto p-3">
          {SUITE_NAV.map(({ to, label, icon, end, hint }) => (
            <div key={label}>
              <RailLink
                to={to}
                label={label}
                icon={icon}
                end={end}
                variant="drawer"
                onNavigate={onClose}
              />
              <p className="px-3 pb-1 pt-0.5 text-xs text-ink-muted">{hint}</p>
            </div>
          ))}
        </nav>
      </div>
    </div>
  )
}

/**
 * Shared page chrome: top bar, left rail on large screens, drawer below `lg`.
 *
 * The top bar keeps only the wordmark, the menu button and the account menu. It
 * used to carry the whole nav, and six destinations do not fit a 52px bar at
 * phone widths without either truncating labels or hiding destinations off the
 * edge — so the destinations moved to the rail, and the bar kept its job.
 */
export function AppShell({ children }: { children: React.ReactNode }) {
  const { user, logout } = useAuth()
  const [menuOpen, setMenuOpen] = useState(false)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const drawerButtonRef = useRef<HTMLButtonElement>(null)
  const location = useLocation()

  // A route change must close the drawer. Navigating from inside it is handled
  // by the link's own onClick, but a back/forward navigation does not go
  // through a click at all, and a drawer left open over a new page is a bug.
  useEffect(() => {
    setDrawerOpen(false)
  }, [location.pathname])

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
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-(--z-skip-link) focus:rounded-full focus:bg-surface focus:px-4 focus:py-2 focus:text-sm focus:font-semibold focus:text-accent"
      >
        Skip to content
      </a>

      <header className="sticky top-0 z-(--z-sticky) bg-surface shadow-[0_0_0_1px_rgba(0,0,0,0.08),0_2px_3px_rgba(0,0,0,0.08)]">
        <div className="mx-auto flex h-13 max-w-[1128px] items-center gap-2 px-4 lg:px-0">
          <Link to="/app" className="shrink-0 text-lg font-bold tracking-tight text-brand">
            <span className="text-accent">Job</span>Suites
          </Link>

          <div className="ml-auto flex items-stretch self-stretch">
            <button
              ref={drawerButtonRef}
              type="button"
              onClick={() => setDrawerOpen(true)}
              aria-expanded={drawerOpen}
              aria-haspopup="dialog"
              className="flex min-h-12 min-w-12 items-center justify-center rounded-lg text-ink-body transition-colors duration-(--duration-ui) ease-(--ease-ui) hover:bg-sunken lg:hidden"
            >
              <MenuIcon className="h-5 w-5" />
              <span className="sr-only">Open menu</span>
            </button>

            <div className="relative flex items-stretch" ref={menuRef}>
              <button
                ref={menuButtonRef}
                type="button"
                onClick={() => setMenuOpen((v) => !v)}
                aria-expanded={menuOpen}
                aria-haspopup="menu"
                className="flex min-h-12 items-center gap-1.5 rounded-lg px-1.5 text-sm font-semibold text-ink-body transition-colors duration-(--duration-ui) ease-(--ease-ui) hover:bg-sunken"
              >
                <span
                  aria-hidden="true"
                  className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-brand text-xs font-semibold text-white"
                >
                  {initials(user?.fullName ?? null)}
                </span>
                <span className="hidden max-w-28 truncate lg:inline">
                  {user?.fullName?.split(' ')[0] ?? 'Me'}
                </span>
                <ChevronDownIcon className="h-4 w-4 shrink-0 text-ink-muted" />
                <span className="sr-only">Account menu</span>
              </button>

              {menuOpen && (
                <div
                  role="menu"
                  className="absolute right-0 top-full z-(--z-dropdown) mt-1 w-64 overflow-hidden rounded-lg border border-border-subtle bg-surface shadow-[0_2px_8px_rgba(0,0,0,0.12)]"
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
                    <UserIcon className="h-4 w-4 text-ink-muted" />
                    View profile
                  </Link>
                  <button
                    type="button"
                    role="menuitem"
                    onClick={logout}
                    className="flex min-h-11 w-full items-center gap-2.5 px-4 text-left text-sm font-semibold text-ink-body hover:bg-sunken"
                  >
                    <TargetIcon className="h-4 w-4 text-ink-muted" />
                    Sign out
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      </header>

      <div className="mx-auto flex max-w-[1128px] gap-6 px-4 py-4 lg:px-0 lg:py-6">
        <nav
          aria-label="Main"
          className="sticky top-19 hidden h-fit w-56 shrink-0 space-y-1 lg:block"
        >
          {SUITE_NAV.map(({ to, label, icon, end }) => (
            <RailLink key={label} to={to} label={label} icon={icon} end={end} variant="rail" />
          ))}
        </nav>

        <main id="main" className="min-w-0 flex-1">
          {children}
        </main>
      </div>

      <NavDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        buttonRef={drawerButtonRef}
      />
    </div>
  )
}

/**
 * The three-column feed used by the dashboard. Column count and the rails that
 * are present have to agree at every breakpoint, or the grid wraps and the third
 * column lands on its own row. Narrow: content only. `md` adds the right rail.
 * `lg` adds the left one — `lg` rather than `md` because `lg` is also where the
 * navigation rail appears, and two rails at once is too narrow to read.
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
    <div
      className={[
        'grid grid-cols-1 items-start gap-4',
        right ? 'md:grid-cols-[minmax(0,1fr)_240px]' : 'md:grid-cols-1',
        left && right ? 'lg:grid-cols-[240px_minmax(0,1fr)_280px]' : '',
        left && !right ? 'lg:grid-cols-[240px_minmax(0,1fr)]' : '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {left && <div className="hidden lg:block">{left}</div>}
      <div className="min-w-0 space-y-4">{center}</div>
      {right && <div className="hidden md:block">{right}</div>}
    </div>
  )
}

/** A card. The hairline border is what separates one from the next in a dense
    feed, so it is a real token rather than an arbitrary grey. */
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
    <section
      className={`overflow-hidden rounded-lg border border-border-subtle bg-surface ${className}`}
    >
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

/** Shared page header for the suite screens: a real `h1` plus a one-line
    statement of what the screen is for. */
export function PageHeader({
  title,
  lede,
  actions,
}: {
  title: string
  lede: string
  actions?: React.ReactNode
}) {
  return (
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-xl font-bold tracking-tight text-ink md:text-2xl">{title}</h1>
        <p className="mt-1 text-sm text-ink-body">{lede}</p>
      </div>
      {actions && <div className="flex shrink-0 flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

/** Primary and secondary buttons. `--color-accent` on white is 4.7:1, which
    passes AA for the label, and white on it is 4.6:1. */
export function Button({
  children,
  onClick,
  variant = 'primary',
  type = 'button',
  disabled,
  className = '',
}: {
  children: React.ReactNode
  onClick?: () => void
  variant?: 'primary' | 'secondary' | 'danger'
  type?: 'button' | 'submit'
  disabled?: boolean
  className?: string
}) {
  const styles = {
    primary: 'bg-accent text-white hover:bg-accent-hover',
    secondary:
      'border border-border-strong bg-surface text-ink-body hover:bg-sunken',
    danger: 'border border-danger/40 bg-surface text-danger hover:bg-danger/5',
  }[variant]

  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-full px-4 text-sm font-semibold transition-colors duration-(--duration-ui) ease-(--ease-ui) disabled:cursor-not-allowed disabled:bg-disabled-bg disabled:text-disabled-text ${styles} ${className}`}
    >
      {children}
    </button>
  )
}

/** Status pill. Tones are named by meaning, not by colour, so a "needs review"
    document and a "checked" one cannot be confused in a greyscale screenshot. */
export function Pill({
  children,
  tone = 'neutral',
}: {
  children: React.ReactNode
  tone?: 'neutral' | 'good' | 'warn' | 'bad' | 'info'
}) {
  const tones = {
    neutral: 'bg-sunken text-ink-body',
    good: 'bg-success-tint text-success',
    warn: 'bg-warn-tint text-warn',
    bad: 'bg-danger-tint text-danger',
    info: 'bg-accent-tint text-accent',
  }[tone]

  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold ${tones}`}
    >
      {children}
    </span>
  )
}

/** A row of count tiles.
 *
 * Every page in the suite opens with one of these. Without it a feature page
 * starts as a bare heading over a single card, which reads as a broken screen
 * until you scroll — the dashboard already proved the pattern, this just stops
 * each page from reinventing the markup. `tone` maps to the text colour only;
 * the tile itself stays neutral so the number, not the tile, carries meaning.
 */
export function StatTiles({
  label,
  tiles,
  footnote,
}: {
  label: string
  tiles: { label: string; value: number | string; tone?: 'good' | 'info' | 'neutral' | 'muted' }[]
  footnote?: React.ReactNode
}) {
  const tones = {
    good: 'text-success',
    info: 'text-accent',
    neutral: 'text-ink-body',
    muted: 'text-ink-muted',
  }

  return (
    <Card bodyClassName="p-4">
      <h2 className="text-base font-semibold text-ink">{label}</h2>
      <dl className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-4" aria-label={label}>
        {tiles.map((t) => (
          <div key={t.label} className="rounded-lg bg-sunken p-3">
            <dt className="text-xs text-ink-muted">{t.label}</dt>
            <dd className={`tabular mt-0.5 text-2xl font-bold ${tones[t.tone ?? 'neutral']}`}>
              {t.value}
            </dd>
          </div>
        ))}
      </dl>
      {footnote && <p className="mt-3 text-sm text-ink-muted">{footnote}</p>}
    </Card>
  )
}

/** A titled note for the right rail of a feature page. */
export function RailNote({
  title,
  icon,
  children,
  tone = 'neutral',
}: {
  title: string
  icon?: React.ReactNode
  children: React.ReactNode
  tone?: 'neutral' | 'good' | 'warn'
}) {
  const tones = {
    neutral: 'border-border-subtle bg-surface',
    good: 'border-success/30 bg-success-tint',
    warn: 'border-warn/40 bg-warn-tint',
  }[tone]

  return (
    <section className={`rounded-lg border p-4 ${tones}`}>
      <h2 className="flex items-center gap-1.5 text-sm font-semibold text-ink">
        {icon}
        {title}
      </h2>
      <div className="mt-1.5 space-y-2 text-sm text-ink-body">{children}</div>
    </section>
  )
}

/** Empty state. A blank page is the most broken-looking thing a product can
    show, so every list states what would fill it and how to get there. */
export function EmptyState({
  icon,
  title,
  children,
  action,
}: {
  icon?: React.ReactNode
  title: string
  children: React.ReactNode
  action?: React.ReactNode
}) {
  return (
    <div className="px-4 py-10 text-center">
      {icon && <div className="flex justify-center text-ink-faint">{icon}</div>}
      <h3 className="mt-3 text-base font-semibold text-ink">{title}</h3>
      <div className="mx-auto mt-1 max-w-md text-sm text-ink-body">{children}</div>
      {action && <div className="mt-4 flex justify-center gap-2">{action}</div>}
    </div>
  )
}
