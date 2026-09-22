import { useState } from 'react'
import { BrowserRouter } from 'react-router-dom'
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

  // The console's pages (incident map, help requests, …) are URL routes.
  return (
    <BrowserRouter>
      <ConsoleShell session={session} onSignOut={handleSignOut} />
    </BrowserRouter>
  )
}

export default App
