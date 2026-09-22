import type { ComponentType } from 'react'
import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import logoUrl from '../../assets/logo.jpg'
import { ROLE_LABELS } from '../auth/session'
import type { Role, Session } from '../auth/session'
import DisasterDashboard from '../../components/componentA/DisasterDashboard'
import HelpRequestDashboard from '../../components/componentB/HelpRequestDashboard'
import HelpRequestsReview from '../../components/componentB/HelpRequestsReview'
import './ConsoleShell.css'

type ConsoleShellProps = {
  session: Session
  onSignOut: () => void
}

type Page = {
  path: string
  label: string
  Component: ComponentType
  end?: boolean
}

// Component A — incident & disaster map
const DISASTER_PAGES: Page[] = [
  { path: '/', label: 'Disaster dashboard', Component: DisasterDashboard, end: true },
]

// Component B — help requests
const HELP_REQUEST_PAGES: Page[] = [
  { path: '/dashboard', label: 'Help request dashboard', Component: HelpRequestDashboard, end: true },
  { path: '/dashboard/help-requests', label: 'Request review', Component: HelpRequestsReview },
]

/**
 * Each account sees only its own component's dashboard: Help Request
 * Managers get the help-request pages, everyone else the disaster
 * dashboard. The first page listed is where sign-in lands.
 */
function pagesFor(role: Role): Page[] {
  return role === 'HelpRequestManager' ? HELP_REQUEST_PAGES : DISASTER_PAGES
}

/** Signed-in frame. Component screens render inside <main>. */
export default function ConsoleShell({ session, onSignOut }: ConsoleShellProps) {
  const { user } = session
  const pages = pagesFor(user.role)

  return (
    <div className="shell">
      <header className="topbar">
        <div className="topbar__brand">
          <img src={logoUrl} alt="" className="topbar__logo" />
          <span className="topbar__name">RescueSriLanka</span>
        </div>

        <div className="topbar__user">
          <span className="topbar__who">
            <strong>{user.fullName}</strong>
            <span className="topbar__role">{ROLE_LABELS[user.role]}</span>
          </span>
          <button type="button" className="btn-ghost" onClick={onSignOut}>
            Sign out
          </button>
        </div>
      </header>

      <nav className="shell__nav" aria-label="Console pages">
        {pages.map((page) => (
          <NavLink
            key={page.path}
            to={page.path}
            end={page.end}
            className={({ isActive }) =>
              `shell__nav-link${isActive ? ' is-active' : ''}`
            }
          >
            {page.label}
          </NavLink>
        ))}
      </nav>

      <main className="shell__body">
        <Routes>
          {pages.map(({ path, Component }) => (
            <Route key={path} path={path} element={<Component />} />
          ))}

          {/* Anything else — including the other component's dashboard —
              goes back to this account's own dashboard. */}
          <Route path="*" element={<Navigate to={pages[0].path} replace />} />
        </Routes>
      </main>
    </div>
  )
}
