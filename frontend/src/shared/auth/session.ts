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

/** Shape of AuthResponse returned by POST /api/auth/login. */
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
  RescueTeam: 'Rescue Team',
  HelpRequestManager: 'Help Request Manager',
}

export function getSession(): Session | null {
  const raw =
    localStorage.getItem(STORAGE_KEY) ?? sessionStorage.getItem(STORAGE_KEY)
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

/** "Keep me signed in" decides whether the session survives a browser restart. */
export function storeSession(session: Session, remember: boolean): void {
  const store = remember ? localStorage : sessionStorage
  store.setItem(STORAGE_KEY, JSON.stringify(session))
}

export function clearSession(): void {
  localStorage.removeItem(STORAGE_KEY)
  sessionStorage.removeItem(STORAGE_KEY)
}
