import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import ResourceDashboard from './ResourceDashboard'

// The dashboard calls the API with bare fetch, so the stub answers by path.
type ResourceState = {
  helpRequests: Record<string, unknown>[]
  managedSupplies: Record<string, unknown>[]
}

let state: ResourceState
let posted: { url: string; body: unknown }[]

/** What the allocation agent answers for the one request in the fixture. */
let advice: Record<string, unknown>

function recommended(quantity: number) {
  return {
    decision: 'Recommend',
    resourceId: 'supply-1',
    resourceType: 'ManagedSupply',
    resourceName: 'Rice',
    unit: 'kg',
    quantity,
    availableQuantity: 50,
    confidence: 0.85,
    reason: '"Rice" matches the request for 20 kg.',
    warnings: [],
    requiresApproval: true,
  }
}

function managedSupply(quantityOnHand: number) {
  return {
    id: 'supply-1',
    category: 'Food',
    name: 'Rice',
    unit: 'kg',
    quantityOnHand,
    lowStockThreshold: 10,
    isActive: true,
    updatedAtUtc: '2026-10-01T00:00:00Z',
  }
}

function helpRequest(status: string) {
  return {
    id: 'request-1',
    requesterName: 'Nimal Perera',
    contactNumber: '0712345678',
    needType: 'Food',
    description: 'Rice - 20 kg',
    status,
    createdAtUtc: '2026-10-01T00:00:00Z',
    district: 'Colombo',
  }
}

function json(body: unknown) {
  return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response)
}

