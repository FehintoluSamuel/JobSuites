import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ErrorSummary, Field, SubmitButton } from '../components/Field'
import { ApiError } from '../lib/api'

export function LoginPage() {
  const { login, isAuthenticated } = useAuth()
  const navigate = useNavigate()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const summaryRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (isAuthenticated) navigate('/app', { replace: true })
  }, [isAuthenticated, navigate])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setPending(true)
    setErrors({})
    setFormError(null)

    try {
      await login({ email, password })
      navigate('/app', { replace: true })
    } catch (err) {
      if (err instanceof ApiError) {
        setErrors(err.fieldErrors)
        setFormError(
          Object.keys(err.fieldErrors).length ? null : err.message,
        )
        // Move focus to the summary so the failure is announced immediately.
        requestAnimationFrame(() => summaryRef.current?.focus())
      } else {
        setFormError('We could not reach the server. Check your connection and try again.')
      }
    } finally {
      setPending(false)
    }
  }

  const hasErrors = Object.keys(errors).length > 0

  return (
    <AuthLayout
      title="Sign in"
      subtitle="Pick up where you left off."
    >
      <form onSubmit={onSubmit} noValidate className="space-y-5">
        {(hasErrors || formError) && (
            <ErrorSummary
              ref={summaryRef}
              title={hasErrors ? 'Check the form' : 'Sign-in failed'}
              errors={
                hasErrors
                  ? errors
                  : { form: [formError ?? 'Unknown error'] }
              }
            />
          )}

        <Field
          label="Email"
          name="email"
          type="email"
          autoComplete="email"
          inputMode="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          errors={errors.email}
          placeholder="you@example.com"
          required
        />

        <Field
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          errors={errors.password}
          required
        />

        <SubmitButton pending={pending}>Sign in</SubmitButton>

        <p className="text-center text-sm text-ink-muted">
          New here?{' '}
          <Link
            to="/signup"
            className="font-semibold text-accent underline underline-offset-2 hover:text-accent-hover"
          >
            Create an account
          </Link>
        </p>
      </form>
    </AuthLayout>
  )
}

export function AuthLayout({
  title,
  subtitle,
  children,
}: {
  title: string
  subtitle: string
  children: React.ReactNode
}) {
  return (
    <div className="flex min-h-dvh flex-col bg-canvas">
      <header className="border-b border-border-subtle bg-surface">
        <div className="mx-auto flex max-w-md items-center justify-between px-6 py-4">
          <span className="text-base font-extrabold tracking-tight text-brand">
            JobSuites
          </span>
        </div>
      </header>

      <main className="flex flex-1 items-start justify-center px-6 py-12 sm:py-20">
        <div className="w-full max-w-md">
          <h1 className="text-3xl font-extrabold tracking-tight text-ink">{title}</h1>
          <p className="mt-2 text-ink-muted">{subtitle}</p>

          <div className="mt-8 rounded-lg border border-border-subtle bg-surface p-6 shadow-sm sm:p-8">
            {children}
          </div>
        </div>
      </main>
    </div>
  )
}
