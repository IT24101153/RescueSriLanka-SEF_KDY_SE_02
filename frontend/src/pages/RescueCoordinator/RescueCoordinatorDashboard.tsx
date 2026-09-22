import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../../api/client'
import {
  createAssignment,
  decideAssignment,
  getAssignments,
  getDispatches,
  getRescueTeams,
  reviseAssignment,
  transitionDispatch,
  validateAssignment,
} from '../../api/rescueCoordination'
import type {
  AssignmentDto,
  DispatchDto,
  RescueTeamDto,
  SafetyValidationWorkflowResultDto,
  SkillType,
} from '../../types/rescueCoordination'
import type { User } from '../../auth/session'
import './RescueCoordinatorDashboard.css'
import './RescueCoordinatorSafety.css'
import ResourceManagementPanel from './ResourceManagementPanel'

type View = 'overview' | 'teams' | 'assignments' | 'safety' | 'dispatches'
type LoadErrors = Partial<Record<'teams' | 'assignments' | 'dispatches', string>>

const skills: SkillType[] = ['WaterRescue', 'FirstAid', 'Paramedic', 'StructuralCollapse', 'FireResponse', 'Logistics', 'Driving']
const views: Array<{ id: View; label: string; hint: string }> = [
  { id: 'overview', label: 'Overview', hint: 'Operational picture' },
  { id: 'teams', label: 'Rescue teams', hint: 'People and vehicles' },
  { id: 'assignments', label: 'Assignments', hint: 'Plan response work' },
  { id: 'safety', label: 'AI safety review', hint: 'Human decision required' },
  { id: 'dispatches', label: 'Active dispatches', hint: 'Mission lifecycle' },
]

function errorMessage(error: unknown) {
  if (!(error instanceof ApiError)) return 'Network or server error. Please retry.'
  return ({
    401: 'Your session has expired. Please sign in again.',
    403: 'You do not have permission for this operation.',
    404: 'The requested resource is no longer available.',
    409: error.message || 'The operation conflicts with the current plan. Refresh and retry.',
  }[error.status] ?? `Unable to complete the request (HTTP ${error.status}). Please retry.`)
}

const nextStatus = (status: DispatchDto['status']) =>
  status === 'Dispatched' ? { status: 'EnRoute' as const, label: 'Mark en route' }
    : status === 'EnRoute' ? { status: 'OnScene' as const, label: 'Mark on scene' }
      : status === 'OnScene' ? { status: 'Resolved' as const, label: 'Resolve mission' }
        : null

// TODO: Replace this generated testing reference with a real Component A incident selector.
const createIncidentId = () => crypto.randomUUID()

