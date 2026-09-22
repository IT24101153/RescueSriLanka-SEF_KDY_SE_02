import logoUrl from '../../assets/logo.jpg'
import { ROLE_LABELS } from '../../auth/session'
import type { Session } from '../../auth/session'
import Dashboard from '../Dashboard/Dashboard'
import './ConsoleShell.css'

type ConsoleShellProps = {
  session: Session
  onSignOut: () => void
}

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

      <main className="shell__body">
        <Dashboard />
      </main>
    </div>
  )
}
