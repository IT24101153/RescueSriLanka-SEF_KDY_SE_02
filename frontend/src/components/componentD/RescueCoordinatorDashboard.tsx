import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../../shared/api/client'
import {
  createAssignment,
  cancelAssignment,
  decideAssignment,
  getAssignments,
  getActiveIncidents,
  getDispatches,
  getRescueTeams,
  reviseAssignment,
  transitionDispatch,
  validateAssignment,
} from './api'
import type {
  AssignmentDto,
  DispatchDto,
  RescueTeamDto,
  SafetyValidationWorkflowResultDto,
  SkillType,
} from './types'
import type { Incident } from '../componentA/types'
import { LockedIncident } from './AssignmentIncident'
import { incidentLabel, isEligibleIncident } from './incidentOptions'
import type { User } from '../../shared/auth/session'
import './RescueCoordinatorDashboard.css'
import './RescueCoordinatorSafety.css'
import ResourceManagementPanel from './ResourceManagementPanel'
import AssignmentCancellation from './AssignmentCancellation'
import HelpRequestCoordinationPanel from './HelpRequestCoordinationPanel'

type View = 'overview' | 'teams' | 'assignments' | 'safety' | 'dispatches'
type LoadErrors = Partial<Record<'teams' | 'assignments' | 'dispatches', string>>

const skills: SkillType[] = ['WaterRescue', 'FirstAid', 'Paramedic', 'StructuralCollapse', 'FireResponse', 'Logistics', 'Driving']
const views: Array<{ id: View; label: string; hint: string }> = [
  { id: 'overview', label: 'Overview', hint: 'Operational picture' },
  { id: 'teams', label: 'Rescue teams', hint: 'People and vehicles' },
  { id: 'assignments', label: 'Assignments', hint: 'Plan response work' },
  { id: 'safety', label: 'AI safety review', hint: 'Rescue Coordinator decision required' },
  { id: 'dispatches', label: 'Dispatches', hint: 'Active missions and history' },
]