export default function RescueCoordinatorDashboard({ user, onLogout }: { user: User; onLogout: () => void }) {
  const [view, setView] = useState<View>('overview')
  const [teams, setTeams] = useState<RescueTeamDto[]>([])
  const [assignments, setAssignments] = useState<AssignmentDto[]>([])
  const [dispatches, setDispatches] = useState<DispatchDto[]>([])
  const [errors, setErrors] = useState<LoadErrors>({})
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [selected, setSelected] = useState<AssignmentDto | null>(null)
  const [validation, setValidation] = useState<SafetyValidationWorkflowResultDto | null>(null)
  const [teamId, setTeamId] = useState('')
  const [vehicleId, setVehicleId] = useState('')
  const [incidentId, setIncidentId] = useState<string>(createIncidentId)
  const [skill, setSkill] = useState<SkillType>('FirstAid')
  const [capacity, setCapacity] = useState(1)
  const [notes, setNotes] = useState('')
  const [actionError, setActionError] = useState<string | null>(null)

  const selectedTeam = teams.find((team) => team.id === teamId)
  const selectedVehicles = selectedTeam?.vehicles ?? []
  const availableTeams = teams.filter((team) => team.status === 'Available')
  const onMissionTeams = teams.filter((team) => team.status === 'OnMission')
  const availableVehicles = teams.flatMap((team) => team.vehicles).filter((vehicle) => vehicle.status === 'Available')
  const pendingAssignments = assignments.filter((assignment) => assignment.status === 'Proposed' || assignment.status === 'PendingApproval')
  const activeDispatches = dispatches.filter((dispatch) => !['Resolved', 'Cancelled'].includes(dispatch.status))
  const canApprove = Boolean(selected && validation?.workflowId && validation.assignmentId === selected.id && validation.planVersion === selected.planVersion && validation.decision === 'APPROVE' && !validation.isStale && !busy)

  async function refresh() {
    setLoading(true)
    const results = await Promise.allSettled([getRescueTeams(), getAssignments(), getDispatches()])
    const nextErrors: LoadErrors = {}
    if (results[0].status === 'fulfilled') setTeams(results[0].value); else nextErrors.teams = errorMessage(results[0].reason)
    const assignmentResult = results[1]
    if (assignmentResult.status === 'fulfilled') {
      const nextAssignments = assignmentResult.value
      setAssignments(nextAssignments)
      setSelected((current) => nextAssignments.find((assignment) => assignment.id === current?.id) ?? current)
    } else nextErrors.assignments = errorMessage(assignmentResult.reason)
    if (results[2].status === 'fulfilled') setDispatches(results[2].value); else nextErrors.dispatches = errorMessage(results[2].reason)
    setErrors(nextErrors)
    setLoading(false)
  }

  useEffect(() => { void refresh() }, [])

  function chooseAssignment(assignment: AssignmentDto) {
    setSelected(assignment)
    setValidation(null)
    setActionError(null)
  }

  function startNewAssignment() {
    setSelected(null); setValidation(null); setActionError(null); setIncidentId(createIncidentId()); setTeamId(''); setVehicleId(''); setSkill('FirstAid'); setCapacity(1); setNotes('')
  }

  function planError() {
    if (!teamId || !vehicleId || capacity < 1) return 'Select a team, vehicle, and capacity of at least one.'
    if (!selectedTeam || selectedTeam.status !== 'Available') return 'Selected rescue team must be available.'
    const vehicle = selectedVehicles.find((item) => item.id === vehicleId)
    if (!vehicle || vehicle.status !== 'Available') return 'Selected vehicle must be available.'
    if (capacity > vehicle.capacity) return 'Required capacity exceeds the selected vehicle capacity.'
    if (!selectedTeam.members.some((member) => member.isAvailable && member.skill === skill)) return `Selected team has no available member with ${skill}.`
    return null
  }

  async function submitAssignment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const invalid = planError()
    if (invalid) {
      setActionError(invalid)
      return
    }
    setBusy(true); setActionError(null)
    try {
      const assignment = await createAssignment({ incidentId, helpRequestId: null, rescueTeamId: teamId, vehicleId, requiredSkill: skill, requiredCapacity: capacity, notes })
      chooseAssignment(assignment)
      // Keep the completed assignment selected for safety review, while the
      // next new proposal receives its own stable generated incident reference.
      setIncidentId(createIncidentId())
      setTeamId('')
      setVehicleId('')
      setSkill('FirstAid')
      setCapacity(1)
      setNotes('')
      setView('safety')
      await refresh()
    } catch (error) { setActionError(errorMessage(error)) } finally { setBusy(false) }
  }

  async function reviseSelected() {
    const invalid = planError()
    if (!selected || invalid) {
      setActionError(invalid ?? 'Select a plan before revising.')
      return
    }
    setBusy(true); setActionError(null)
    try {
      const revised = await reviseAssignment(selected.id, { rescueTeamId: teamId, vehicleId, requiredSkill: skill, requiredCapacity: capacity, notes })
      chooseAssignment(revised)
      await refresh()
    } catch (error) { setActionError(errorMessage(error)) } finally { setBusy(false) }
  }

  async function runValidation() {
    if (!selected) return
    setBusy(true); setActionError(null)
    try { setValidation(await validateAssignment(selected.id)); setView('safety') }
    catch (error) { setActionError(errorMessage(error)) } finally { setBusy(false) }
  }

  async function decide(decision: 'APPROVE' | 'REVISE' | 'REJECT') {
    if (!selected || !validation?.workflowId) return
    if (decision === 'APPROVE' && !canApprove) {
      setActionError('Approval is available only for a current APPROVE recommendation on this plan version.')
      return
    }
    if (decision === 'APPROVE' && !window.confirm('The backend will perform a fresh live safety revalidation before dispatch. Continue?')) return
    setBusy(true); setActionError(null)
    try {
      await decideAssignment(selected.id, { workflowId: validation.workflowId, planVersion: selected.planVersion, decision, notes: null })
      if (decision !== 'APPROVE') setValidation(null)
      await refresh()
      setView(decision === 'APPROVE' ? 'dispatches' : 'assignments')
    } catch (error) { setActionError(errorMessage(error)) } finally { setBusy(false) }
  }

  async function progressDispatch(dispatch: DispatchDto) {
    const next = nextStatus(dispatch.status)
    if (!next) return
    setBusy(true); setActionError(null)
    try { await transitionDispatch(dispatch.id, next.status); await refresh() }
    catch (error) { setActionError(errorMessage(error)) } finally { setBusy(false) }
  }

  const operationalAssignments = useMemo(() => assignments.slice().sort((a, b) => b.assignedAt.localeCompare(a.assignedAt)), [assignments])

  return <main className="rescue-dashboard">
    <header className="rescue-dashboard__header">
      <div><p className="rescue-dashboard__brand">RescueSriLanka</p><h1>Rescue Coordination Center</h1><p>Manage rescue teams, validate response plans, approve deployments, and track active emergency missions.</p></div>
      <div className="rescue-dashboard__identity"><div><strong>{user.fullName}</strong><span>{user.email}</span></div><span className="role-badge">Emergency Coordinator</span><button type="button" className="btn-ghost" onClick={() => void refresh()} disabled={loading || busy}>{loading ? 'Refreshing…' : 'Refresh'}</button><button type="button" className="btn-ghost" onClick={onLogout}>Logout</button></div>
    </header>

    <nav className="rescue-dashboard__nav" aria-label="Rescue coordination sections">{views.map((item) => <button key={item.id} type="button" className={view === item.id ? 'is-active' : ''} onClick={() => { setActionError(null); setView(item.id) }}><strong>{item.label}</strong><span>{item.hint}</span></button>)}</nav>
    {actionError && view !== 'assignments' && <div className="alert" role="alert">{actionError}</div>}

    {view === 'overview' && <section className="rescue-content"><SummaryCards items={[
      ['Available rescue teams', availableTeams.length], ['Teams on mission', onMissionTeams.length], ['Available vehicles', availableVehicles.length], ['Proposed assignments', assignments.filter((item) => item.status === 'Proposed').length], ['Pending safety reviews', pendingAssignments.length], ['Active dispatches', activeDispatches.length],
    ]} />
      <div className="rescue-dashboard__grid"><OperationalList title="Recent assignments" error={errors.assignments} empty="No assignments have been proposed." items={operationalAssignments.slice(0, 6).map((item) => <button key={item.id} className="rescue-row" onClick={() => { chooseAssignment(item); setView('assignments') }}><strong>{item.id.slice(0, 8)}</strong><span>{item.rescueTeamName} · {item.vehiclePlateNumber}</span><small>{item.status} · plan v{item.planVersion}</small></button>)} />
      <OperationalList title="Active missions" error={errors.dispatches} empty="No active dispatches." items={activeDispatches.map((item) => <div key={item.id} className="rescue-row"><strong>{item.id.slice(0, 8)}</strong><span>Assignment {item.assignmentId.slice(0, 8)}</span><small>{item.status}</small></div>)} />
      <OperationalList title="Teams currently unavailable" error={errors.teams} empty="All teams are available." items={teams.filter((team) => team.status !== 'Available').map((team) => <div key={team.id} className="rescue-row"><strong>{team.name}</strong><span>{team.members.length} members · {team.vehicles.length} vehicles</span><small>{team.status}</small></div>)} /></div></section>}

    {view === 'teams' && <ResourceManagementPanel teams={teams} refresh={refresh} />}

    {view === 'assignments' && <section className="rescue-content"><PanelTitle title="Assignments" meta="Create and revise response plans before AI safety review" /><InlineError error={errors.assignments || errors.teams} />
      <div className="assignment-layout"><form className="assignment-form panel" onSubmit={(event) => { if (selected) { event.preventDefault(); void reviseSelected() } else void submitAssignment(event) }}><h2>{selected ? `Revise assignment · Plan v${selected.planVersion}` : 'Create assignment'}</h2>{actionError && <div className="alert" role="alert">{actionError}</div>}<div className="incident-reference"><span>Incident reference</span><code>{selected?.incidentId ?? incidentId}</code><small>{selected ? 'Preserved from the selected assignment.' : 'Automatically generated for this response plan.'}</small></div><label>Team<select value={teamId} onChange={(event) => { setActionError(null); setTeamId(event.target.value); setVehicleId('') }} required><option value="">Select team</option>{teams.map((team) => <option value={team.id} key={team.id}>{team.name} — {team.status}</option>)}</select></label><label>Vehicle<select value={vehicleId} onChange={(event) => { setActionError(null); setVehicleId(event.target.value) }} required><option value="">Select vehicle</option>{selectedVehicles.map((vehicle) => <option value={vehicle.id} key={vehicle.id}>{vehicle.plateNumber} — {vehicle.status}, capacity {vehicle.capacity}</option>)}</select></label><label>Required skill<select value={skill} onChange={(event) => { setActionError(null); setSkill(event.target.value as SkillType) }}>{skills.map((item) => <option key={item}>{item}</option>)}</select></label><label>Required capacity<input type="number" min="1" value={capacity} onChange={(event) => { setActionError(null); setCapacity(Number(event.target.value)) }} /></label><label>Notes<input value={notes} onChange={(event) => setNotes(event.target.value)} /></label><div className="assignment-form__actions"><button className="btn" disabled={busy}>{busy ? 'Saving…' : selected ? 'Save revision' : 'Create proposal'}</button>{selected && <button type="button" className="btn-ghost" disabled={busy} onClick={startNewAssignment}>Cancel editing / New assignment</button>}</div></form>
        <div className="panel assignment-list"><h2>Current assignments</h2>{assignments.length ? assignments.map((item) => <button key={item.id} className={`assignment-list__item${selected?.id === item.id ? ' is-selected' : ''}`} onClick={() => { chooseAssignment(item); setTeamId(item.rescueTeamId); setVehicleId(item.vehicleId ?? ''); setSkill(item.requiredSkill); setCapacity(item.requiredCapacity); setNotes(item.notes ?? ''); setIncidentId(item.incidentId ?? '') }}><strong>{item.id}</strong><span>{item.rescueTeamName} · {item.vehiclePlateNumber}</span><small>{item.requiredSkill} · capacity {item.requiredCapacity} · {item.status} · v{item.planVersion}</small></button>) : <Empty text="No assignments have been proposed." />}</div></div></section>}

    {view === 'safety' && <section className="rescue-content"><PanelTitle title="AI safety review" meta="AI recommendation only — human approval required." /><InlineError error={errors.assignments} />{!selected ? <Empty text="Select an assignment in the Assignments view to run a safety review." /> : <div className="safety-layout"><article className="panel safety-plan"><h2>Selected plan</h2><dl><dt>Assignment</dt><dd>{selected.id}</dd><dt>Plan version</dt><dd>{selected.planVersion}</dd><dt>Team</dt><dd>{selected.rescueTeamName}</dd><dt>Vehicle</dt><dd>{selected.vehiclePlateNumber}</dd><dt>Required skill</dt><dd>{selected.requiredSkill}</dd><dt>Required capacity</dt><dd>{selected.requiredCapacity}</dd></dl><button className="btn" disabled={busy} onClick={() => void runValidation()}>{busy ? 'Validating…' : 'Run AI Safety Validation'}</button></article><article className="panel safety-result">{!validation ? <Empty text="No safety review has been run for this selected plan." /> : <><h2>AI recommendation: <Status status={validation.decision} /></h2><p>{validation.summary}</p><p><strong>Workflow:</strong> {validation.workflowStatus} {validation.isStale ? '— STALE; validate again before deciding.' : ''}</p><h3>Validation checks</h3><ul>{validation.checks.map((check) => <li key={check.name}><strong>{check.passed ? 'PASS' : 'FAIL'}</strong><span>{check.name}: {check.reason}</span></li>)}</ul>{validation.failedChecks.length > 0 && <><h3>Failed checks</h3><ul>{validation.failedChecks.map((item) => <li key={item}><strong>FAIL</strong><span>{item}</span></li>)}</ul></>}{validation.suggestedActions.length > 0 && <><h3>Suggested actions</h3><ul>{validation.suggestedActions.map((item) => <li key={item}>{item}</li>)}</ul></>}<p className="safety-note">AI recommendation only — human approval required.</p><div className="decision-actions"><button className="btn" disabled={!canApprove} onClick={() => void decide('APPROVE')}>Approve & Dispatch</button><button className="btn-ghost" disabled={busy || !validation.workflowId} onClick={() => void decide('REVISE')}>Revise Plan</button><button className="btn-ghost" disabled={busy || !validation.workflowId} onClick={() => void decide('REJECT')}>Reject Plan</button></div></>}</article></div>}</section>}

    {view === 'dispatches' && <section className="rescue-content"><PanelTitle title="Active dispatches" meta="Only legal, next-state mission transitions are available" /><InlineError error={errors.dispatches} />{!loading && !errors.dispatches && dispatches.length === 0 && <Empty text="No dispatches have been created." />}<div className="dispatch-list">{dispatches.map((dispatch) => { const next = nextStatus(dispatch.status); return <article key={dispatch.id} className="dispatch-card"><div><h2>{dispatch.id}</h2><p>Assignment {dispatch.assignmentId}</p><Status status={dispatch.status} /></div><dl><dt>Approved</dt><dd>{dispatch.approvedAt ? new Date(dispatch.approvedAt).toLocaleString() : 'Not recorded'}</dd><dt>Dispatched</dt><dd>{dispatch.dispatchedAt ? new Date(dispatch.dispatchedAt).toLocaleString() : 'Not recorded'}</dd><dt>En route</dt><dd>{dispatch.enRouteAt ? new Date(dispatch.enRouteAt).toLocaleString() : 'Not recorded'}</dd><dt>On scene</dt><dd>{dispatch.onSceneAt ? new Date(dispatch.onSceneAt).toLocaleString() : 'Not recorded'}</dd></dl>{next && <button className="btn-ghost" disabled={busy} onClick={() => void progressDispatch(dispatch)}>{next.label}</button>}</article> })}</div></section>}
  </main>
}

function SummaryCards({ items }: { items: Array<[string, number]> }) { return <div className="summary-cards">{items.map(([label, value]) => <article key={label}><span>{label}</span><strong>{value}</strong></article>)}</div> }
function PanelTitle({ title, meta }: { title: string; meta: string }) { return <header className="content-title"><div><h2>{title}</h2><p>{meta}</p></div></header> }
function Empty({ text }: { text: string }) { return <p className="empty">{text}</p> }
function InlineError({ error }: { error?: string }) { return error ? <div className="alert" role="alert">{error}</div> : null }
function Status({ status }: { status: string }) { return <span className="status-chip">{status.replace(/([A-Z])/g, ' $1').trim()}</span> }
function OperationalList({ title, error, empty, items }: { title: string; error?: string; empty: string; items: React.ReactNode[] }) { return <section className="panel operational-list"><h2>{title}</h2><InlineError error={error} />{!error && (items.length ? items : <Empty text={empty} />)}</section> }
