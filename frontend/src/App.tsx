import { useState } from 'react'
import LoginPage from './pages/Login/LoginPage'
import ConsoleShell from './pages/Console/ConsoleShell'
import { clearSession, getSession } from './auth/session'
import type { Session } from './auth/session'

function App() {
  const [session, setSession] = useState<Session | null>(() => getSession())

  function handleSignOut() {
    clearSession()
    setSession(null)
  }

  if (!session) {
    return <LoginPage onSignedIn={setSession} />
  }

  return <ConsoleShell session={session} onSignOut={handleSignOut} />
}

export default App
