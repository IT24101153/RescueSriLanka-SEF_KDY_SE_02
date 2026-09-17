// Central place for API config, so it's a one-line change once the real
// backend URL / auth endpoint shape is confirmed with the team.

export const API_BASE = "http://localhost:5093";

// ASSUMPTION (not yet confirmed with team): login endpoint shape.
// POST /api/auth/login  body: { email, password }
// returns: { token, role, userId, name }
export const AUTH_LOGIN_ENDPOINT = `${API_BASE}/api/auth/login`;

export function getStoredToken(): string | null {
  return localStorage.getItem("authToken");
}

export function getStoredUser(): { role: string; userId: string; name: string } | null {
  const raw = localStorage.getItem("authUser");
  return raw ? JSON.parse(raw) : null;
}

// Wrapper around fetch that attaches the Authorization header automatically.
export async function authFetch(path: string, options: RequestInit = {}): Promise<Response> {
  const token = getStoredToken();
  const headers = new Headers(options.headers);
  headers.set("Content-Type", "application/json");
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  return fetch(`${API_BASE}${path}`, { ...options, headers });
}