import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../../shared/api/client'
import { createAssignment, getRescueHelpRequests, recommendRescueTeam } from './api'
import type { AssignmentDto, RescueCandidate, RescueHelpRequest, RescueRecommendation, SkillType } from './types'
import HelpRequestResponseMap from './HelpRequestResponseMap'
import { hasBaseLocation } from './baseLocation'
import './HelpRequestCoordinationPanel.css'

const skills: SkillType[] = ['WaterRescue', 'FirstAid', 'Paramedic', 'StructuralCollapse', 'FireResponse', 'Logistics', 'Driving']
const candidateKey = (candidate: RescueCandidate) => `${candidate.teamId}/${candidate.vehicleId}`
const failure = (error: unknown) => error instanceof ApiError ? error.message : 'Unable to reach the service. Please retry.'

export default function HelpRequestCoordinationPanel({ refreshVersion, onCreated, onCreating }: { onCreating?: (creating: boolean) => void; refreshVersion: number; onCreated: (assignment: AssignmentDto) => Promise<void> }) {
  const [requests, setRequests] = useState<RescueHelpRequest[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [retry, setRetry] = useState(0)
  const [requestId, setRequestId] = useState('')
  const [skill, setSkill] = useState<SkillType | ''>('')
  const [capacity, setCapacity] = useState('')
  const [notes, setNotes] = useState('')
  const [result, setResult] = useState<RescueRecommendation | null>(null)
  const [selectedKey, setSelectedKey] = useState('')
  const [busy, setBusy] = useState<'recommend' | 'create' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const creating = useRef(false)
  const generation = useRef(0)
  const candidateSelect = useRef<HTMLSelectElement>(null)
  useEffect(() => {
    const controller = new AbortController()
    setLoading(true); setLoadError(null)
    generation.current++; setResult(null); setSelectedKey(''); setBusy((current) => current === 'create' ? current : null)
    void getRescueHelpRequests(controller.signal).then((items) => {
      if (controller.signal.aborted) return
      setRequests(items)
      setRequestId((current) => items.some((item) => item.id === current) ? current : '')
    }).catch((cause) => { if (!controller.signal.aborted) setLoadError(failure(cause)) })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => { controller.abort(); generation.current++ }
  }, [refreshVersion, retry])
  const request = requests.find((item) => item.id === requestId)
  const selected = result?.candidates.find((candidate) => candidateKey(candidate) === selectedKey)
  const demand = Number(capacity)
  const validDemand = capacity.trim() !== '' && Number.isSafeInteger(demand) && demand > 0 && demand <= 2147483647
  const validLocation = request && hasBaseLocation({ baseLatitude: request.latitude, baseLongitude: request.longitude })
  function invalidate() { generation.current++; setResult(null); setSelectedKey(''); setError(null); setBusy(null) }
  async function recommend() {
    if (!request || !skill || !validDemand || !validLocation) return
    const current = ++generation.current
    setBusy('recommend'); setError(null); setResult(null); setSelectedKey('')
    try { const next = await recommendRescueTeam(request.id, skill, demand); if (generation.current === current) setResult(next) }
    catch (cause) { if (generation.current === current) setError(failure(cause)) }
    finally { if (generation.current === current) setBusy(null) }
  }
  async function create() {
    if (!request || !selected || !skill || !validDemand || busy || creating.current || loading || loadError) return
    creating.current = true; onCreating?.(true); setBusy('create'); setError(null)
    try {
      const assignment = await createAssignment({ helpRequestId: request.id, incidentId: null,
        rescueTeamId: selected.teamId, vehicleId: selected.vehicleId, requiredSkill: skill, requiredCapacity: demand, notes })
      await onCreated(assignment)
    } catch (cause) { setError(failure(cause)); setResult(null); setSelectedKey('') }
    finally { creating.current = false; onCreating?.(false); setBusy(null) }
  }
  return <div className="help-coordination">
    <section className="panel"><h2>Open Help Requests</h2>
      <p>The Rescue Coordinator reviews Pending, Verified requests without current response work.</p>
      {loading && <p role="status">Loading Help Requests...</p>}
      {loadError && <div role="alert">{loadError} <button type="button" onClick={() => setRetry((value) => value + 1)}>Retry Help Requests</button></div>}
      {!loading && !loadError && requests.length === 0 && <p>No eligible Help Requests are currently available.</p>}
      {!loading && !loadError && requests.map((item) => <button type="button" className="help-request-card" key={item.id} aria-pressed={item.id === requestId} disabled={busy === 'create'}
        onClick={() => { invalidate(); setRequestId(item.id); setSkill(''); setCapacity(''); setNotes('') }}>
        <strong>{item.type}</strong><span>{item.description}</span><span>Urgency: {item.urgencyScore} · {item.verificationStatus} · {item.status}</span>
        <time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString()}</time>
        <span>{hasBaseLocation({ baseLatitude: item.latitude, baseLongitude: item.longitude }) ? 'Location available — select to view map' : 'Location unavailable'}</span>
      </button>)}
    </section>
    {request && !loading && !loadError && <section className="panel"><h2>Help Request response plan</h2>
      <p>{request.type}: {request.description}</p>
      <HelpRequestResponseMap request={request} candidates={result?.candidates ?? []} recommendedTeamId={result?.recommendedCandidate?.teamId} selectedTeamId={selected?.teamId} />
      <form className="assignment-form" onSubmit={(event) => { event.preventDefault(); void create() }}>
        <p>The Rescue Coordinator selects the required skill and confirms transport demand. Total beneficiaries are not assumed to require transport.</p>
        <label>Required skill<select required value={skill} disabled={busy === 'create'} onChange={(event) => { invalidate(); setSkill(event.target.value as SkillType | '') }}>
          <option value="">Select required skill</option>{skills.map((value) => <option key={value}>{value}</option>)}
        </select></label>
        <label>People/patients requiring transport<input required type="number" min="1" max="2147483647" step="1" value={capacity} disabled={busy === 'create'} onChange={(event) => { invalidate(); setCapacity(event.target.value) }} /></label>
        <button type="button" className="btn" disabled={Boolean(busy) || !skill || !validDemand || !validLocation} onClick={() => void recommend()}>{busy === 'recommend' ? 'Finding eligible teams…' : 'Recommend rescue team'}</button>
        {error && <p className="alert" role="alert">{error}</p>}
        {result && <section aria-label="Rescue team recommendation">
          <h3>{result.aiAvailable ? 'AI recommendation' : 'Deterministic recommendation'}</h3>
          <p>{result.explanation}</p>
          {result.recommendedCandidate && <><h4>Recommended rescue team: {result.recommendedCandidate.teamName}</h4><CandidateFacts candidate={result.recommendedCandidate} />
            <button type="button" className="btn-ghost" disabled={busy === 'create'} onClick={() => setSelectedKey(candidateKey(result.recommendedCandidate!))}>Use recommended team</button></>}
          {result.candidates.length > 0 && <>
            <button type="button" className="btn-ghost" disabled={busy === 'create'} onClick={() => candidateSelect.current?.focus()}>Choose another eligible team</button>
            <label>Eligible team and vehicle<select ref={candidateSelect} required value={selectedKey} disabled={busy === 'create'} onChange={(event) => setSelectedKey(event.target.value)}>
              <option value="">Select team and vehicle</option>{result.candidates.map((candidate) => <option key={candidateKey(candidate)} value={candidateKey(candidate)}>{candidate.teamName} — {candidate.vehicleType} ({candidate.vehicleId}) — {candidate.distanceKm.toFixed(2)} km — capacity {candidate.vehicleCapacity}</option>)}
            </select></label>
          </>}
        </section>}
        {selected && <section aria-label="Selected response resources"><h3>Selected team: {selected.teamName}</h3><CandidateFacts candidate={selected} /></section>}
        <label>Notes<input value={notes} maxLength={500} disabled={busy === 'create'} onChange={(event) => setNotes(event.target.value)} /></label>
        <p>AI recommends → Rescue Coordinator decides → backend revalidates → transactional dispatch.</p>
        <button type="submit" className="btn" disabled={Boolean(busy) || !selected || !validDemand || !skill}>{busy === 'create' ? 'Creating response plan…' : 'Create response plan'}</button>
      </form>
    </section>}
  </div>
}

function CandidateFacts({ candidate }: { candidate: RescueCandidate }) {
  return <dl><dt>Suitable vehicle</dt><dd>{candidate.vehicleType} · {candidate.vehicleId}</dd>
    <dt>Straight-line distance</dt><dd>{candidate.distanceKm.toFixed(2)} km</dd>
    <dt>Matching skill</dt><dd>{candidate.matchingSkill}</dd><dt>Vehicle carrying capacity</dt><dd>{candidate.vehicleCapacity}</dd>
    <dt>Team availability</dt><dd>{candidate.teamAvailability}</dd><dt>Vehicle availability</dt><dd>{candidate.vehicleAvailability}</dd></dl>
}
