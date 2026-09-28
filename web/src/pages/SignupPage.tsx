import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ErrorSummary, Field, SubmitButton } from '../components/Field'
import { ApiError } from '../lib/api'
import { AuthLayout } from './LoginPage'

export function SignupPage() {
  const { register, isAuthenticated } = useAuth()
  const navigate = useNavigate()

  const [form, setForm] = useState({ email: '', fullName: '', password: '' })
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const summaryRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (isAuthenticated) navigate('/app', { replace: true })
  }, [isAuthenticated, navigate])

  function set<K extends keyof typeof form>(key: K, value: string) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setPending(true)
    setErrors({})
    setFormError(null)

    try {
      await register(form)
      navigate('/app', { replace: true })
    } catch (err) {
      if (err instanceof ApiError) {
        setErrors(err.fieldErrors)
        setFormError(Object.keys(err.fieldErrors).length ? null : err.message)
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
      title="Create your account"
      subtitle="One profile, then every role we can find for you."
    >
      <form onSubmit={onSubmit} noValidate className="space-y-5">
        {(hasErrors || formError) && (
            <ErrorSummary
              ref={summaryRef}
              title={hasErrors ? 'Check the form' : 'Could not create account'}
              errors={hasErrors ? errors : { form: [formError ?? 'Unknown error'] }}
            />
          )}

        <Field
          label="Full name"
          name="fullName"
          autoComplete="name"
          value={form.fullName}
          onChange={(e) => set('fullName', e.target.value)}
          errors={errors.fullName}
          placeholder="Samuel Fehintolu"
          required
        />

        <Field
          label="Email"
          name="email"
          type="email"
          inputMode="email"
          autoComplete="email"
          value={form.email}
          onChange={(e) => set('email', e.target.value)}
          errors={errors.email}
          placeholder="you@example.com"
          required
        />

        <Field
          label="Password"
          name="password"
          type="password"
          autoComplete="new-password"
          value={form.password}
          onChange={(e) => set('password', e.target.value)}
          errors={errors.password}
          hint="At least 10 characters, with a symbol or a space."
          required
        />

        <SubmitButton pending={pending}>Create account</SubmitButton>

        <p className="text-center text-sm text-ink-muted">
          Already have an account?{' '}
          <Link
            to="/login"
            className="font-semibold text-accent underline underline-offset-2 hover:text-accent-hover"
          >
            Sign in
          </Link>
        </p>
      </form>
    </AuthLayout>
  )
}
