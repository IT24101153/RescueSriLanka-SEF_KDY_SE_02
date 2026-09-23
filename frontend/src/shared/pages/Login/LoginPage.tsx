import { useState } from 'react'
import type { FormEvent } from 'react'
import logoUrl from '../../../assets/logo.jpg'
import { signIn } from '../../auth/authService'
import { storeSession } from '../../auth/session'
import type { Session } from '../../auth/session'
import './LoginPage.css'

type FieldErrors = {
  email?: string
  password?: string
}

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function CheckIcon() {
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true" className="feature__icon">
      <path
        d="M6.2 11.3 3.5 8.6l1-1 1.7 1.7 5-5 1 1-6 6Z"
        fill="currentColor"
      />
    </svg>
  )
}

type LoginPageProps = {
  onSignedIn: (session: Session) => void
}

export default function LoginPage({ onSignedIn }: LoginPageProps) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(true)
  const [showPassword, setShowPassword] = useState(false)
  const [errors, setErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  function validate(): FieldErrors {
    const next: FieldErrors = {}
    const trimmed = email.trim()

    if (!trimmed) {
      next.email = 'Email is required'
    } else if (!EMAIL_PATTERN.test(trimmed)) {
      next.email = 'Enter a valid email address'
    }

    if (!password) {
      next.password = 'Password is required'
    } else if (password.length < 8) {
      next.password = 'Password must be at least 8 characters'
    }

    return next
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError(null)

    const nextErrors = validate()
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length > 0) return

    setSubmitting(true)
    try {
      const result = await signIn(email, password)
      if (!result.ok) {
        setFormError(result.message)
        return
      }
      storeSession(result.session, remember)
      onSignedIn(result.session)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="login">
      <aside className="login__brand">
        <div className="brand">
          <span className="brand__logo">
            <img src={logoUrl} alt="" className="brand__logo-img" />
          </span>
          <span className="brand__name">RescueSriLanka</span>
        </div>

        <div className="brand__body">
          <h1 className="brand__title">
            Coordinate the response,
            <br />
            from one console.
          </h1>
          <p className="brand__tagline">
            Live incident mapping, safety zones and AI-assisted severity
            analysis for emergency response teams across Sri Lanka.
          </p>

          <ul className="features">
            <li className="feature">
              <CheckIcon />
              Live disaster map with safety zones
            </li>
            <li className="feature">
              <CheckIcon />
              AI severity analysis with human approval
            </li>
            <li className="feature">
              <CheckIcon />
              Teams, shelters and supplies in one place
            </li>
          </ul>
        </div>

        <div className="legend" aria-hidden="true">
          <span className="legend__item">
            <i className="dot dot--safe" />
            Safe
          </span>
          <span className="legend__item">
            <i className="dot dot--caution" />
            Caution
          </span>
          <span className="legend__item">
            <i className="dot dot--danger" />
            Danger
          </span>
        </div>
      </aside>

      <section className="login__panel">
        <div className="card">
          <header className="card__head">
            <h2 className="card__title">Sign in</h2>
            <p className="card__subtitle">
              Emergency coordination console — authorised personnel only.
            </p>
          </header>

          <form className="form" onSubmit={handleSubmit} noValidate>
            {formError && (
              <div className="alert" role="alert">
                {formError}
              </div>
            )}

            <div className="field">
              <label className="field__label" htmlFor="email">
                Email
              </label>
              <input
                id="email"
                className="field__input"
                type="email"
                autoComplete="username"
                placeholder="coordinator@rescue.lk"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                aria-invalid={Boolean(errors.email)}
                aria-describedby={errors.email ? 'email-error' : undefined}
                disabled={submitting}
              />
              {errors.email && (
                <span className="field__error" id="email-error">
                  {errors.email}
                </span>
              )}
            </div>

            <div className="field">
              <label className="field__label" htmlFor="password">
                Password
              </label>
              <div className="field__control">
                <input
                  id="password"
                  className="field__input field__input--padded"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  placeholder="••••••••"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  aria-invalid={Boolean(errors.password)}
                  aria-describedby={
                    errors.password ? 'password-error' : undefined
                  }
                  disabled={submitting}
                />
                <button
                  type="button"
                  className="field__toggle"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                  disabled={submitting}
                >
                  {showPassword ? 'Hide' : 'Show'}
                </button>
              </div>
              {errors.password && (
                <span className="field__error" id="password-error">
                  {errors.password}
                </span>
              )}
            </div>

            <div className="form__row">
              <label className="checkbox">
                <input
                  type="checkbox"
                  checked={remember}
                  onChange={(e) => setRemember(e.target.checked)}
                  disabled={submitting}
                />
                <span>Keep me signed in</span>
              </label>
              <a className="link" href="#forgot">
                Forgot password?
              </a>
            </div>

            <button className="btn" type="submit" disabled={submitting}>
              {submitting ? (
                <>
                  <span className="spinner" aria-hidden="true" />
                  Signing in…
                </>
              ) : (
                'Sign in'
              )}
            </button>
          </form>

          <footer className="card__foot">
            Accounts are provisioned by your administrator.
          </footer>
        </div>
      </section>
    </main>
  )
}
