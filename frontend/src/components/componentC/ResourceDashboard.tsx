import { useEffect, useState } from 'react'
import { getSession } from '../../shared/auth/session'
import { API_BASE } from '../../shared/api/client'
import './ResourceDashboard.css'

/**
 * Creating, changing and deleting resources is staff work, so those calls
 * carry the signed-in Resource Manager's token. Reads stay anonymous.
 */
function authHeaders(base?: Record<string, string>): Record<string, string> {
  const token = getSession()?.token
  return { ...(base ?? {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) }
}

type Supply = { id: string; name: string; unit: string; quantityOnHand: number; lowStockThreshold: number }
type FoodWaterStock = { id: string; itemName: string; unit: string; quantityOnHand: number; lowStockThreshold: number }
type ManagedSupply = { id: string; category: string; name: string; unit: string; quantityOnHand: number; lowStockThreshold: number; isActive: boolean; updatedAtUtc: string }
type ResourceAlert = { resourceType: string; resourceId: string; name: string; quantityOnHand: number; lowStockThreshold: number; unit: string }
type ResourceHelpRequest = { id: string; requesterName: string; contactNumber: string; needType: string; description: string; status: string; createdAtUtc: string; district?: string | null }
type Donation = { id: string; userId?: string | null; submissionId?: string | null; donorName: string; contactNumber: string; donationType: string; quantity: number; unit: string; notes?: string | null; status: string; createdAtUtc: string; district?: string | null }
type SupplyForm = { name: string; unit: string; quantityOnHand: string; lowStockThreshold: string }
type StockForm = { itemName: string; unit: string; quantityOnHand: string; lowStockThreshold: string }
type ResourceType = 'medical' | 'food' | 'managed'
type Page = 'overview' | 'supplies' | 'allocations' | 'donate' | 'history'
type SupplySource = { id: string; type: ResourceType }
type DeleteRequest = { type: ResourceType; id: string; label: string; duplicates: SupplySource[] }
type FulfillmentRequest = { id: string; supplyKey: string; quantity: string }
type AllocationRecommendation = { decision: string; resourceId: string | null; resourceType: string | null; quantity: number; confidence: number; reason: string; warnings: string[]; requiresApproval: boolean; resourceName: string | null; unit: string | null; availableQuantity: number | null }
type StockForecastItem = { resourceType: string; resourceId: string; name: string; unit: string; quantityOnHand: number; lowStockThreshold: number | null; usedInLast30Days: number; averageDailyUse: number; estimatedDaysRemaining: number | null; riskLevel: 'Critical' | 'Watch' | 'Stable'; suggestedAction: string }
type StockForecast = { windowDays: number; generatedAtUtc: string; summary: string; items: StockForecastItem[] }
type AllocationPlanItem = { helpRequestId: string; needType: string; requesterName: string; priority: number; decision: string; resourceId: string | null; resourceType: string | null; resourceName: string | null; unit: string | null; quantity: number; availableQuantity: number | null; confidence: number; reason: string; warnings: string[]; requiresApproval: boolean }
type AllocationPlan = { generatedAtUtc: string; summary: string; items: AllocationPlanItem[] }

const MANAGED_SUPPLY_ITEMS: Record<string, string[]> = {
  Food: ['Dry foods', 'Rice', 'Other'],
  Water: ['Bottled water', 'Drinking water', 'Water containers', 'Other'],
  Medical: ['Bandages', 'Plasters', 'Surgical spirits', 'Saline', 'Gauze / cotton packets', 'Other'],
  'Sanitary products': ['Napkins', 'Other'],
  'Hygiene items': ['Soap', 'Toothpaste', 'Toothbrushes', 'Other'],
  Other: ['Other'],
}
const SUPPLY_CATEGORIES = ['Food', 'Water', 'Medical', 'Sanitary products', 'Hygiene items', 'Other'] as const
const HISTORY_RETENTION_DAYS = 7
const HISTORY_RETENTION_MS = HISTORY_RETENTION_DAYS * 24 * 60 * 60 * 1000

async function getResources<T>(path: string): Promise<T> {
  let lastError: unknown
  for (let attempt = 0; attempt < 8; attempt += 1) {
    try {
      const response = await fetch(`${API_BASE}/api/resources/${path}`, { headers: authHeaders() })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(result?.error ?? `Unable to load ${path}.`)
      }
      return response.json() as Promise<T>
    } catch (resourceError) {
      lastError = resourceError
      if (attempt < 7 && resourceError instanceof TypeError) {
        await new Promise((resolve) => window.setTimeout(resolve, 500 * (attempt + 1)))
        continue
      }
      throw resourceError
    }
  }
  throw lastError instanceof Error ? lastError : new Error(`Unable to load ${path}.`)
}

function clearedHistoryStorageKey(): string {
  return `rsl.resources.clearedHistory.${getSession()?.user.id ?? 'anonymous'}`
}

function readHistoryDates(): Record<string, string> {
  try {
    const stored = localStorage.getItem(clearedHistoryStorageKey())
    if (!stored) return {}
    const parsed = JSON.parse(stored) as unknown
    // Older builds kept only the hidden item IDs. Give those entries a fresh
    // retention window when upgrading to the countdown history view.
    if (Array.isArray(parsed)) {
      const upgraded: Record<string, string> = {}
      for (const id of parsed) {
        if (typeof id === 'string') upgraded[id] = new Date().toISOString()
      }
      return upgraded
    }
    return parsed && typeof parsed === 'object' ? parsed as Record<string, string> : {}
  } catch {
    return {}
  }
}

function rememberHistoryDates(entries: Record<string, string>): void {
  try {
    localStorage.setItem(clearedHistoryStorageKey(), JSON.stringify({ ...readHistoryDates(), ...entries }))
  } catch {
    // The active screen still clears the records if browser storage is unavailable.
  }
}

function daysUntilHistoryDeletion(processedAt: string | undefined, now = Date.now()): number {
  if (!processedAt) return 0
  const remaining = new Date(processedAt).getTime() + HISTORY_RETENTION_MS - now
  return Math.max(0, Math.ceil(remaining / (24 * 60 * 60 * 1000)))
}