beforeEach(() => {
  sessionStorage.clear()
  localStorage.clear()
  posted = []
  state = { helpRequests: [helpRequest('Pending')], managedSupplies: [managedSupply(50)] }
  advice = recommended(20)

  vi.stubGlobal('fetch', vi.fn((input: string, init?: RequestInit) => {
    const url = String(input)
    const method = init?.method ?? 'GET'

    if (method !== 'GET') {
      posted.push({ url, body: init?.body ? JSON.parse(String(init.body)) : null })

      if (url.endsWith('/allocation-recommendation')) return json(advice)

      // Mirror what the API does, so the reload after each action sees it.
      if (url.includes('/help-requests/') && url.endsWith('/status')) {
        const status = (JSON.parse(String(init?.body)) as { status: string }).status
        state.helpRequests = [helpRequest(status)]
      }
      if (url.endsWith('/allocations')) {
        const sent = JSON.parse(String(init?.body)) as { quantity: number }
        state.helpRequests = [helpRequest('Fulfilled')]
        state.managedSupplies = [managedSupply(50 - sent.quantity)]
      }
      return json({})
    }

    if (url.includes('help-requests')) return json(state.helpRequests)
    if (url.includes('managed-supplies')) return json(state.managedSupplies)
    return json([])
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

/** Opens the Requests tab, where a request is decided and resourced. */
async function openRequests() {
  render(<ResourceDashboard />)
  // The tab carries a count badge, so its accessible name is not just "Requests".
  await waitFor(() => expect(screen.getByRole('tab', { name: /^Requests/ })).toBeTruthy())
  fireEvent.click(screen.getByRole('tab', { name: /^Requests/ }))
  await waitFor(() => expect(screen.getByRole('button', { name: 'Accept' })).toBeTruthy())
}

/** The select or input inside the form field with this label. */
function field(label: string) {
  const element = screen.getByText(label).closest('label')!
  return element.querySelector('select') ?? element.querySelector('input')!
}

describe('Component C resource requests', () => {
  it('accepting sends the asked-for resources and deducts them, in one click', async () => {
    await openRequests()

    // Accepting *is* sending. Nothing else is asked of the manager.
    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))

    await waitFor(() => expect(posted.some((call) => call.url.endsWith('/allocations'))).toBe(true))

    // The acceptance is recorded, so the citizen hears about it...
    expect(posted.find((call) => call.url.endsWith('/status'))!.body).toMatchObject({ status: 'Accepted' })
    // ...and the allocation names the supply row and the amount, which is what
    // the API subtracts.
    expect(posted.find((call) => call.url.endsWith('/allocations'))!.body).toMatchObject({
      resourceType: 'ManagedSupply',
      resourceId: 'supply-1',
      quantity: 20,
      helpRequestId: 'request-1',
    })
    expect(state.managedSupplies[0].quantityOnHand).toBe(30)

    // Done in one step, so it is finished and off the working list.
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull())
    expect(screen.queryByRole('button', { name: 'Send resources' })).toBeNull()
  })

  it('offers the AI recommendation before deciding', async () => {
    await openRequests()
    expect(screen.getByRole('button', { name: 'AI recommendation' })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'AI recommendation' }))

    await waitFor(() => expect(screen.getByText(/Rice/)).toBeTruthy())
    expect(posted.some((call) => call.url.endsWith('/allocations'))).toBe(false)
  })

  it('reuses a recommendation already on screen rather than asking twice', async () => {
    await openRequests()
    fireEvent.click(screen.getByRole('button', { name: 'AI recommendation' }))
    await waitFor(() => expect(screen.getByText(/Rice/)).toBeTruthy())

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
    await waitFor(() => expect(posted.some((call) => call.url.endsWith('/allocations'))).toBe(true))

    // One model call for the button, none for the send that followed.
    expect(posted.filter((call) => call.url.endsWith('/allocation-recommendation'))).toHaveLength(1)
  })

  it('leaves an unsendable request accepted and retryable, with the reason', async () => {
    advice = {
      ...recommended(0),
      decision: 'NoMatch',
      resourceId: null,
      resourceType: null,
      reason: 'The request does not state a quantity, so no amount can be suggested.',
    }
    await openRequests()

    fireEvent.click(screen.getByRole('button', { name: 'Accept' }))

    // Better to say why than to invent a quantity and move real stock.
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('does not state a quantity'))
    expect(posted.some((call) => call.url.endsWith('/allocations'))).toBe(false)
    expect(state.managedSupplies[0].quantityOnHand).toBe(50)

    // And it must not vanish half-done: the acceptance stands, and the send can
    // be retried once the cause is dealt with.
    expect(screen.getByText('Accepted')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Send resources' })).toBeTruthy()
  })

  it('can finish a request left accepted by an earlier failed send', async () => {
    state.helpRequests = [helpRequest('Accepted')]
    render(<ResourceDashboard />)
    await waitFor(() => expect(screen.getByRole('tab', { name: /^Requests/ })).toBeTruthy())
    fireEvent.click(screen.getByRole('tab', { name: /^Requests/ }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Send resources' })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: 'Send resources' }))

    await waitFor(() => expect(posted.some((call) => call.url.endsWith('/allocations'))).toBe(true))
    expect(state.managedSupplies[0].quantityOnHand).toBe(30)
  })

  it('moves a rejected request straight to history', async () => {
    await openRequests()

    fireEvent.click(screen.getByRole('button', { name: 'Reject' }))

    // Rejecting ends the request, so it leaves the working list entirely.
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull())
    expect(screen.queryByRole('button', { name: 'Send resources' })).toBeNull()
  })
})

describe('Component C inventory units', () => {
  it('offers units for the chosen category instead of a free-text box', async () => {
    render(<ResourceDashboard />)
    await waitFor(() => expect(screen.getByRole('tab', { name: 'Supplies' })).toBeTruthy())
    fireEvent.click(screen.getByRole('tab', { name: 'Supplies' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add supply' }))

    // Typed units fragment the stock tables, because stock is consolidated by
    // item *and* unit — "kg" and "kgs" would never add up.
    const unit = field('Unit') as HTMLSelectElement
    expect(unit.tagName).toBe('SELECT')

    fireEvent.change(field('Category'), { target: { value: 'Food' } })
    const food = [...unit.options].map((option) => option.value).filter(Boolean)
    expect(food).toContain('kg')
    expect(food).not.toContain('litres')

    // And the list follows the category.
    fireEvent.change(field('Category'), { target: { value: 'Water' } })
    const water = [...(field('Unit') as HTMLSelectElement).options].map((option) => option.value).filter(Boolean)
    expect(water).toContain('litres')
    expect(water).not.toContain('kg')
  })
})
