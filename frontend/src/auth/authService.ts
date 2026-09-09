import type { Session } from './session'

/** Base URL of the ASP.NET Core API. Override with VITE_API_URL. */
export const API_BASE = import.meta.env.VITE_API_URL ?? 'http://localhost:5093'

export type SignInResult =
  | { ok: true; session: Session }
  | { ok: false; message: string }

export async function signIn(
  email: string,
  password: string,
): Promise<SignInResult> {
  let response: Response

  try {
    response = await fetch(`${API_BASE}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.trim(), password }),
    })
  } catch {
    return {
      ok: false,
      message: `Cannot reach the API at ${API_BASE}. Is the backend running?`,
    }
  }

  if (response.status === 401) {
    return { ok: false, message: 'Invalid email or password.' }
  }

  if (!response.ok) {
    return { ok: false, message: `Sign-in failed (HTTP ${response.status}).` }
  }

  return { ok: true, session: (await response.json()) as Session }
}
