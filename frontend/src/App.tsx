import { useState } from 'react'
import { clearSession, getSession } from './auth/session'
import type { Session } from './auth/session'
import LoginPage from './pages/Login/LoginPage'
import Dashboard from './pages/Dashboard/Dashboard'
import RescueCoordinatorDashboard from './pages/RescueCoordinator/RescueCoordinatorDashboard'

export default function App() {
  const [session, setSession] = useState<Session | null>(() => getSession())
  const signOut = () => {
    clearSession()
    setSession(null)
  }

  if (!session) return <LoginPage onSignedIn={setSession} />

  return session.user.role === 'EmergencyCoordinator'
    ? <RescueCoordinatorDashboard user={session.user} onLogout={signOut} />
    : <Dashboard role={session.user.role} />
}
