import { afterEach, expect, it, vi } from 'vitest'
import { getRescueHelpRequests, recommendRescueTeam } from './api'
import { clearSession, storeSession } from '../../shared/auth/session'

afterEach(() => { vi.unstubAllGlobals(); clearSession() })

it('uses authenticated coordination endpoints and sends only explicit recommendation inputs', async () => {
  storeSession({ token: 'test-token', expiresAt: '2099-01-01T00:00:00Z', user: { id: 'user', fullName: 'Rescue', email: 'test@example.test', role: 'RescueTeam' } }, false)
  const fetch = vi.fn().mockImplementation(async () => new Response(JSON.stringify([]), { status: 200 }))
  vi.stubGlobal('fetch', fetch)
  const controller = new AbortController()
  await getRescueHelpRequests(controller.signal)
  await recommendRescueTeam('help-id', 'WaterRescue', 3, controller.signal)
  expect(fetch.mock.calls.map(([url]) => new URL(url).pathname)).toEqual(['/api/rescue/help-requests', '/api/rescue/help-requests/help-id/recommend-team'])
  for (const [, init] of fetch.mock.calls) {
    expect(init.headers.get('Authorization')).toBe('Bearer test-token')
    expect(init.signal).toBe(controller.signal)
  }
  expect(fetch.mock.calls[0][1].body).toBeUndefined()
  expect(fetch.mock.calls[1][1].method).toBe('POST')
  expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ requiredSkill: 'WaterRescue', requiredCapacity: 3 })
})
