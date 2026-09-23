import { clearSession, getSession } from '../../shared/auth/session'
import RescueCoordinatorDashboard from './RescueCoordinatorDashboard'

/**
 * Adapts Component D's dashboard to the console shell: the shell renders pages
 * without props, while the dashboard takes the signed-in user and a sign-out
 * callback.
 */
export default function RescueDashboardPage() {
  const session = getSession()
  if (!session) return null

  return (
    <RescueCoordinatorDashboard
      user={session.user}
      onLogout={() => {
        clearSession()
        window.location.reload()
      }}
    />
  )
}
