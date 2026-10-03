import { afterEach, expect, it, vi } from 'vitest'
import { getActiveIncidents, getIncident } from './api'
import { clearSession, storeSession } from '../../shared/auth/session'

afterEach(() => { vi.unstubAllGlobals(); clearSession() })

it('uses the authenticated client for read-only incident list and detail requests', async () => {
  storeSession({ token: 'test-token', expiresAt: '2099-01-01T00:00:00Z', user: { id: 'user', fullName: 'Rescue', email: 'test@example.test', role: 'RescueTeam' } })
  const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify([]), { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)
  await getActiveIncidents()
  await getIncident('existing-id')
  expect(fetchMock.mock.calls.map(([url]) => new URL(url).pathname + new URL(url).search)).toEqual(['/api/incidents?activeOnly=true', '/api/incidents/existing-id'])
  for (const [, init] of fetchMock.mock.calls) {
    expect(init.headers.get('Authorization')).toBe('Bearer test-token')
    expect(init.method ?? 'GET').toBe('GET')
    expect(init.body).toBeUndefined()
  }
})
