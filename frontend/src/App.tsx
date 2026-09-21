import { useState } from 'react'
import { getSession } from './auth/session'
import type { Session } from './auth/session'
import LoginPage from './pages/Login/LoginPage'
import Dashboard from './pages/Dashboard/Dashboard'

export default function App() {
  const [session, setSession] = useState<Session | null>(() => getSession())
  return session ? <Dashboard /> : <LoginPage onSignedIn={setSession} />
}
