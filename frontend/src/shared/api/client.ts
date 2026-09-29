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
 * Attaches the bearer token, turns a rejected token into a clean signed-out
 * state rather than a wall of failing requests, and surfaces the API's error
 * body on failure. Shared by apiFetch and apiFetchPage below.
 */
async function apiFetchRaw(path: string, init: RequestInit = {}): Promise<Response> {
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
    // Surface what the API said — ProblemDetails, a plain { error }, or
    // validation errors — and fall back to the status when the body is not JSON.
    let message = `Request failed (HTTP ${response.status}).`
    try {
      const body = await response.json() as { title?: string; detail?: string; errors?: Record<string, string[]>; error?: string }
      message = body.detail ?? body.error ?? body.title ?? (Object.values(body.errors ?? {}).flat().join(' ') || message)
    } catch { /* Non-JSON errors keep the safe status fallback. */ }
    throw new ApiError(response.status, message)
  }

  return response
}

/** Fetch wrapper for the common case: attach the token, return the parsed body. */
export async function apiFetch<T>(
  path: string,
  init: RequestInit = {},
): Promise<T> {
  const response = await apiFetchRaw(path, init)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

/**
 * Like apiFetch, but also reads the X-Total-Count header a paginated endpoint
 * returns alongside its (still plain-array) body — for rendering "page N of M"
 * without changing the response shape every other caller of that endpoint
 * already depends on.
 */
export async function apiFetchPage<T>(
  path: string,
  init: RequestInit = {},
): Promise<{ data: T; totalCount: number | null }> {
  const response = await apiFetchRaw(path, init)
  const data = response.status === 204 ? (undefined as T) : ((await response.json()) as T)
  const header = response.headers.get('X-Total-Count')
  return { data, totalCount: header === null ? null : Number(header) }
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
