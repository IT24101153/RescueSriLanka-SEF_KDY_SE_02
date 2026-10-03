/** Roles issued by the API. Mirrors UserRole in the backend. */
export type Role =
  | 'Citizen'
  | 'EmergencyCoordinator'
  | 'ResourceManager'
  | 'RescueTeam'
  | 'HelpRequestManager'

/** Shape of UserDto returned by the API. */
export type User = {
  id: string
  fullName: string
  email: string
  role: Role
  phoneNumber?: string | null
}

/** Shape of AuthResponse returned by POST /api/auth/portal/login. */
export type Session = {
  token: string
  expiresAt: string
  user: User
}

const STORAGE_KEY = 'rsl.session'

export const ROLE_LABELS: Record<Role, string> = {
  Citizen: 'Citizen',
  EmergencyCoordinator: 'Emergency Coordinator',
  ResourceManager: 'Resource Manager',
  RescueTeam: 'Rescue Coordinator',
  HelpRequestManager: 'Help Request Manager',
}

export function getSession(): Session | null {
  const raw = sessionStorage.getItem(STORAGE_KEY)
  if (!raw) return null

  try {
    const session = JSON.parse(raw) as Session
    // Drop an expired token rather than letting the API reject every call.
    if (new Date(session.expiresAt).getTime() <= Date.now()) {
      clearSession()
      return null
    }
    return session
  } catch {
    clearSession()
    return null
  }
}

/**
 * Kept in sessionStorage only: it ends with the tab, and never sits in localStorage where it would outlive
 * the visit. Browser storage is still readable by script, so this narrows exposure rather than removing it.
 */
export function storeSession(session: Session): void {
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session))
}

export function clearSession(): void {
  // An older build kept a copy in localStorage; remove it so it cannot resurface.
  localStorage.removeItem(STORAGE_KEY)
  sessionStorage.removeItem(STORAGE_KEY)
}