function ResourceDashboard() {
  const [medicalSupplies, setMedicalSupplies] = useState<Supply[]>([])
  const [foodWaterStock, setFoodWaterStock] = useState<FoodWaterStock[]>([])
  const [managedSupplies, setManagedSupplies] = useState<ManagedSupply[]>([])
  const [alerts, setAlerts] = useState<ResourceAlert[]>([])
  const [helpRequests, setHelpRequests] = useState<ResourceHelpRequest[]>([])
  const [donations, setDonations] = useState<Donation[]>([])
  const [requestHistory, setRequestHistory] = useState<ResourceHelpRequest[]>([])
  const [donationHistory, setDonationHistory] = useState<Donation[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [formError, setFormError] = useState('')
  const [resourceType, setResourceType] = useState<ResourceType>('medical')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [duplicateSupplies, setDuplicateSupplies] = useState<SupplySource[]>([])
  const [supplyForm, setSupplyForm] = useState<SupplyForm>({ name: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
  const [stockForm, setStockForm] = useState<StockForm>({ itemName: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
  const [managedCategory, setManagedCategory] = useState('Medical')
  const [managedItem, setManagedItem] = useState('Bandages')
  const [managedCustomName, setManagedCustomName] = useState('')
  const [page, setPage] = useState<Page>('overview')
  const [historyClock, setHistoryClock] = useState(Date.now())
  const [deleteRequest, setDeleteRequest] = useState<DeleteRequest | null>(null)
  const [fulfillmentRequest, setFulfillmentRequest] = useState<FulfillmentRequest | null>(null)
  const [updatingRequestId, setUpdatingRequestId] = useState<string | null>(null)
  const [sendingRequestId, setSendingRequestId] = useState<string | null>(null)
  const [requestActionError, setRequestActionError] = useState<{ id: string; message: string } | null>(null)
  const [updatingDonationKey, setUpdatingDonationKey] = useState<string | null>(null)
  const [recommendation, setRecommendation] = useState<{ id: string; result: AllocationRecommendation } | null>(null)
  const [loadingRecommendationId, setLoadingRecommendationId] = useState<string | null>(null)
  const [stockForecast, setStockForecast] = useState<StockForecast | null>(null)
  const [loadingStockForecast, setLoadingStockForecast] = useState(false)
  const [stockForecastError, setStockForecastError] = useState('')
  const [allocationPlan, setAllocationPlan] = useState<AllocationPlan | null>(null)
  const [loadingAllocationPlan, setLoadingAllocationPlan] = useState(false)
  const [allocationPlanError, setAllocationPlanError] = useState('')

  const loadResources = async () => {
    setIsLoading(true)
    setError('')
    try {
      const [medicalData, foodData, managedData, alertData, helpRequestData, donationData] = await Promise.all([
        getResources<Supply[]>('medical-supplies'),
        getResources<FoodWaterStock[]>('food-water-stock'),
        getResources<ManagedSupply[]>('managed-supplies'),
        getResources<ResourceAlert[]>('alerts/low-stock'),
        getResources<ResourceHelpRequest[]>('help-requests'),
        getResources<Donation[]>('donations'),
      ])
      setMedicalSupplies(medicalData)
      setFoodWaterStock(foodData)
      setManagedSupplies(managedData)
      setAlerts(alertData)
      const historyDates = readHistoryDates()
      const now = Date.now()
      const requestKeys = new Set<string>()
      const donationKeys = new Set<string>()
      const processedRequests = helpRequestData.filter((request) => request.status !== 'Pending')
      const processedDonations = donationData.filter((donation) => donation.status !== 'PendingReview')
      for (const request of processedRequests) {
        const key = `request:${request.id}`
        requestKeys.add(key)
        historyDates[key] ??= new Date(now).toISOString()
      }
      for (const donation of processedDonations) {
        const key = `donation:${donation.id}`
        donationKeys.add(key)
        historyDates[key] ??= new Date(now).toISOString()
      }
      const retainedRequests = processedRequests.filter((request) => {
        const processedAt = historyDates[`request:${request.id}`]
        return processedAt && daysUntilHistoryDeletion(processedAt) > 0
      })
      const retainedDonations = processedDonations.filter((donation) => {
        const processedAt = historyDates[`donation:${donation.id}`]
        return processedAt && daysUntilHistoryDeletion(processedAt) > 0
      })
      for (const key of Object.keys(historyDates)) {
        if (key.startsWith('request:') && !requestKeys.has(key)) delete historyDates[key]
        if (key.startsWith('donation:') && !donationKeys.has(key)) delete historyDates[key]
      }
      for (const item of processedRequests) {
        if (daysUntilHistoryDeletion(historyDates[`request:${item.id}`]) <= 0) delete historyDates[`request:${item.id}`]
      }
      for (const item of processedDonations) {
        if (daysUntilHistoryDeletion(historyDates[`donation:${item.id}`]) <= 0) delete historyDates[`donation:${item.id}`]
      }
      for (const [key, processedAt] of Object.entries(historyDates)) {
        if (new Date(processedAt).getTime() > now) historyDates[key] = new Date(now).toISOString()
      }
      rememberHistoryDates(historyDates)
      setHelpRequests(helpRequestData.filter((request) => request.status === 'Pending'))
      setDonations(donationData.filter((donation) => donation.status === 'PendingReview'))
      setRequestHistory(retainedRequests)
      setDonationHistory(retainedDonations)
    } catch (resourceError) {
      setError(resourceError instanceof Error ? resourceError.message : 'Unable to load resources.')
    } finally {
      setIsLoading(false)
    }
  }

  const submitInventory = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setFormError('')
    const isMedical = resourceType === 'medical'
    const isManaged = resourceType === 'managed'
    const path = isMedical ? 'medical-supplies' : 'food-water-stock'
    const form = isMedical ? supplyForm : stockForm
    const itemName = isManaged
      ? managedItem === 'Other' || managedCategory === 'Other' ? managedCustomName : managedItem
      : isMedical ? supplyForm.name : stockForm.itemName
    const unit = isMedical ? supplyForm.unit : stockForm.unit
    const quantityText = isMedical ? supplyForm.quantityOnHand : stockForm.quantityOnHand
    const quantity = Number(quantityText)
    if (!itemName.trim()) {
      setFormError('Enter an item name.')
      return
    }
    if (!unit.trim()) {
      setFormError('Enter a unit.')
      return
    }
    if (!quantityText.trim() || !Number.isFinite(quantity) || quantity <= 0) {
      setFormError('Enter a quantity greater than zero.')
      return
    }
    if (isMedical && !Number.isInteger(quantity)) {
      setFormError('Medical supply quantity must be a whole number.')
      return
    }
    setIsSubmitting(true)
    const body = isManaged
      ? {
          category: managedCategory,
          name: managedItem === 'Other' || managedCategory === 'Other'
              ? managedCustomName.trim()
              : managedItem,
          unit: stockForm.unit,
          quantityOnHand: Number(stockForm.quantityOnHand),
          lowStockThreshold: 0,
        }
      : isMedical
      ? { name: supplyForm.name, unit: supplyForm.unit, quantityOnHand: Number(supplyForm.quantityOnHand), lowStockThreshold: 0 }
      : { itemName: stockForm.itemName, unit: stockForm.unit, quantityOnHand: Number(stockForm.quantityOnHand), lowStockThreshold: 0 }
    try {
      const endpoint = isManaged ? `${API_BASE}/api/resources/managed-supplies` : `${API_BASE}/api/resources/${path}`
      const response = await fetch(editingId ? `${endpoint}/${editingId}` : endpoint, { method: editingId ? 'PUT' : 'POST', headers: authHeaders({ 'Content-Type': 'application/json' }), body: JSON.stringify(body) })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string; title?: string } | null
        throw new Error(result?.error ?? result?.title ?? `Unable to create ${isMedical ? 'the medical supply' : 'the food or water stock'}.`)
      }
      for (const duplicate of duplicateSupplies) {
        const duplicatePath = duplicate.type === 'managed'
          ? 'managed-supplies'
          : duplicate.type === 'medical' ? 'medical-supplies' : 'food-water-stock'
        const duplicateResponse = await fetch(`${API_BASE}/api/resources/${duplicatePath}/${duplicate.id}`, {
          method: 'DELETE',
          headers: authHeaders(),
        })
        if (!duplicateResponse.ok) {
          throw new Error('The supply was updated, but duplicate inventory rows could not be merged. Refresh and try again.')
        }
      }
      if (isMedical) setSupplyForm({ name: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
      else if (isManaged) {
        setStockForm({ itemName: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
        setManagedCustomName('')
      } else setStockForm({ itemName: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
      setEditingId(null)
      setDuplicateSupplies([])
      setIsFormOpen(false)
      await loadResources()
    } catch (submitError) {
      setFormError(submitError instanceof Error ? submitError.message : `Unable to create ${form.unit}.`)
    } finally {
      setIsSubmitting(false)
    }
  }

  useEffect(() => { void loadResources() }, [])

  useEffect(() => {
    const timer = window.setInterval(() => {
      const now = Date.now()
      setHistoryClock(now)
      const dates = readHistoryDates()
      setRequestHistory((current) => current.filter((request) => daysUntilHistoryDeletion(dates[`request:${request.id}`], now) > 0))
      setDonationHistory((current) => current.filter((donation) => daysUntilHistoryDeletion(dates[`donation:${donation.id}`], now) > 0))
    }, 60_000)
    return () => window.clearInterval(timer)
  }, [])

  const totalSupplyValue = [...medicalSupplies, ...foodWaterStock, ...managedSupplies]
  const totalAvailable = totalSupplyValue.reduce((sum, item) => sum + Number(item.quantityOnHand), 0)
  const totalThreshold = totalSupplyValue.reduce((sum, item) => sum + Number(item.lowStockThreshold), 0)
  const stockPercentage = totalAvailable + totalThreshold === 0
    ? 0
    : Math.round((totalAvailable / (totalAvailable + totalThreshold)) * 100)
  const supplyLevels = totalSupplyValue.slice(0, 5).map((item) => ({
    name: 'name' in item ? item.name : item.itemName,
    percentage: Math.min(100, Math.round((Number(item.quantityOnHand) / Math.max(1, Number(item.quantityOnHand) + Number(item.lowStockThreshold))) * 100)),
  }))
  const availableSupplies = [
    ...medicalSupplies
      .filter((supply) => supply.quantityOnHand > 0)
      .map((supply) => ({
        key: `MedicalSupply:${supply.id}`,
        resourceType: 'MedicalSupply',
        name: supply.name,
        quantityOnHand: supply.quantityOnHand,
        unit: supply.unit,
        detail: 'Medical',
      })),
    ...foodWaterStock
      .filter((stock) => stock.quantityOnHand > 0)
      .map((stock) => ({
        key: `FoodWaterStock:${stock.id}`,
        resourceType: 'FoodWaterStock',
        name: stock.itemName,
        quantityOnHand: stock.quantityOnHand,
        unit: stock.unit,
        detail: 'Food / water',
      })),
    ...managedSupplies
      .filter((supply) => supply.quantityOnHand > 0)
      .map((supply) => ({
        key: `ManagedSupply:${supply.id}`,
        resourceType: 'ManagedSupply',
        name: supply.name,
        quantityOnHand: supply.quantityOnHand,
        unit: supply.unit,
        detail: supply.category,
      })),
  ]
  type InventoryTableRow = { id: string; name: string; unit: string; quantity: number; type: ResourceType; sources: SupplySource[] }
  const categoryInventory: Record<string, InventoryTableRow[]> = Object.fromEntries(
    SUPPLY_CATEGORIES.map((category) => [category, []]),
  )
  const addInventoryRow = (category: string, source: SupplySource, name: string, unit: string, quantity: number) => {
    const existing = categoryInventory[category].find((row) => row.name.trim().toLowerCase() === name.trim().toLowerCase() && row.unit.trim().toLowerCase() === unit.trim().toLowerCase())
    if (existing) {
      existing.quantity += quantity
      existing.sources.push(source)
    } else {
      categoryInventory[category].push({ id: source.id, name, unit, quantity, type: source.type, sources: [source] })
    }
  }
  for (const supply of medicalSupplies) {
    addInventoryRow('Medical', { id: supply.id, type: 'medical' }, supply.name, supply.unit, supply.quantityOnHand)
  }
  for (const stock of foodWaterStock) {
    const category = /water/i.test(stock.itemName) ? 'Water' : 'Food'
    addInventoryRow(category, { id: stock.id, type: 'food' }, stock.itemName, stock.unit, stock.quantityOnHand)
  }
  for (const supply of managedSupplies) {
    const category = SUPPLY_CATEGORIES.find((value) => value.toLowerCase() === supply.category.toLowerCase()) ?? 'Other'
    addInventoryRow(category, { id: supply.id, type: 'managed' }, supply.name, supply.unit, supply.quantityOnHand)
  }
  const preferredSupplyKey = (needType: string) => {
    const need = needType.toLowerCase()
    const preferred = need.includes('medical')
      ? availableSupplies.find((supply) => supply.resourceType === 'MedicalSupply')
      : need.includes('food') || need.includes('water')
        ? availableSupplies.find((supply) => supply.resourceType === 'FoodWaterStock')
        : undefined
    return (preferred ?? availableSupplies[0])?.key ?? ''
  }
  const donationGroups = Object.entries(donations.reduce<Record<string, Donation[]>>((groups, donation) => {
    const key = donation.submissionId ?? `single:${donation.id}`
    if (!groups[key]) groups[key] = []
    groups[key].push(donation)
    return groups
  }, {})).map(([key, items]) => ({ key, items }))
  const historyDates = readHistoryDates()

  const openResourceForm = (type: ResourceType) => {
    setFormError('')
    setDuplicateSupplies([])
    setResourceType('managed')
    const category = type === 'food' ? 'Food' : 'Medical'
    setManagedCategory(category)
    setManagedItem(MANAGED_SUPPLY_ITEMS[category][0])
    setManagedCustomName('')
    setEditingId(null)
    setIsFormOpen(true)
  }

  const editSupply = (supply: Supply, quantity = supply.quantityOnHand, duplicates: SupplySource[] = []) => {
    setResourceType('medical')
    setEditingId(supply.id)
    setDuplicateSupplies(duplicates)
    setSupplyForm({ name: supply.name, unit: supply.unit, quantityOnHand: String(quantity), lowStockThreshold: String(supply.lowStockThreshold) })
    setFormError('')
    setIsFormOpen(true)
  }

  const editStock = (stock: FoodWaterStock, quantity = stock.quantityOnHand, duplicates: SupplySource[] = []) => {
    setResourceType('food')
    setEditingId(stock.id)
    setDuplicateSupplies(duplicates)
    setStockForm({ itemName: stock.itemName, unit: stock.unit, quantityOnHand: String(quantity), lowStockThreshold: String(stock.lowStockThreshold) })
    setFormError('')
    setIsFormOpen(true)
  }

  const editManagedSupply = (supply: ManagedSupply, quantity: number, duplicates: SupplySource[]) => {
    const options = MANAGED_SUPPLY_ITEMS[supply.category] ?? ['Other']
    const isListedItem = options.includes(supply.name)
    setResourceType('managed')
    setEditingId(supply.id)
    setDuplicateSupplies(duplicates)
    setManagedCategory(supply.category)
    setManagedItem(isListedItem ? supply.name : 'Other')
    setManagedCustomName(isListedItem ? '' : supply.name)
    setStockForm({ itemName: supply.name, unit: supply.unit, quantityOnHand: String(quantity), lowStockThreshold: String(supply.lowStockThreshold) })
    setFormError('')
    setIsFormOpen(true)
  }

  const removeResource = async (type: ResourceType, id: string, label: string) => {
    setError('')
    try {
      const path = type === 'medical' ? 'medical-supplies' : type === 'managed' ? 'managed-supplies' : 'food-water-stock'
      const response = await fetch(`${API_BASE}/api/resources/${path}/${id}`, { method: 'DELETE', headers: authHeaders() })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(result?.error ?? `Unable to remove ${label}.`)
      }
      await loadResources()
    } catch (removeError) {
      setError(removeError instanceof Error ? removeError.message : `Unable to remove ${label}.`)
    }
  }

  const confirmRemove = async () => {
    if (!deleteRequest) return
    const request = deleteRequest
    setDeleteRequest(null)
    await removeResource(request.type, request.id, request.label)
    for (const duplicate of request.duplicates) {
      await removeResource(duplicate.type, duplicate.id, request.label)
    }
  }

  const updateRequestStatus = async (id: string, status: 'Accepted' | 'Rejected') => {
    setError('')
    setRequestActionError(null)
    setUpdatingRequestId(id)
    try {
      const response = await fetch(`${API_BASE}/api/resources/help-requests/${id}/status`, {
        method: 'PATCH',
        headers: authHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ status }),
      })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(result?.error ?? `Unable to update the request (HTTP ${response.status}).`)
      }
      rememberHistoryDates({ [`request:${id}`]: new Date().toISOString() })
      setHelpRequests((current) => current.filter((request) => request.id !== id))
      await loadResources()
    } catch (statusError) {
      setRequestActionError({
        id,
        message: statusError instanceof Error ? statusError.message : 'Unable to update the request.',
      })
    } finally {
      setUpdatingRequestId(null)
    }
  }

  const getAllocationRecommendation = async (id: string) => {
    setRequestActionError(null)
    setLoadingRecommendationId(id)
    try {
      const response = await fetch(`${API_BASE}/api/resources/help-requests/${id}/allocation-recommendation`, { method: 'POST', headers: authHeaders() })
      const result = await response.json().catch(() => null) as AllocationRecommendation | { error?: string } | null
      if (!response.ok) throw new Error((result && 'error' in result ? result.error : undefined) ?? `Unable to get an allocation recommendation (HTTP ${response.status}).`)
      setRecommendation({ id, result: result as AllocationRecommendation })
    } catch (recommendationError) {
      setRequestActionError({ id, message: recommendationError instanceof Error ? recommendationError.message : 'Unable to get an allocation recommendation.' })
    } finally {
      setLoadingRecommendationId(null)
    }
  }

  const getStockForecast = async () => {
    setLoadingStockForecast(true)
    setStockForecastError('')
    try {
      const result = await getResources<StockForecast>('stock-forecast')
      setStockForecast(result)
    } catch (forecastError) {
      setStockForecastError(forecastError instanceof Error ? forecastError.message : 'Unable to load the stock forecast.')
    } finally {
      setLoadingStockForecast(false)
    }
  }

  const getAllocationPlan = async () => {
    setLoadingAllocationPlan(true)
    setAllocationPlanError('')
    try {
      const response = await fetch(`${API_BASE}/api/resources/help-requests/allocation-plan`, { method: 'POST', headers: authHeaders() })
      const result = await response.json().catch(() => null) as AllocationPlan | { error?: string } | null
      if (!response.ok) throw new Error((result && 'error' in result ? result.error : undefined) ?? `Unable to plan allocations (HTTP ${response.status}).`)
      setAllocationPlan(result as AllocationPlan)
    } catch (planError) {
      setAllocationPlanError(planError instanceof Error ? planError.message : 'Unable to plan allocations.')
    } finally {
      setLoadingAllocationPlan(false)
    }
  }

  const updateDonationStatus = async (submission: Donation[], status: 'Accepted' | 'Rejected') => {
    setError('')
    const first = submission[0]
    const key = first.submissionId ?? first.id
    setUpdatingDonationKey(key)
    try {
      const endpoint = first.submissionId
        ? `${API_BASE}/api/resources/donations/batch/${first.submissionId}/status`
        : `${API_BASE}/api/resources/donations/${first.id}/status`
      const response = await fetch(endpoint, {
        method: 'PATCH',
        headers: authHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({ status }),
      })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(result?.error ?? 'Unable to update the donation.')
      }
      const processedAt = new Date().toISOString()
      rememberHistoryDates(Object.fromEntries(
        submission.map((donation) => [`donation:${donation.id}`, processedAt]),
      ))
      setDonations((current) => current.filter((donation) =>
        first.submissionId
          ? donation.submissionId !== first.submissionId
          : donation.id !== first.id,
      ))
      await loadResources()
    } catch (statusError) {
      setError(statusError instanceof Error ? statusError.message : 'Unable to update the donation.')
    } finally {
      setUpdatingDonationKey(null)
    }
  }

  const fulfillRequest = async () => {
    if (!fulfillmentRequest) return
    const quantityText = fulfillmentRequest.quantity.trim()
    const quantity = Number(quantityText)
    if (!fulfillmentRequest.supplyKey) {
      setRequestActionError({ id: fulfillmentRequest.id, message: 'Select an available supply.' })
      return
    }
    if (!quantityText || !Number.isFinite(quantity) || quantity <= 0) {
      setRequestActionError({ id: fulfillmentRequest.id, message: 'Enter a quantity greater than zero.' })
      return
    }
    const [resourceType, resourceId] = fulfillmentRequest.supplyKey.split(':')
    if (!resourceType || !resourceId) {
      setRequestActionError({ id: fulfillmentRequest.id, message: 'Select an available supply first.' })
      return
    }
    const selectedSupply = availableSupplies.find((supply) => supply.key === fulfillmentRequest.supplyKey)
    if (!selectedSupply || quantity > selectedSupply.quantityOnHand) {
      setRequestActionError({ id: fulfillmentRequest.id, message: 'Quantity exceeds the selected supply available.' })
      return
    }
    if (resourceType === 'MedicalSupply' && !Number.isInteger(quantity)) {
      setRequestActionError({ id: fulfillmentRequest.id, message: 'Medical supply quantity must be a whole number.' })
      return
    }
    setError('')
    setRequestActionError(null)
    setSendingRequestId(fulfillmentRequest.id)
    try {
      const response = await fetch(`${API_BASE}/api/resources/allocations`, {
        method: 'POST',
        headers: authHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({
          resourceType,
          resourceId,
          quantity,
          helpRequestId: fulfillmentRequest.id,
          incidentId: null,
        }),
      })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(result?.error ?? `Unable to send resources (HTTP ${response.status}).`)
      }
      setFulfillmentRequest(null)
      await loadResources()
    } catch (fulfillmentError) {
      setRequestActionError({
        id: fulfillmentRequest.id,
        message: fulfillmentError instanceof Error ? fulfillmentError.message : 'Unable to send resources.',
      })
    } finally {
      setSendingRequestId(null)
    }
  }

  const navigate = (nextPage: Page) => setPage(nextPage)
  const pageTitles: Record<Page, string> = {
    overview: 'Resource overview',
    supplies: 'Supplies and stock',
    allocations: 'Resource requests',
    donate: 'Donation offers',
    history: 'Resource history',
  }

  return (
    <div className="app-shell">
      <div className="main-content" id="overview">
        <nav className="resource-page-tabs" aria-label="Resource manager sections" role="tablist">
          {([
            ['overview', 'Overview'],
            ['supplies', 'Supplies'],
            ['allocations', 'Requests'],
            ['donate', 'Donations'],
            ['history', 'History'],
          ] as const).map(([tab, label]) => (
            <button
              key={tab}
              className={`resource-page-tab ${page === tab ? 'is-active' : ''}`}
              type="button"
              role="tab"
              aria-selected={page === tab}
              onClick={() => navigate(tab)}
            >
              {label}
              {tab === 'allocations' && helpRequests.length > 0 && <span>{helpRequests.length}</span>}
              {tab === 'donate' && donationGroups.length > 0 && <span>{donationGroups.length}</span>}
            </button>
          ))}
        </nav>

        {page === 'overview' && (
          <header className="dashboard-heading">
            <div>
              <p className="eyebrow">Operations overview</p>
              <h1>{pageTitles[page]}</h1>
              <p className="resource-muted">Monitor available resources and coordinate support requests.</p>
            </div>
            <div className="dashboard-actions">
              <button className="dashboard-action" type="button" onClick={() => navigate('supplies')}>Manage inventory</button>
              <button className="dashboard-action" type="button" disabled={isLoading} onClick={() => void loadResources()}>{isLoading ? 'Refreshing...' : 'Refresh data'}</button>
              <button className="dashboard-action is-primary" type="button" disabled={loadingStockForecast} onClick={() => void getStockForecast()}>
                {loadingStockForecast ? 'Forecasting...' : 'AI stock forecast'}
              </button>
            </div>
          </header>
        )}

        {error && (
          <div className="api-error" role="alert">
            <span>{error}</span>
            <button type="button" onClick={() => void loadResources()}>Try again</button>
          </div>
        )}

        {deleteRequest && (
          <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.currentTarget === event.target) setDeleteRequest(null) }}>
            <section className="confirm-modal" role="alertdialog" aria-modal="true" aria-labelledby="remove-title">
              <span className="confirm-icon" aria-hidden="true">!</span>
              <p className="eyebrow">Remove resource</p>
              <h2 id="remove-title">Remove {deleteRequest.label}?</h2>
              <p className="resource-muted">This resource will no longer appear in your active inventory. Existing allocation history will remain safe.</p>
              <div className="form-actions">
                <button className="secondary-button large-action" type="button" onClick={() => setDeleteRequest(null)}>Keep resource</button>
                <button className="danger-button large-action" type="button" onClick={() => void confirmRemove()}>Remove resource</button>
              </div>
            </section>
          </div>
        )}

        {isFormOpen && (
          <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.currentTarget === event.target) setIsFormOpen(false) }}>
            <section className="modal" role="dialog" aria-modal="true" aria-labelledby="resource-form-title">
              <div className="modal-heading">
                <div>
                  <p className="eyebrow">{editingId ? 'Update resource' : 'New resource'}</p>
              <h2 id="resource-form-title">{editingId ? 'Edit' : 'Add'} {resourceType === 'medical' ? 'medical supplies' : resourceType === 'food' ? 'food or water stock' : 'supplies'}</h2>
                </div>
                <button className="close-button" type="button" aria-label="Close form" onClick={() => { setEditingId(null); setIsFormOpen(false) }}>x</button>
              </div>
              <form noValidate onSubmit={submitInventory}>
                {resourceType === 'managed' ? (
                  <>
                    <label>
                      Category
                      <select value={managedCategory} onChange={(event) => {
                        const category = event.target.value
                        setManagedCategory(category)
                        setManagedItem(MANAGED_SUPPLY_ITEMS[category][0] ?? '')
                        setManagedCustomName('')
                      }}>
                        {Object.keys(MANAGED_SUPPLY_ITEMS).map((category) => <option key={category} value={category}>{category}</option>)}
                      </select>
                    </label>
                    <label>
                      Item
                      <select value={managedItem} onChange={(event) => {
                        setManagedItem(event.target.value)
                        setManagedCustomName('')
                      }}>
                        {MANAGED_SUPPLY_ITEMS[managedCategory].map((item) => <option key={item} value={item}>{item}</option>)}
                      </select>
                    </label>
                    {(managedCategory === 'Other' || managedItem === 'Other') && (
                      <label>
                        Item name
                        <input required maxLength={100} value={managedCustomName} onChange={(event) => setManagedCustomName(event.target.value)} />
                      </label>
                    )}
                  </>
                ) : (
                  <label>
                    Item
                    <input required value={resourceType === 'medical' ? supplyForm.name : stockForm.itemName} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, name: event.target.value }) : setStockForm({ ...stockForm, itemName: event.target.value })} />
                  </label>
                )}
                <label>
                  Unit
                  <input required placeholder="e.g. boxes, litres, kg, sets" value={resourceType === 'medical' ? supplyForm.unit : stockForm.unit} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, unit: event.target.value }) : setStockForm({ ...stockForm, unit: event.target.value })} />
                </label>
                <div className="form-row">
                  <label>
                    Quantity on hand
                    <input required min="0.01" type="number" step={resourceType === 'medical' ? '1' : 'any'} value={resourceType === 'medical' ? supplyForm.quantityOnHand : stockForm.quantityOnHand} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, quantityOnHand: event.target.value }) : setStockForm({ ...stockForm, quantityOnHand: event.target.value })} />
                  </label>
                </div>
                {formError && <p className="form-error" role="alert">{formError}</p>}
                <div className="form-actions">
                  <button className="secondary-button" type="button" onClick={() => { setEditingId(null); setIsFormOpen(false) }}>Cancel</button>
                  <button className="primary-button" type="submit" disabled={isSubmitting}>{isSubmitting ? 'Saving...' : editingId ? 'Update resource' : 'Save resource'}</button>
                </div>
              </form>
            </section>
          </div>
        )}

        {page === 'overview' && (
          <section className="metric-grid" aria-label="Resource summary">
            <article className="metric-card accent-teal">
              <span className="metric-label">Pending requests</span>
              <strong>{isLoading ? '--' : helpRequests.filter(r => r.status === 'Pending').length}</strong>
              <span className="metric-note">Awaiting manager review</span>
            </article>
            <article className="metric-card accent-amber">
              <span className="metric-label">Active stock items</span>
              <strong>{isLoading ? '--' : totalSupplyValue.filter(item => item.quantityOnHand > 0).length}</strong>
              <span className="metric-note">{stockPercentage}% availability ratio</span>
            </article>
            <article className="metric-card accent-coral">
              <span className="metric-label">Low-stock alerts</span>
              <strong>{isLoading ? '--' : String(alerts.length).padStart(2, '0')}</strong>
              <span className="metric-note">Needs attention</span>
            </article>
            <article className="metric-card accent-blue">
              <span className="metric-label">Donation offers</span>
              <strong>{isLoading ? '--' : donations.length}</strong>
              <span className="metric-note">Awaiting review</span>
            </article>
          </section>
        )}

        {page === 'overview' && stockForecast && (
          <section className="stock-forecast" aria-labelledby="stock-forecast-title">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">30-day allocation history</p>
                <h2 id="stock-forecast-title">Stock forecast</h2>
              </div>
              <span>{stockForecast.summary}</span>
            </div>
            {stockForecastError && <p className="request-row-error" role="alert">{stockForecastError}</p>}
            <div className="stock-forecast-list">
              {stockForecast.items.map((item) => (
                <div className="stock-forecast-row" key={`${item.resourceType}:${item.resourceId}`}>
                  <div><strong>{item.name}</strong><span>{item.resourceType} · {item.quantityOnHand} {item.unit} on hand</span></div>
                  <div><strong>{item.estimatedDaysRemaining === null ? 'No recent use' : `${item.estimatedDaysRemaining} days`}</strong><span>{item.averageDailyUse} {item.unit}/day average</span></div>
                  <span className={`forecast-risk forecast-${item.riskLevel.toLowerCase()}`}>{item.riskLevel}</span>
                  <p>{item.suggestedAction}</p>
                </div>
              ))}
              {stockForecast.items.length === 0 && <p className="empty-state">No active stock to forecast.</p>}
            </div>
          </section>
        )}
        {page === 'overview' && stockForecastError && !stockForecast && <p className="request-row-error" role="alert">{stockForecastError}</p>}

        {page === 'overview' && (
          <section className="content-grid">
            <article className="resource-panel" id="supplies">
              <div className="panel-heading">
                <div>
                    <p className="eyebrow">Inventory watch</p>
                    <h2>Supply levels</h2>
                </div>
                <button className="text-button" type="button" onClick={() => navigate('supplies')}>View all</button>
              </div>
              <div className="supply-list">
                {isLoading && <p className="empty-state">Loading current inventory...</p>}
                {!isLoading && supplyLevels.length === 0 && <p className="empty-state">No inventory has been added yet.</p>}
                {supplyLevels.map((supply) => (
                  <div className="supply-row" key={supply.name}>
                    <span>{supply.name}</span>
                    <strong>{supply.percentage}%</strong>
                    <span className="resource-bar"><i style={{ width: `${supply.percentage}%` }} /></span>
                  </div>
                ))}
              </div>
            </article>

            <article className="resource-panel" id="recent-requests">
              <div className="panel-heading">
                <div>
                  <p className="eyebrow">Incoming activity</p>
                  <h2>Pending request queue</h2>
                </div>
                <button className="text-button" type="button" onClick={() => navigate('allocations')}>View all</button>
              </div>
              <div className="activity-list">
                {helpRequests.length === 0 && !isLoading && <p className="empty-state">No help requests received yet.</p>}
                {helpRequests.slice(0, 4).map((request) => (
                  <div key={request.id}>
                    <span className="activity-icon">R</span>
                    <p>
                      <strong>{request.needType}</strong> ({request.requesterName})<br />
                      <span>{request.district || 'District not set'} • {request.description}</span>
                    </p>
                    <time>{request.status}</time>
                  </div>
                ))}
              </div>
            </article>
          </section>
        )}

        {page === 'supplies' && (
          <section className="page-section">
            <div className="page-heading">
              <div>
                <p className="eyebrow">Inventory control</p>
                <h2>Supplies and stock</h2>
                <p className="resource-muted">Keep medical, food, and water resources ready for dispatch.</p>
              </div>
              <button className="primary-button" type="button" onClick={() => openResourceForm('medical')}>
                Add supply <span aria-hidden="true">+</span>
              </button>
            </div>
            <div className="category-inventory">
              {SUPPLY_CATEGORIES.map((category) => {
                const items = categoryInventory[category]
                return (
                  <section className="category-inventory-section" key={category} aria-labelledby={`inventory-${category.replace(/[^a-z0-9]/gi, '-').toLowerCase()}`}>
                    <div className="category-inventory-heading">
                      <h3 id={`inventory-${category.replace(/[^a-z0-9]/gi, '-').toLowerCase()}`}>{category}</h3>
                      <span>{items.length} {items.length === 1 ? 'item' : 'items'}</span>
                    </div>
                    <div className="data-table inventory-table-wrap">
                      <table className="inventory-table">
                        <thead><tr><th>Supply</th><th>Available quantity</th><th>Status</th><th>Actions</th></tr></thead>
                        <tbody>
                          {items.length === 0
                            ? <tr><td className="inventory-empty" colSpan={4}>No supplies in this category yet.</td></tr>
                            : items.map((item) => (
                              <tr key={item.id}>
                                <td>{item.name}</td>
                                <td>{item.quantity} {item.unit}</td>
                                <td><span className={`inventory-status ${item.quantity > 0 ? 'is-available' : 'is-empty'}`}>{item.quantity > 0 ? 'Available' : 'Out of stock'}</span></td>
                                <td className="inventory-actions">
                                  {item.type === 'medical' && <button className="edit-action" type="button" onClick={() => { const supply = medicalSupplies.find((value) => value.id === item.id); if (supply) editSupply(supply, item.quantity, item.sources.slice(1)) }}>Edit</button>}
                                  {item.type === 'food' && <button className="edit-action" type="button" onClick={() => { const stock = foodWaterStock.find((value) => value.id === item.id); if (stock) editStock(stock, item.quantity, item.sources.slice(1)) }}>Edit</button>}
                                  {item.type === 'managed' && <button className="edit-action" type="button" onClick={() => { const supply = managedSupplies.find((value) => value.id === item.id); if (supply) editManagedSupply(supply, item.quantity, item.sources.slice(1)) }}>Edit</button>}
                                  <button className="remove-action" type="button" onClick={() => setDeleteRequest({ type: item.type, id: item.id, label: item.name, duplicates: item.sources.slice(1) })}>Remove</button>
                                </td>
                              </tr>
                            ))}
                        </tbody>
                      </table>
                    </div>
                  </section>
                )
              })}
            </div>
          </section>
        )}

        {page === 'donate' && (
          <section className="page-section">
            <div className="page-heading">
              <div>
                <p className="eyebrow">Community support</p>
                <h2>Donation offers</h2>
                <p className="muted">Review items offered by people who want to support the response effort.</p>
              </div>
            </div>
            <div className="data-table">
              {isLoading && <p className="empty-state">Loading donations...</p>}
              {!isLoading && donations.length === 0 && <p className="empty-state">No donation offers yet.</p>}
              {donationGroups.map(({ key, items }) => {
                const first = items[0]
                const pending = items.some((item) => item.status === 'PendingReview')
                const accepted = items.every((item) => item.status === 'Accepted')
                const rejected = items.every((item) => item.status === 'Rejected')
                return (
                  <div className="data-row request-row" key={key}>
                    <div>
                      <strong>{items.length === 1 ? first.donationType : `${items.length} donated items`}</strong>
                      {items.map((item) => (
                        <span key={item.id}>{item.donationType}: {item.quantity} {item.unit}</span>
                      ))}
                    </div>
                    <div>
                      <strong>{first.donorName}</strong>
                      <span>{first.contactNumber}</span>
                      <span>{first.district || 'District not set'}</span>
                    </div>
                    <div>
                      <strong>{accepted ? 'Accepted' : rejected ? 'Rejected' : first.status}</strong>
                      <span>{items.map((item) => item.notes).filter((note): note is string => Boolean(note)).join(' · ') || 'No notes provided'}</span>
                    </div>
                    <div className="request-actions">
                      {pending && (
                        <>
                          <button className="primary-button compact-button" type="button" disabled={updatingDonationKey === key} onClick={() => void updateDonationStatus(items, 'Accepted')}>{updatingDonationKey === key ? 'Accepting…' : 'Accept all'}</button>
                          <button className="danger-button compact-button" type="button" disabled={updatingDonationKey === key} onClick={() => void updateDonationStatus(items, 'Rejected')}>{updatingDonationKey === key ? 'Updating…' : 'Reject all'}</button>
                        </>
                      )}
                      {accepted && <span className="fulfilled-label">Accepted</span>}
                      {rejected && <span className="fulfilled-label">Rejected</span>}
                    </div>
                  </div>
                )
              })}
            </div>
          </section>
        )}

        {page === 'allocations' && (
          <section className="page-section">
            <div className="page-heading">
              <div>
                <p className="eyebrow">Incoming requests</p>
                <h2>Resource requests</h2>
                <p className="muted">Review support requests, accept or reject them, and send available resources.</p>
              </div>
              <button className="secondary-button" type="button" disabled={loadingAllocationPlan} onClick={() => void getAllocationPlan()}>
                {loadingAllocationPlan ? 'Planning...' : 'Plan all requests'}
              </button>
            </div>
            {allocationPlan && (
              <section className="stock-forecast" aria-labelledby="allocation-plan-title">
                <div className="panel-heading">
                  <div>
                    <p className="eyebrow">Priority order across pending requests</p>
                    <h2 id="allocation-plan-title">Allocation plan</h2>
                  </div>
                  <span>{allocationPlan.summary}</span>
                </div>
                <div className="stock-forecast-list">
                  {allocationPlan.items.map((item) => (
                    <div className="stock-forecast-row" key={item.helpRequestId}>
                      <div><strong>#{item.priority} · {item.needType}</strong><span>{item.requesterName}</span></div>
                      <div>
                        <strong>{item.decision === 'Recommend' ? `${item.resourceName ?? item.resourceType} · ${item.quantity} ${item.unit ?? 'units'}` : 'No safe match'}</strong>
                        {item.decision === 'Recommend' && <span>{item.availableQuantity} {item.unit} available before this request</span>}
                      </div>
                      <span className={`forecast-risk forecast-${item.decision === 'Recommend' ? 'stable' : 'critical'}`}>{item.decision}</span>
                      <p>{item.reason}{item.warnings.length > 0 ? ` — ${item.warnings.join(' ')}` : ''}</p>
                    </div>
                  ))}
                  {allocationPlan.items.length === 0 && <p className="empty-state">No pending requests to plan for.</p>}
                </div>
                <p className="muted">Manager approval is still required — use Accept and Send resources below to act on a request.</p>
              </section>
            )}
            {allocationPlanError && <p className="request-row-error" role="alert">{allocationPlanError}</p>}
            <div className="data-table">
              {isLoading && <p className="empty-state">Loading requests...</p>}
              {!isLoading && helpRequests.length === 0 && <p className="empty-state">No resource requests yet.</p>}
              {helpRequests.map((request) => (
                <div className="data-row request-row" key={request.id}>
                  <div>
                    <strong>{request.needType}</strong>
                    <span>{request.description}</span>
                  </div>
                  <div>
                    <strong>{request.requesterName}</strong>
                    <span>{request.contactNumber}</span>
                    <span>{request.district || 'District not set'}</span>
                  </div>
                  <div>
                    <strong>{request.status}</strong>
                    <span>Request status</span>
                  </div>
                  <div className="request-actions">
                    {request.status === 'Pending' && (
                      <>
                        <button
                          className="secondary-button compact-button"
                          type="button"
                          disabled={loadingRecommendationId === request.id}
                          onClick={() => void getAllocationRecommendation(request.id)}
                        >
                          {loadingRecommendationId === request.id ? 'Thinking...' : 'AI recommendation'}
                        </button>
                        <button className="primary-button compact-button" type="button" disabled={updatingRequestId === request.id} onClick={() => void updateRequestStatus(request.id, 'Accepted')}>{updatingRequestId === request.id ? 'Accepting…' : 'Accept'}</button>
                        <button className="danger-button compact-button" type="button" disabled={updatingRequestId === request.id} onClick={() => void updateRequestStatus(request.id, 'Rejected')}>{updatingRequestId === request.id ? 'Updating…' : 'Reject'}</button>
                      </>
                    )}
                    {request.status === 'Accepted' && !fulfillmentRequest && (
                      <>
                      <button
                        className="primary-button compact-button"
                        type="button"
                        disabled={availableSupplies.length === 0}
                        onClick={() =>
                          setFulfillmentRequest({
                            id: request.id,
                            supplyKey: preferredSupplyKey(request.needType),
                            quantity: '1',
                          })
                        }
                      >
                        Send resources
                      </button>
                      {availableSupplies.length === 0 && <span>No supplies in stock</span>}
                      </>
                    )}
                    {recommendation?.id === request.id && (
                      <div className="recommendation-result">
                        <strong>{recommendation.result.decision === 'Recommend' ? `Recommend ${recommendation.result.resourceName ?? recommendation.result.resourceType} · ${recommendation.result.quantity} ${recommendation.result.unit ?? 'units'}` : 'No safe match'}</strong>
                        {recommendation.result.decision === 'Recommend' && <span>{recommendation.result.availableQuantity} {recommendation.result.unit} currently available</span>}
                        <span>{recommendation.result.reason}</span>
                        {recommendation.result.warnings.map((warning) => <span key={warning}>{warning}</span>)}
                      </div>
                    )}
                    {request.status === 'Fulfilled' && <span className="fulfilled-label">Sent</span>}
                  </div>
                  {fulfillmentRequest?.id === request.id && (
                    <div className="fulfillment-form">
                      <label>
                        Resource
                        <select
                          required
                          value={fulfillmentRequest.supplyKey}
                          onChange={(event) =>
                            setFulfillmentRequest({
                              ...fulfillmentRequest,
                              supplyKey: event.target.value,
                            })
                          }
                        >
                          <option value="" disabled>Select an available supply</option>
                          {availableSupplies.map((supply) => (
                            <option key={supply.key} value={supply.key}>
                              {supply.name} · {supply.quantityOnHand} {supply.unit} · {supply.detail}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        Quantity
                        <input
                          required
                          min="1"
                          step={fulfillmentRequest.supplyKey.startsWith('MedicalSupply:') ? '1' : 'any'}
                          type="number"
                          value={fulfillmentRequest.quantity}
                          onChange={(event) =>
                            setFulfillmentRequest({
                              ...fulfillmentRequest,
                              quantity: event.target.value,
                            })
                          }
                        />
                      </label>
                      <button
                        className="secondary-button"
                        type="button"
                        onClick={() => setFulfillmentRequest(null)}
                      >
                        Cancel
                      </button>
                      <button
                        className="primary-button"
                        type="button"
                        disabled={sendingRequestId === request.id || !fulfillmentRequest.supplyKey}
                        onClick={() => void fulfillRequest()}
                      >
                        {sendingRequestId === request.id ? 'Sending…' : 'Send now'}
                      </button>
                    </div>
                  )}
                  {requestActionError?.id === request.id && (
                    <p className="request-row-error" role="alert">{requestActionError.message}</p>
                  )}
                </div>
              ))}
            </div>
          </section>
        )}

        {page === 'history' && (
          <section className="page-section history-page">
            <div className="page-heading">
              <div>
                <p className="eyebrow">Processed items</p>
                <h2>Request history</h2>
                <p className="muted">Processed requests stay here for up to {HISTORY_RETENTION_DAYS} days.</p>
              </div>
            </div>
            <div className="data-table">
              {requestHistory.length === 0 && !isLoading && <p className="empty-state">No processed requests in history.</p>}
              {requestHistory.map((request) => {
                const days = daysUntilHistoryDeletion(historyDates[`request:${request.id}`], historyClock)
                return (
                  <div className="data-row request-row history-row" key={request.id}>
                    <div><strong>{request.needType}</strong><span>{request.description}</span></div>
                    <div><strong>{request.requesterName}</strong><span>{request.contactNumber}</span><span>{request.district || 'District not set'}</span></div>
                    <div><strong>{request.status}</strong><span>Request status</span></div>
                    <span className="history-countdown">Auto-delete in {days} {days === 1 ? 'day' : 'days'}</span>
                  </div>
                )
              })}
            </div>

            <div className="page-heading history-section-heading">
              <div>
                <p className="eyebrow">Processed items</p>
                <h2>Donation history</h2>
                <p className="muted">Processed donations stay here for up to {HISTORY_RETENTION_DAYS} days.</p>
              </div>
            </div>
            <div className="data-table">
              {donationHistory.length === 0 && !isLoading && <p className="empty-state">No processed donations in history.</p>}
              {donationHistory.map((donation) => {
                const days = daysUntilHistoryDeletion(historyDates[`donation:${donation.id}`], historyClock)
                return (
                  <div className="data-row request-row history-row" key={donation.id}>
                    <div><strong>{donation.donationType}</strong><span>{donation.quantity} {donation.unit}</span></div>
                    <div><strong>{donation.donorName}</strong><span>{donation.contactNumber}</span><span>{donation.district || 'District not set'}</span></div>
                    <div><strong>{donation.status}</strong><span>Donation status</span></div>
                    <span className="history-countdown">Auto-delete in {days} {days === 1 ? 'day' : 'days'}</span>
                  </div>
                )
              })}
            </div>
          </section>
        )}
      </div>
    </div>
  )
}

export default ResourceDashboard
