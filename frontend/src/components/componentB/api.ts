// Help-request pages call the API through authFetch(). It uses the console's
// shared session and API base URL (auth/session.ts, api/client.ts), but returns
// the raw Response so each page can decide how to handle errors.
import { API_BASE } from "../../shared/api/client";
import { getSession } from "../../shared/auth/session";

// Wrapper around fetch that attaches the Authorization header automatically.
export async function authFetch(path: string, options: RequestInit = {}): Promise<Response> {
  const token = getSession()?.token;
  const headers = new Headers(options.headers);
  headers.set("Content-Type", "application/json");
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  return fetch(`${API_BASE}${path}`, { ...options, headers });
}
