import { useEffect, useState } from 'react'
import { getSession } from '../../shared/auth/session'
import './ResourceDashboard.css'

/**
 * Creating, changing and deleting resources is staff work, so those calls
 * carry the signed-in Resource Manager's token. Reads stay anonymous.
 */
function authHeaders(base?: Record<string, string>): Record<string, string> {
  const token = getSession()?.token
  return { ...(base ?? {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) }
}

type Shelter = { id: string; name: string; address: string; latitude: number; longitude: number; capacity: number; occupiedCapacity: number }
type Supply = { id: string; name: string; unit: string; quantityOnHand: number; lowStockThreshold: number }
type FoodWaterStock = { id: string; itemName: string; unit: string; quantityOnHand: number; lowStockThreshold: number }
type ResourceAlert = { resourceType: string; resourceId: string; name: string; quantityOnHand: number; lowStockThreshold: number; unit: string }
type ShelterForm = { name: string; address: string; latitude: string; longitude: string; capacity: string }
type SupplyForm = { name: string; unit: string; quantityOnHand: string; lowStockThreshold: string }
type StockForm = { itemName: string; unit: string; quantityOnHand: string; lowStockThreshold: string }
type ResourceType = 'shelter' | 'medical' | 'food'
type Page = 'overview' | 'shelters' | 'supplies' | 'allocations'
type DeleteRequest = { type: ResourceType; id: string; label: string }

async function getResources<T>(path: string): Promise<T> {
  const response = await fetch(`/api/resources/${path}`)
  if (!response.ok) {
    const result = await response.json().catch(() => null) as { error?: string } | null
    throw new Error(result?.error ?? `Unable to load ${path}.`)
  }
  return response.json() as Promise<T>
}

function ResourceDashboard() {
  const [shelters, setShelters] = useState<Shelter[]>([])
  const [medicalSupplies, setMedicalSupplies] = useState<Supply[]>([])
  const [foodWaterStock, setFoodWaterStock] = useState<FoodWaterStock[]>([])
  const [alerts, setAlerts] = useState<ResourceAlert[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState('')
  const [isFormOpen, setIsFormOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [formError, setFormError] = useState('')
  const [resourceType, setResourceType] = useState<ResourceType>('shelter')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [shelterForm, setShelterForm] = useState<ShelterForm>({ name: '', address: '', latitude: '', longitude: '', capacity: '' })
  const [supplyForm, setSupplyForm] = useState<SupplyForm>({ name: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
  const [stockForm, setStockForm] = useState<StockForm>({ itemName: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
  const [page, setPage] = useState<Page>('overview')
  const [deleteRequest, setDeleteRequest] = useState<DeleteRequest | null>(null)

  const loadResources = async () => {
    setIsLoading(true)
    setError('')
    try {
      const [shelterData, medicalData, foodData, alertData] = await Promise.all([
        getResources<Shelter[]>('shelters'),
        getResources<Supply[]>('medical-supplies'),
        getResources<FoodWaterStock[]>('food-water-stock'),
        getResources<ResourceAlert[]>('alerts/low-stock'),
      ])
      setShelters(shelterData)
      setMedicalSupplies(medicalData)
      setFoodWaterStock(foodData)
      setAlerts(alertData)
    } catch (resourceError) {
      setError(resourceError instanceof Error ? resourceError.message : 'Unable to load resources.')
    } finally {
      setIsLoading(false)
    }
  }

  const submitShelter = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setIsSubmitting(true)
    setFormError('')
    try {
      const response = await fetch(editingId ? `/api/resources/shelters/${editingId}` : '/api/resources/shelters', {
        method: editingId ? 'PUT' : 'POST',
        headers: authHeaders({ 'Content-Type': 'application/json' }),
        body: JSON.stringify({
          name: shelterForm.name,
          address: shelterForm.address,
          latitude: Number(shelterForm.latitude),
          longitude: Number(shelterForm.longitude),
          capacity: Number(shelterForm.capacity),
        }),
      })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string } | null
        throw new Error(result?.error ?? 'Unable to create the shelter.')
      }
      setShelterForm({ name: '', address: '', latitude: '', longitude: '', capacity: '' })
      setEditingId(null)
      setIsFormOpen(false)
      await loadResources()
    } catch (submitError) {
      setFormError(submitError instanceof Error ? submitError.message : 'Unable to create the shelter.')
    } finally {
      setIsSubmitting(false)
    }
  }

  const submitInventory = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setIsSubmitting(true)
    setFormError('')
    const isMedical = resourceType === 'medical'
    const path = isMedical ? 'medical-supplies' : 'food-water-stock'
    const form = isMedical ? supplyForm : stockForm
    const body = isMedical
      ? { name: supplyForm.name, unit: supplyForm.unit, quantityOnHand: Number(supplyForm.quantityOnHand), lowStockThreshold: Number(supplyForm.lowStockThreshold) }
      : { itemName: stockForm.itemName, unit: stockForm.unit, quantityOnHand: Number(stockForm.quantityOnHand), lowStockThreshold: Number(stockForm.lowStockThreshold) }
    try {
      const response = await fetch(editingId ? `/api/resources/${path}/${editingId}` : `/api/resources/${path}`, { method: editingId ? 'PUT' : 'POST', headers: authHeaders({ 'Content-Type': 'application/json' }), body: JSON.stringify(body) })
      if (!response.ok) {
        const result = await response.json().catch(() => null) as { error?: string; title?: string } | null
        throw new Error(result?.error ?? result?.title ?? `Unable to create ${isMedical ? 'the medical supply' : 'the food or water stock'}.`)
      }
      if (isMedical) setSupplyForm({ name: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
      else setStockForm({ itemName: '', unit: '', quantityOnHand: '', lowStockThreshold: '' })
      setEditingId(null)
      setIsFormOpen(false)
      await loadResources()
    } catch (submitError) {
      setFormError(submitError instanceof Error ? submitError.message : `Unable to create ${form.unit}.`)
    } finally {
      setIsSubmitting(false)
    }
  }

  useEffect(() => { void loadResources() }, [])

  const totalSupplyValue = [...medicalSupplies, ...foodWaterStock]
  const totalAvailable = totalSupplyValue.reduce((sum, item) => sum + Number(item.quantityOnHand), 0)
  const totalThreshold = totalSupplyValue.reduce((sum, item) => sum + Number(item.lowStockThreshold), 0)
  const stockPercentage = totalAvailable + totalThreshold === 0
    ? 0
    : Math.round((totalAvailable / (totalAvailable + totalThreshold)) * 100)
  const supplyLevels = totalSupplyValue.slice(0, 3).map((item) => ({
    name: 'name' in item ? item.name : item.itemName,
    percentage: Math.min(100, Math.round((Number(item.quantityOnHand) / Math.max(1, Number(item.quantityOnHand) + Number(item.lowStockThreshold))) * 100)),
  }))
  const recentResources = totalSupplyValue.slice(0, 3).map((item) => ({
    label: 'name' in item ? item.name : item.itemName,
    type: 'name' in item ? 'Medical supply' : 'Food / water stock',
    icon: 'name' in item ? 'M' : 'F',
  }))

  const openResourceForm = (type: ResourceType) => {
    setFormError('')
    setResourceType(type)
    setEditingId(null)
    setIsFormOpen(true)
  }

  const editShelter = (shelter: Shelter) => {
    setResourceType('shelter')
    setEditingId(shelter.id)
    setShelterForm({ name: shelter.name, address: shelter.address, latitude: String(shelter.latitude), longitude: String(shelter.longitude), capacity: String(shelter.capacity) })
    setFormError('')
    setIsFormOpen(true)
  }

  const editSupply = (supply: Supply) => {
    setResourceType('medical')
    setEditingId(supply.id)
    setSupplyForm({ name: supply.name, unit: supply.unit, quantityOnHand: String(supply.quantityOnHand), lowStockThreshold: String(supply.lowStockThreshold) })
    setFormError('')
    setIsFormOpen(true)
  }

  const editStock = (stock: FoodWaterStock) => {
    setResourceType('food')
    setEditingId(stock.id)
    setStockForm({ itemName: stock.itemName, unit: stock.unit, quantityOnHand: String(stock.quantityOnHand), lowStockThreshold: String(stock.lowStockThreshold) })
    setFormError('')
    setIsFormOpen(true)
  }

  const removeResource = async (type: ResourceType, id: string, label: string) => {
    setError('')
    try {
      const path = type === 'shelter' ? 'shelters' : type === 'medical' ? 'medical-supplies' : 'food-water-stock'
      const response = await fetch(`/api/resources/${path}/${id}`, { method: 'DELETE', headers: authHeaders() })
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
  }

  const navigate = (nextPage: Page) => setPage(nextPage)

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark">RS</span>
          <span>Rescue Sri Lanka</span>
        </div>
        <nav aria-label="Main navigation">
          <button className={`nav-link ${page === 'overview' ? 'active' : ''}`} type="button" onClick={() => navigate('overview')}>Overview</button>
          <button className={`nav-link ${page === 'shelters' ? 'active' : ''}`} type="button" onClick={() => navigate('shelters')}>Shelters</button>
          <button className={`nav-link ${page === 'supplies' ? 'active' : ''}`} type="button" onClick={() => navigate('supplies')}>Supplies</button>
          <button className={`nav-link ${page === 'allocations' ? 'active' : ''}`} type="button" onClick={() => navigate('allocations')}>Allocations</button>
        </nav>
        <div className="sidebar-footer">
          <span className="status-dot" />
          <span>Response network online</span>
        </div>
      </aside>

      <main className="main-content" id="overview">
        <header className="topbar">
          <div>
            <p className="eyebrow">Emergency response operations</p>
            <h1>{page === 'overview' ? 'Resource overview' : page[0].toUpperCase() + page.slice(1)}</h1>
          </div>
          <button className="profile-button" type="button" aria-label="Open user profile">DR</button>
        </header>

        {page === 'overview' && <section className="welcome-panel" aria-labelledby="welcome-title">
          <div>
            <p className="eyebrow">Friday, 18 September 2026</p>
            <h2 id="welcome-title">Ready to coordinate relief.</h2>
            <p className="muted">Track essential resources and move support where it is needed most.</p>
          </div>
          <button className="primary-button" type="button" onClick={() => openResourceForm('shelter')}>Add resource <span aria-hidden="true">+</span></button>
        </section>
        }

        {error && <div className="api-error" role="alert"><span>{error}</span><button type="button" onClick={() => void loadResources()}>Try again</button></div>}

        {deleteRequest && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.currentTarget === event.target) setDeleteRequest(null) }}>
          <section className="confirm-modal" role="alertdialog" aria-modal="true" aria-labelledby="remove-title">
            <span className="confirm-icon" aria-hidden="true">!</span>
            <p className="eyebrow">Remove resource</p>
            <h2 id="remove-title">Remove {deleteRequest.label}?</h2>
            <p className="muted">This resource will no longer appear in your active inventory. Existing allocation history will remain safe.</p>
            <div className="form-actions"><button className="secondary-button large-action" type="button" onClick={() => setDeleteRequest(null)}>Keep resource</button><button className="danger-button large-action" type="button" onClick={() => void confirmRemove()}>Remove resource</button></div>
          </section>
        </div>}

        {isFormOpen && <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.currentTarget === event.target) setIsFormOpen(false) }}>
          <section className="modal" role="dialog" aria-modal="true" aria-labelledby="resource-form-title">
            <div className="modal-heading"><div><p className="eyebrow">{editingId ? 'Update resource' : 'New resource'}</p><h2 id="resource-form-title">{editingId ? 'Edit' : 'Add'} {resourceType === 'shelter' ? 'a shelter' : resourceType === 'medical' ? 'medical supplies' : 'food or water stock'}</h2></div><button className="close-button" type="button" aria-label="Close form" onClick={() => { setEditingId(null); setIsFormOpen(false) }}>x</button></div>
            <div className="resource-tabs" role="tablist" aria-label="Resource type"><button className={resourceType === 'shelter' ? 'selected' : ''} type="button" onClick={() => { setResourceType('shelter'); setFormError('') }}>Shelter</button><button className={resourceType === 'medical' ? 'selected' : ''} type="button" onClick={() => { setResourceType('medical'); setFormError('') }}>Medical supply</button><button className={resourceType === 'food' ? 'selected' : ''} type="button" onClick={() => { setResourceType('food'); setFormError('') }}>Food / water</button></div>
            {resourceType === 'shelter' && <form onSubmit={submitShelter}>
              <label>Name<input required value={shelterForm.name} onChange={(event) => setShelterForm({ ...shelterForm, name: event.target.value })} /></label>
              <label>Address<input required value={shelterForm.address} onChange={(event) => setShelterForm({ ...shelterForm, address: event.target.value })} /></label>
              <div className="form-row"><label>Latitude<input required type="number" step="any" value={shelterForm.latitude} onChange={(event) => setShelterForm({ ...shelterForm, latitude: event.target.value })} /></label><label>Longitude<input required type="number" step="any" value={shelterForm.longitude} onChange={(event) => setShelterForm({ ...shelterForm, longitude: event.target.value })} /></label></div>
              <label>Capacity<input required min="0" type="number" value={shelterForm.capacity} onChange={(event) => setShelterForm({ ...shelterForm, capacity: event.target.value })} /></label>
              {formError && <p className="form-error" role="alert">{formError}</p>}<div className="form-actions"><button className="secondary-button" type="button" onClick={() => { setEditingId(null); setIsFormOpen(false) }}>Cancel</button><button className="primary-button" type="submit" disabled={isSubmitting}>{isSubmitting ? 'Saving...' : editingId ? 'Update shelter' : 'Save shelter'}</button></div>
            </form>}
            {resourceType !== 'shelter' && <form onSubmit={submitInventory}>
              <label>{resourceType === 'medical' ? 'Supply name' : 'Item name'}<input required value={resourceType === 'medical' ? supplyForm.name : stockForm.itemName} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, name: event.target.value }) : setStockForm({ ...stockForm, itemName: event.target.value })} /></label>
              <label>Unit<input required placeholder="e.g. boxes, litres, kg" value={resourceType === 'medical' ? supplyForm.unit : stockForm.unit} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, unit: event.target.value }) : setStockForm({ ...stockForm, unit: event.target.value })} /></label>
              <div className="form-row"><label>Quantity on hand<input required min="0" type="number" step="any" value={resourceType === 'medical' ? supplyForm.quantityOnHand : stockForm.quantityOnHand} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, quantityOnHand: event.target.value }) : setStockForm({ ...stockForm, quantityOnHand: event.target.value })} /></label><label>Low-stock threshold<input required min="0" type="number" step="any" value={resourceType === 'medical' ? supplyForm.lowStockThreshold : stockForm.lowStockThreshold} onChange={(event) => resourceType === 'medical' ? setSupplyForm({ ...supplyForm, lowStockThreshold: event.target.value }) : setStockForm({ ...stockForm, lowStockThreshold: event.target.value })} /></label></div>
              {formError && <p className="form-error" role="alert">{formError}</p>}<div className="form-actions"><button className="secondary-button" type="button" onClick={() => { setEditingId(null); setIsFormOpen(false) }}>Cancel</button><button className="primary-button" type="submit" disabled={isSubmitting}>{isSubmitting ? 'Saving...' : editingId ? 'Update resource' : 'Save resource'}</button></div>
            </form>}
          </section>
        </div>}

        {page === 'overview' && <section className="metric-grid" aria-label="Resource summary">
          <article className="metric-card accent-teal">
            <span className="metric-label">Active shelters</span>
            <strong>{isLoading ? '--' : shelters.length}</strong>
            <span className="metric-note">Active locations</span>
          </article>
          <article className="metric-card accent-amber">
            <span className="metric-label">Medical supplies</span>
            <strong>{isLoading ? '--' : `${stockPercentage}%`}</strong>
            <span className="metric-note">Stock availability</span>
          </article>
          <article className="metric-card accent-coral">
            <span className="metric-label">Low-stock alerts</span>
            <strong>{isLoading ? '--' : String(alerts.length).padStart(2, '0')}</strong>
            <span className="metric-note">Needs attention today</span>
          </article>
          <article className="metric-card accent-blue">
            <span className="metric-label">Active allocations</span>
            <strong>31</strong>
            <span className="metric-note">12 dispatched this week</span>
          </article>
        </section>}

        {page === 'overview' && <section className="content-grid">
          <article className="panel" id="supplies">
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
              {supplyLevels.map((supply) => <div className="supply-row" key={supply.name}><span>{supply.name}</span><strong>{supply.percentage}%</strong><span className="bar"><i style={{ width: `${supply.percentage}%` }} /></span></div>)}
            </div>
          </article>

          <article className="panel" id="shelters">
            <div className="panel-heading">
              <div>
                <p className="eyebrow">Latest activity</p>
                <h2>Recent allocations</h2>
              </div>
              <button className="text-button" type="button" onClick={() => navigate('allocations')}>View all</button>
            </div>
            <div className="activity-list">
              {recentResources.length === 0 && !isLoading && <p className="empty-state">Your saved resources will appear here.</p>}
              {recentResources.map((resource) => <div key={resource.label}><span className="activity-icon">{resource.icon}</span><p><strong>{resource.label}</strong><br /><span>{resource.type}</span></p><time>Saved</time></div>)}
            </div>
          </article>
        </section>}

        {page === 'shelters' && <section className="page-section">
          <div className="page-heading"><div><p className="eyebrow">Safe locations</p><h2>Active shelters</h2><p className="muted">Manage capacity and locations available to people affected by emergencies.</p></div><button className="primary-button" type="button" onClick={() => openResourceForm('shelter')}>Add shelter <span aria-hidden="true">+</span></button></div>
          <div className="data-table">{isLoading && <p className="empty-state">Loading shelters...</p>}{!isLoading && shelters.length === 0 && <p className="empty-state">No shelters have been added yet.</p>}{shelters.map((shelter) => <div className="data-row" key={shelter.id}><div><strong>{shelter.name}</strong><span>{shelter.address ?? 'Location details unavailable'}</span></div><div><strong>{shelter.capacity - shelter.occupiedCapacity}</strong><span>spaces available</span></div><div className="row-actions"><button className="edit-action" type="button" onClick={() => editShelter(shelter)}>Edit</button><button className="remove-action" type="button" onClick={() => setDeleteRequest({ type: 'shelter', id: shelter.id, label: shelter.name })}>Remove</button></div></div>)}</div>
        </section>}

        {page === 'supplies' && <section className="page-section">
          <div className="page-heading"><div><p className="eyebrow">Inventory control</p><h2>Supplies and stock</h2><p className="muted">Keep medical, food, and water resources ready for dispatch.</p></div><button className="primary-button" type="button" onClick={() => openResourceForm('medical')}>Add supply <span aria-hidden="true">+</span></button></div>
          <div className="supply-cards">{medicalSupplies.map((supply) => <div className="inventory-card" key={supply.id}><span className="inventory-type">Medical</span><strong>{supply.name}</strong><span>{supply.quantityOnHand} {supply.unit} available</span><small>Alert at {supply.lowStockThreshold} {supply.unit}</small><div className="card-actions"><button className="edit-action" type="button" onClick={() => editSupply(supply)}>Edit</button><button className="remove-action" type="button" onClick={() => setDeleteRequest({ type: 'medical', id: supply.id, label: supply.name })}>Remove</button></div></div>)}{foodWaterStock.map((stock) => <div className="inventory-card" key={stock.id}><span className="inventory-type food">Food / water</span><strong>{stock.itemName}</strong><span>{stock.quantityOnHand} {stock.unit} available</span><small>Alert at {stock.lowStockThreshold} {stock.unit}</small><div className="card-actions"><button className="edit-action" type="button" onClick={() => editStock(stock)}>Edit</button><button className="remove-action" type="button" onClick={() => setDeleteRequest({ type: 'food', id: stock.id, label: stock.itemName })}>Remove</button></div></div>)}</div>
          <button className="secondary-button add-food-button" type="button" onClick={() => openResourceForm('food')}>Add food or water stock</button>
        </section>}

        {page === 'allocations' && <section className="page-section"><div className="page-heading"><div><p className="eyebrow">Distribution tracking</p><h2>Resource allocations</h2><p className="muted">Track resources dispatched to shelters, field units, and incident responses.</p></div></div><div className="empty-panel"><span className="empty-symbol">↗</span><h2>Allocation history is ready for the next step</h2><p className="muted">Create an allocation from an available resource once help requests are connected.</p></div></section>}
      </main>
    </div>
  )
}

export default ResourceDashboard
