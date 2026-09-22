import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import logoUrl from '../../assets/logo.jpg'
import { ROLE_LABELS } from '../auth/session'
import type { Session } from '../auth/session'
import Dashboard from '../pages/Dashboard'
import HelpRequestsReview from '../../components/componentB/HelpRequestsReview'
import UserManagement from '../pages/UserManagement'
import Reports from '../pages/Reports'
import './ConsoleShell.css'

type ConsoleShellProps = {
  session: Session
  onSignOut: () => void
}

const NAV_ITEMS = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/dashboard/help-requests', label: 'Request review', end: false },
  { to: '/dashboard/users', label: 'Users', end: false },
  { to: '/dashboard/reports', label: 'Reports', end: false },
]

/** Signed-in frame. Component screens render inside <main>. */
export default function ConsoleShell({ session, onSignOut }: ConsoleShellProps) {
  const { user } = session

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
        {NAV_ITEMS.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              `shell__nav-link${isActive ? ' is-active' : ''}`
            }
          >
            {item.label}
          </NavLink>
        ))}
      </nav>

      <main className="shell__body">
        <Routes>
          {/* Shared home: Component A and Component B dashboards */}
          <Route path="/" element={<Dashboard />} />

          {/* Component B — help requests */}
          <Route path="/dashboard/help-requests" element={<HelpRequestsReview />} />

          {/* Shared placeholders — not built yet */}
          <Route path="/dashboard/users" element={<UserManagement />} />
          <Route path="/dashboard/reports" element={<Reports />} />

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  )
}
