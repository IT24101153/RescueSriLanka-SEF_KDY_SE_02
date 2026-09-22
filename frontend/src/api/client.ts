import { clearSession, getSession } from '../auth/session'

/** Base URL of the ASP.NET Core API. Override with VITE_API_URL. */
export const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5093'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/**
 * Fetch wrapper that attaches the bearer token and turns a rejected token into
 * a clean signed-out state rather than a wall of failing requests.
 */
export async function apiFetch<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const session = getSession()

  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  if (session) headers.set('Authorization', `Bearer ${session.token}`)

  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { ...init, headers })
  } catch {
    throw new ApiError(0, `Cannot reach the API at ${API_BASE}.`)
  }

  if (response.status === 401) {
    clearSession()
    throw new ApiError(401, 'Your session has expired. Please sign in again.')
  }

  if (!response.ok) {
    let message = `Request failed (HTTP ${response.status}).`
    try {
      const body = await response.json() as { title?: string; detail?: string; errors?: Record<string, string[]>; error?: string }
      message = body.detail ?? body.error ?? body.title ?? (Object.values(body.errors ?? {}).flat().join(' ') || message)
    } catch { /* Non-JSON errors keep the safe status fallback. */ }
    throw new ApiError(response.status, message)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

/** Builds a query string, skipping empty values. */
export function queryString(params: Record<string, string | number | boolean | undefined>) {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') search.set(key, String(value))
  }
  const query = search.toString()
  return query ? `?${query}` : ''
}