function errorMessage(error: unknown) {
  if (!(error instanceof ApiError)) return 'Network or server error. Please retry.'
  return ({
    400: error.message,
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

export default function RescueCoordinatorDashboard({ user }: { user: User; onLogout: () => void }) {
  const currentAssignmentsTitle = useRef<HTMLHeadingElement>(null)
  const cancellationPending = useRef(false)
  const [updateSuccess, setUpdateSuccess] = useState(false)
  const revisionForm = useRef<HTMLFormElement>(null)
  const [cancelTarget, setCancelTarget] = useState<AssignmentDto | null>(null)
  const [cancelError, setCancelError] = useState<string | null>(null)
  const [cancelSuccess, setCancelSuccess] = useState(false)
  const [responseMode, setResponseMode] = useState<'incident' | 'help'>('incident')
  const [refreshVersion, setRefreshVersion] = useState(0)
  const [dispatchView, setDispatchView] = useState<'active' | 'history'>('active')
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
  const [incidentId, setIncidentId] = useState('')
  const [incidents, setIncidents] = useState<Incident[]>([])
  const [incidentsLoading, setIncidentsLoading] = useState(true)
  const [incidentsError, setIncidentsError] = useState<string | null>(null)
  const eligibleSelection = incidents.some((incident) => incident.id === incidentId)
  const [skill, setSkill] = useState<SkillType>('FirstAid')
  const [capacity, setCapacity] = useState(1)
  const [notes, setNotes] = useState('')
  const [actionError, setActionError] = useState<string | null>(null)

  const selectedTeam = teams.find((team) => team.id === teamId)
  const selectedVehicles = selectedTeam?.vehicles ?? []
  const availableTeams = teams.filter((team) => team.status === 'Available')
  const onMissionTeams = teams.filter((team) => team.status === 'OnMission')
  const availableVehicles = teams.flatMap((team) => team.vehicles).filter((vehicle) => vehicle.status === 'Available')
  const terminalAssignmentIds = new Set(dispatches.filter(isTerminalDispatch).map((item) => item.assignmentId))
  const currentAssignments = assignments.filter((item) => item.status !== 'Cancelled' && !terminalAssignmentIds.has(item.id))
  const canRevise = (item: AssignmentDto) => ['Proposed', 'PendingApproval', 'Rejected'].includes(item.status)
    && !item.dispatchId && !dispatches.some((dispatch) => dispatch.assignmentId === item.id)
  const canCancel = (item: AssignmentDto) => ['Proposed', 'PendingApproval', 'Rejected', 'Approved'].includes(item.status)
    && !item.dispatchId && !dispatches.some((dispatch) => dispatch.assignmentId === item.id)
  const historyDispatches = dispatches.filter(isTerminalDispatch).sort((a, b) => (Date.parse(terminalTime(b)) || 0) - (Date.parse(terminalTime(a)) || 0) || a.id.localeCompare(b.id))
  const pendingAssignments = currentAssignments.filter((assignment) => assignment.status === 'Proposed' || assignment.status === 'PendingApproval')
  const activeDispatches = dispatches.filter((dispatch) => !isTerminalDispatch(dispatch))
  const visibleDispatches = dispatchView === 'active' ? activeDispatches : historyDispatches
  useEffect(() => {
    if (selected && (selected.status === 'Cancelled' || dispatches.some((item) => item.assignmentId === selected.id))) {
      setSelected(null); setValidation(null); setTeamId(''); setVehicleId(''); setNotes(''); setCapacity(1); setSkill('FirstAid')
    }
  }, [dispatches, selected])
  const canApprove = Boolean(selected && validation?.workflowId && validation.assignmentId === selected.id && validation.planVersion === selected.planVersion && validation.decision === 'APPROVE' && !validation.isStale && !busy)

  async function loadIncidents() {
    setIncidentsLoading(true)
    setIncidentsError(null)
    try { setIncidents((await getActiveIncidents()).filter(isEligibleIncident)) }
    catch (error) { setIncidentsError(errorMessage(error)) }
    finally { setIncidentsLoading(false) }
  }

  async function refresh() {
    setLoading(true)
    const results = await Promise.allSettled([getRescueTeams(), getAssignments(), getDispatches(), loadIncidents()])
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
    setRefreshVersion((value) => value + 1)
  }

  useEffect(() => { void refresh() }, [])

  function chooseAssignment(assignment: AssignmentDto) {
    setUpdateSuccess(false)
    setSelected(assignment)
    setTeamId(assignment.rescueTeamId); setVehicleId(assignment.vehicleId ?? ''); setSkill(assignment.requiredSkill); setCapacity(assignment.requiredCapacity); setNotes(assignment.notes ?? ''); setIncidentId(assignment.incidentId ?? '')
    setValidation(null)
    setActionError(null)
  }

  async function confirmCancellation() {
    if (!cancelTarget || busy || cancellationPending.current) return
    cancellationPending.current = true
    setBusy(true); setCancelError(null)
    try {
      const cancelled = await cancelAssignment(cancelTarget.id)
      setAssignments((items) => items.map((item) => item.id === cancelled.id ? cancelled : item))
      if (selected?.id === cancelled.id) startNewAssignment()
      setCancelTarget(null); setCancelSuccess(true)
      await refresh()
      currentAssignmentsTitle.current?.focus()
    } catch (error) { setCancelError(error instanceof ApiError ? error.message : errorMessage(error)) }
    finally { cancellationPending.current = false; setBusy(false) }
  }

  function startNewAssignment() {
    setUpdateSuccess(false)
    setSelected(null); setValidation(null); setActionError(null); setIncidentId(''); setTeamId(''); setVehicleId(''); setSkill('FirstAid'); setCapacity(1); setNotes('')
  }

  function planError() {
    if (!teamId || !vehicleId || capacity < 1) return 'Select a team, vehicle, and capacity of at least one.'
    if (!selectedTeam || selectedTeam.status !== 'Available') return 'Selected rescue team must be available.'
    const vehicle = selectedVehicles.find((item) => item.id === vehicleId)
    if (!vehicle || vehicle.status !== 'Available') return 'Selected vehicle must be available.'
    if (capacity > vehicle.capacity) return 'People/patients requiring transport exceeds the selected vehicle capacity.'
    if (!selectedTeam.members.some((member) => member.isAvailable && member.skill === skill)) return `Selected team has no available member with ${skill}.`
    return null
  }

  async function submitAssignment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const invalid = incidentsLoading || incidentsError || !eligibleSelection
      ? 'Select an available incident before creating an assignment.' : planError()
    if (invalid) {
      setActionError(invalid)
      return
    }
    setBusy(true); setActionError(null)
    try {
      const assignment = await createAssignment({ incidentId, helpRequestId: null, rescueTeamId: teamId, vehicleId, requiredSkill: skill, requiredCapacity: capacity, notes })
      chooseAssignment(assignment)
      setIncidentId('')
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
      setUpdateSuccess(true)
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

  const assignmentCards = <div className="panel assignment-list"><h2 ref={currentAssignmentsTitle} tabIndex={-1}>Current assignments</h2>
    {cancelSuccess && <p className="alert rescue-feedback--success" role="status">Assignment cancelled.</p>}
    {cancelTarget && <AssignmentCancellation returnFocus={currentAssignmentsTitle} busy={busy} error={cancelError} onKeep={() => { setCancelTarget(null); setCancelError(null) }} onConfirm={() => void confirmCancellation()} />}
    {currentAssignments.length ? currentAssignments.map((item) => <article key={item.id} className={`assignment-list__item${selected?.id === item.id ? ' is-selected' : ''}`}>
      <button className="assignment-summary" disabled={!canRevise(item) || busy} onClick={() => chooseAssignment(item)}>
        <strong className="assignment-team">{item.rescueTeamName} · {item.vehiclePlateNumber}</strong><span className="assignment-reference">{item.id}</span>
        <span className="assignment-card-chips"><Status status={item.status} /> <span className="status-chip">Plan v{item.planVersion}</span></span>
        <small>{item.requiredSkill} · people/patients requiring transport {item.requiredCapacity}</small>
      </button>
      <div className="assignment-card-actions">
        {canRevise(item) && <button type="button" className="btn-ghost" disabled={busy || loading} onClick={() => { chooseAssignment(item); requestAnimationFrame(() => { revisionForm.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' }); revisionForm.current?.focus() }) }}>Update assignment</button>}
        {canCancel(item) && <button type="button" className="btn-ghost rescue-destructive" disabled={busy || loading} onClick={() => { setCancelTarget(item); setCancelError(null); setCancelSuccess(false) }}>Cancel assignment</button>}
      </div>
    </article>) : <Empty text="No current assignments. Create a proposal to plan the next response." />}
  </div>

  const operationalAssignments = currentAssignments.slice().sort((a, b) => b.assignedAt.localeCompare(a.assignedAt))

  return <main className="rescue-dashboard">
    <header className="rescue-dashboard__header">
      <div><p className="rescue-dashboard__brand">RescueSriLanka</p><h1>Rescue Coordination Center</h1><p>Manage rescue teams, validate response plans, approve deployments, and track active rescue missions.</p></div>
      <div className="rescue-dashboard__identity"><div><strong>{user.fullName}</strong><span>{user.email}</span></div><span className="role-badge">Rescue Coordinator</span><button type="button" className="btn-ghost" onClick={() => void refresh()} disabled={loading || busy}>{loading ? 'Refreshing…' : 'Refresh'}</button></div>
    </header>

    <nav className="rescue-dashboard__nav" aria-label="Rescue coordination sections">{views.map((item) => <button key={item.id} type="button" aria-pressed={view === item.id} className={view === item.id ? 'is-active' : ''} onClick={() => { setActionError(null); setView(item.id) }}><strong>{item.label}</strong><span>{item.hint}</span></button>)}</nav>
    {loading && <p className="rescue-loading" role="status">Refreshing coordination data...</p>}
    {actionError && view !== 'assignments' && <div className="alert" role="alert">{actionError}</div>}

    {view === 'overview' && <section className="rescue-content"><SummaryCards items={[
      ['Available rescue teams', availableTeams.length], ['Teams on mission', onMissionTeams.length], ['Available vehicles', availableVehicles.length], ['Proposed assignments', assignments.filter((item) => item.status === 'Proposed').length], ['Pending safety reviews', pendingAssignments.length], ['Active dispatches', activeDispatches.length],
    ]} />
      <div className="rescue-dashboard__grid"><OperationalList title="Recent assignments" error={errors.assignments} empty="No current assignments. Create a proposal to plan the next response." items={operationalAssignments.slice(0, 6).map((item) => <button key={item.id} className="rescue-row" disabled={!canRevise(item)} onClick={() => { chooseAssignment(item); setView('assignments') }}><strong>{item.id.slice(0, 8)}</strong><span>{item.rescueTeamName} · {item.vehiclePlateNumber}</span><small>{item.status} · plan v{item.planVersion}</small></button>)} />
      <OperationalList title="Active missions" error={errors.dispatches} empty="No active dispatches." items={activeDispatches.map((item) => <div key={item.id} className="rescue-row"><strong>{item.id.slice(0, 8)}</strong><span>Assignment {item.assignmentId.slice(0, 8)}</span><small>{item.status}</small></div>)} />
      <OperationalList title="Teams currently unavailable" error={errors.teams} empty="All teams are available." items={teams.filter((team) => team.status !== 'Available').map((team) => <div key={team.id} className="rescue-row"><strong>{team.name}</strong><span>{team.members.length} members · {team.vehicles.length} vehicles</span><small>{team.status}</small></div>)} /></div></section>}

    {view === 'teams' && <><InlineError error={errors.teams} /><ResourceManagementPanel teams={teams} refresh={refresh} /></>}

    {view === 'assignments' && <section className="rescue-content"><PanelTitle title="Assignments" meta="Create and revise response plans before AI safety review" /><InlineError error={errors.assignments || errors.teams} />
      {!selected && <div className="response-objective-tabs" aria-label="Response objective"><button type="button" className="btn-ghost" disabled={busy} aria-pressed={responseMode === 'incident'} onClick={() => setResponseMode('incident')}>Incident assignments</button><button type="button" className="btn-ghost" disabled={busy} aria-pressed={responseMode === 'help'} onClick={() => setResponseMode('help')}>Help Request responses</button></div>}
      {!selected && responseMode === 'help' ? <><HelpRequestCoordinationPanel onCreating={setBusy} refreshVersion={refreshVersion} onCreated={async (assignment) => { chooseAssignment(assignment); setView('safety'); await refresh() }} />{assignmentCards}</> : <div className="assignment-layout"><form ref={revisionForm} tabIndex={-1} className="assignment-form panel" onSubmit={(event) => { if (selected) { event.preventDefault(); void reviseSelected() } else void submitAssignment(event) }}><h2>{selected ? `Revise assignment · Plan v${selected.planVersion}` : 'Create assignment'}</h2>{updateSuccess && <p className="alert rescue-feedback--success" role="status">Assignment updated. Fresh safety review required.</p>}{actionError && <div className="alert" role="alert">{actionError}</div>}{selected?.helpRequestId ? <div className="incident-reference"><span>Help Request</span><code>{selected.helpRequestId}</code><small>Help Request is preserved from the selected assignment.</small></div> : selected ? <LockedIncident id={selected.incidentId} incident={incidents.find((item) => item.id === selected.incidentId)} /> : <div>
          <label>Incident<select value={incidentId} onChange={(event) => { setIncidentId(event.target.value); setActionError(null) }} required disabled={incidentsLoading || Boolean(incidentsError) || incidents.length === 0}>
            <option value="">Select an incident</option>
            {incidentId && !eligibleSelection && <option value={incidentId} disabled>Previously selected incident is no longer available</option>}
            {incidents.map((incident) => <option key={incident.id} value={incident.id}>{incidentLabel(incident)}</option>)}
          </select></label>
          {incidentsLoading && <p role="status">Loading incidents...</p>}
          {incidentsError && <div className="alert" role="alert">Unable to load incidents. {incidentsError} <button type="button" className="btn-ghost" onClick={() => void loadIncidents()}>Retry</button></div>}
          {!incidentsLoading && !incidentsError && incidents.length === 0 && <p>No active incidents are currently available for assignment.</p>}
          {!incidentsLoading && !incidentsError && incidentId && !eligibleSelection && <p role="alert">The selected incident is no longer eligible. Select another active incident.</p>}
        </div>}<label>Team<select value={teamId} onChange={(event) => { setActionError(null); setTeamId(event.target.value); setVehicleId('') }} required><option value="">Select team</option>{teams.map((team) => <option value={team.id} key={team.id}>{team.name} — {team.status}</option>)}</select></label><label>Vehicle<select value={vehicleId} onChange={(event) => { setActionError(null); setVehicleId(event.target.value) }} required><option value="">Select vehicle</option>{selectedVehicles.map((vehicle) => <option value={vehicle.id} key={vehicle.id}>{vehicle.plateNumber} — {vehicle.status}, people/patient capacity {vehicle.capacity}</option>)}</select></label><label>Required skill<select value={skill} onChange={(event) => { setActionError(null); setSkill(event.target.value as SkillType) }}>{skills.map((item) => <option key={item}>{item}</option>)}</select></label><label>People/patients requiring transport<input type="number" min="1" value={capacity} onChange={(event) => { setActionError(null); setCapacity(Number(event.target.value)) }} /></label><label>Notes<input value={notes} onChange={(event) => setNotes(event.target.value)} /></label><div className="assignment-form__actions"><button className="btn" disabled={busy || (!selected && (incidentsLoading || Boolean(incidentsError) || !eligibleSelection))}>{busy ? 'Saving…' : selected ? 'Save revision' : 'Create proposal'}</button>{selected && <button type="button" className="btn-ghost" disabled={busy} onClick={startNewAssignment}>Cancel editing / New assignment</button>}</div></form>
        {assignmentCards}</div>}</section>}

    {view === 'safety' && <section className="rescue-content"><PanelTitle title="AI safety review" meta="AI recommendation only — Rescue Coordinator approval required." /><InlineError error={errors.assignments} />{!selected ? <Empty text="Select an assignment in the Assignments view to run a safety review." /> : <div className="safety-layout"><article className="panel safety-plan"><h2>Selected plan</h2><dl><dt>Assignment</dt><dd>{selected.id}</dd><dt>Plan version</dt><dd>{selected.planVersion}</dd><dt>Team</dt><dd>{selected.rescueTeamName}</dd><dt>Vehicle</dt><dd>{selected.vehiclePlateNumber}</dd><dt>Required skill</dt><dd>{selected.requiredSkill}</dd><dt>People/patients requiring transport</dt><dd>{selected.requiredCapacity}</dd></dl><button className="btn" disabled={busy} onClick={() => void runValidation()}>{busy ? 'Validating…' : 'Run AI Safety Validation'}</button></article><article className="panel safety-result">{!validation ? <Empty text="No safety review has been run for this selected plan." /> : <><p className="safety-note">AI recommends → Rescue Coordinator decides → backend revalidates</p><h2>AI recommendation: <Status status={validation.decision} /></h2><p>{validation.summary}</p><p><strong>Workflow:</strong> {validation.workflowStatus} {validation.isStale ? '— STALE; validate again before deciding.' : ''}</p><h3>Deterministic safety checks</h3><ul>{validation.checks.map((check) => <li key={check.name} data-result={check.passed ? 'pass' : 'fail'}><strong>{check.passed ? 'PASS' : 'FAIL'}</strong><span>{check.name}: {check.reason}</span></li>)}</ul>{validation.failedChecks.length > 0 && <><h3>Failed checks</h3><ul>{validation.failedChecks.map((item) => <li key={item}><strong>FAIL</strong><span>{item}</span></li>)}</ul></>}{validation.suggestedActions.length > 0 && <><h3>Suggested actions</h3><ul>{validation.suggestedActions.map((item) => <li key={item}>{item}</li>)}</ul></>}<p className="safety-note">AI recommendation only — Rescue Coordinator approval required.</p><div className="decision-actions"><button className="btn" disabled={!canApprove} onClick={() => void decide('APPROVE')}>Approve & Dispatch</button><button className="btn-ghost" disabled={busy || !validation.workflowId} onClick={() => void decide('REVISE')}>Revise Plan</button><button className="btn-ghost rescue-destructive" disabled={busy || !validation.workflowId} onClick={() => void decide('REJECT')}>Reject Plan</button></div></>}</article></div>}</section>}

    {view === 'dispatches' && <section className="rescue-content"><PanelTitle title={dispatchView === 'active' ? 'Active dispatches' : 'Dispatch history'} meta="Only legal, next-state mission transitions are available" /><label>Dispatch view<select value={dispatchView} onChange={(event) => setDispatchView(event.target.value as 'active' | 'history')}><option value="active">Active</option><option value="history">History</option></select></label><InlineError error={errors.dispatches} />{!loading && !errors.dispatches && visibleDispatches.length === 0 && <Empty text={dispatchView === 'active' ? 'No active dispatches.' : 'No completed dispatch history yet.'} />}<div className="dispatch-list">{visibleDispatches.map((dispatch) => { const next = nextStatus(dispatch.status); return <article key={dispatch.id} className="dispatch-card"><div><h2>Dispatch record</h2><Status status={dispatch.status} /><p className="dispatch-reference">Dispatch ID <code>{dispatch.id}</code></p><p className="dispatch-reference">Assignment ID <code>{dispatch.assignmentId}</code></p></div><dl><dt>Approved</dt><dd>{dispatch.approvedAt ? new Date(dispatch.approvedAt).toLocaleString() : 'Not recorded'}</dd><dt>Dispatched</dt><dd>{dispatch.dispatchedAt ? new Date(dispatch.dispatchedAt).toLocaleString() : 'Not recorded'}</dd><dt>En route</dt><dd>{dispatch.enRouteAt ? new Date(dispatch.enRouteAt).toLocaleString() : 'Not recorded'}</dd><dt>On scene</dt><dd>{dispatch.onSceneAt ? new Date(dispatch.onSceneAt).toLocaleString() : 'Not recorded'}</dd>{isTerminalDispatch(dispatch) && <><dt>{dispatch.status === 'Resolved' ? 'Resolved' : 'Cancelled'}</dt><dd>{terminalTime(dispatch) ? new Date(terminalTime(dispatch)).toLocaleString() : 'Not recorded'}</dd></>}</dl>{next && <button className="btn-ghost" disabled={busy} onClick={() => void progressDispatch(dispatch)}>{next.label}</button>}</article> })}</div></section>}
  </main>
}

function SummaryCards({ items }: { items: Array<[string, number]> }) { return <div className="summary-cards">{items.map(([label, value]) => <article key={label}><span>{label}</span><strong>{value}</strong></article>)}</div> }
function PanelTitle({ title, meta }: { title: string; meta: string }) { return <header className="content-title"><div><h2>{title}</h2><p>{meta}</p></div></header> }
function Empty({ text }: { text: string }) { return <p className="empty">{text}</p> }
function InlineError({ error }: { error?: string }) { return error ? <div className="alert" role="alert">{error}</div> : null }
function Status({ status }: { status: string }) { return <span className="status-chip" data-status={status}>{status.replace(/([A-Z])/g, ' $1').trim()}</span> }
function OperationalList({ title, error, empty, items }: { title: string; error?: string; empty: string; items: React.ReactNode[] }) { return <section className="panel operational-list"><h2>{title}</h2><InlineError error={error} />{!error && (items.length ? items : <Empty text={empty} />)}</section> }

function isTerminalDispatch(dispatch: DispatchDto) { return dispatch.status === 'Resolved' || dispatch.status === 'Cancelled' }
function terminalTime(dispatch: DispatchDto) { return (dispatch.status === 'Resolved' ? dispatch.resolvedAt : dispatch.cancelledAt) ?? dispatch.onSceneAt ?? dispatch.enRouteAt ?? dispatch.dispatchedAt ?? dispatch.approvedAt ?? '' }
