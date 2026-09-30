import { useEffect, useId, useRef, useState } from 'react'
import TeamLocationsMap from './TeamLocationsMap'
import TeamLocationPicker from './TeamLocationPicker'
import { hasBaseLocation, readBaseLocation } from './baseLocation'
import type { TeamMapFocus } from './TeamLocationsMap'
import type { FormEvent } from 'react'
import { addTeamMember, addVehicle, createRescueTeam, deleteRescueTeam, removeTeamMember, removeVehicle, updateRescueTeam, updateTeamMember, updateVehicle } from './api'
import type { CreateTeamMemberRequest, CreateVehicleRequest, RescueTeamDto, SkillType, TeamStatus, VehicleDto, VehicleType } from './types'

const skills: SkillType[] = ['WaterRescue', 'FirstAid', 'Paramedic', 'StructuralCollapse', 'FireResponse', 'Logistics', 'Driving']
const vehicleTypes: VehicleType[] = ['Ambulance', 'Boat', 'FireTruck', 'FourByFour', 'Truck']

type FeedbackType = 'success' | 'error' | 'destructive'
const TEAM_PAGE_SIZE = 5

function BaseLocationFields({ team }: { team?: RescueTeamDto }) {
  const hintId = useId()
  const [latitude, setLatitude] = useState(String(team?.baseLatitude ?? ''))
  const [longitude, setLongitude] = useState(String(team?.baseLongitude ?? ''))
  const [picking, setPicking] = useState(false)
  const pickButton = useRef<HTMLButtonElement>(null)
  const pickerId = useId()
  const closePicker = () => { setPicking(false); pickButton.current?.focus() }
  return <fieldset className="team-base-fields"><legend>Base location (optional)</legend>
    <p id={hintId}>Optional. Used to show the team’s registered base on the map. Supply both coordinates or leave both blank.</p>
    <label>Base latitude<input name="baseLatitude" inputMode="decimal" aria-describedby={hintId} value={latitude} onChange={(event) => setLatitude(event.target.value)} placeholder="−90 to 90" /></label>
    <label>Base longitude<input name="baseLongitude" inputMode="decimal" aria-describedby={hintId} value={longitude} onChange={(event) => setLongitude(event.target.value)} placeholder="−180 to 180" /></label>
    <div className="team-base-fields__actions"><button ref={pickButton} type="button" className="btn-ghost" aria-expanded={picking} aria-controls={pickerId} onClick={() => setPicking(!picking)}>Pick on map</button>
      <button type="button" className="btn-ghost" onClick={() => { setLatitude(''); setLongitude(''); setPicking(false) }}>Clear location</button></div>
    <div id={pickerId} className="team-base-fields__picker" hidden={!picking}>{picking && <TeamLocationPicker latitude={latitude.trim() ? Number(latitude) : null} longitude={longitude.trim() ? Number(longitude) : null}
      onCancel={closePicker} onConfirm={(lat, lng) => { setLatitude(String(lat)); setLongitude(String(lng)); closePicker() }} />}</div>
  </fieldset>
}

function MemberPhoneField({ defaultValue = '' }: { defaultValue?: string }) {
  const errorId = useId()
  const [invalid, setInvalid] = useState(false)

  return <div className="rescue-member-phone"><label>Phone number
    <input name="phone" type="tel" inputMode="numeric" aria-label="Phone" placeholder="Phone" defaultValue={defaultValue} required pattern="[0-9]{10}"
      aria-invalid={invalid} aria-describedby={invalid ? errorId : undefined}
      onInvalid={(event) => { event.preventDefault(); setInvalid(true) }}
      onChange={(event) => { if (/^[0-9]{10}$/.test(event.currentTarget.value)) setInvalid(false) }} /></label>
    {invalid && <span id={errorId} className="rescue-member-phone__error" role="alert">Invalid phone number, Please check</span>}
  </div>
}

export default function ResourceManagementPanel({ teams, refresh }: { teams: RescueTeamDto[]; refresh: () => Promise<void> }) {
  const [newTeam, setNewTeam] = useState('')
  const [createLocationVersion, setCreateLocationVersion] = useState(0)
  const [notice, setNotice] = useState<{ message: string; type: FeedbackType } | null>(null)
  const [busy, setBusy] = useState(false)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<TeamStatus | ''>('')
  const [sort, setSort] = useState<'asc' | 'desc'>('asc')
  const [page, setPage] = useState(1)
  const [showMap, setShowMap] = useState(false)
  const [mapFocus, setMapFocus] = useState<TeamMapFocus>(null)
  const mapSection = useRef<HTMLElement>(null)
  const mapId = useId()
  useEffect(() => {
    if (showMap && mapFocus) {
      mapSection.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
      mapSection.current?.focus({ preventScroll: true })
    }
  }, [showMap, mapFocus])
  const query = search.trim().toLowerCase()
  const matchingTeams = teams
    .filter((team) => team.name.toLowerCase().includes(query)
      || team.members.some((member) => member.fullName.toLowerCase().includes(query))
      || team.vehicles.some((vehicle) => vehicle.plateNumber.toLowerCase().includes(query)))
    .filter((team) => !status || team.status === status)
    .sort((a, b) => {
      const byName = a.name.toLowerCase().localeCompare(b.name.toLowerCase())
      return (sort === 'asc' ? byName : -byName) || a.id.localeCompare(b.id)
    })
  const pageCount = Math.max(1, Math.ceil(matchingTeams.length / TEAM_PAGE_SIZE))
  const currentPage = Math.min(page, pageCount)
  // Persist the clamp so a later refresh cannot jump back to an old page.
  if (page !== currentPage) setPage(currentPage)
  const start = (currentPage - 1) * TEAM_PAGE_SIZE
  const visibleTeams = matchingTeams.slice(start, start + TEAM_PAGE_SIZE)
  const run = async (work: () => Promise<unknown>, success: string, type: Exclude<FeedbackType, 'error'> = 'success') => { setBusy(true); setNotice(null); try { await work(); await refresh(); setNotice({ message: success, type }); return true } catch (error) { setNotice({ message: error instanceof Error ? error.message : 'Unable to change this resource.', type: 'error' }); return false } finally { setBusy(false) } }
  const createTeam = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!newTeam.trim()) return
    const form = event.currentTarget
    try {
      const location = readBaseLocation(form)
      void run(() => createRescueTeam({ name: newTeam.trim(), ...location }), 'Rescue team created.').then((success) => { if (success) { setNewTeam(''); form.reset(); setCreateLocationVersion((version) => version + 1) } })
    } catch (error) { setNotice({ type: 'error', message: (error as Error).message }) }
  }
  const editTeam = (event: FormEvent<HTMLFormElement>, team: RescueTeamDto) => {
    event.preventDefault()
    try {
      const location = readBaseLocation(event.currentTarget)
      const data = new FormData(event.currentTarget)
      void run(() => updateRescueTeam(team.id, { ...team, name: String(data.get('name')), status: String(data.get('status')) as TeamStatus, ...location }), 'Team updated.')
    } catch (error) { setNotice({ type: 'error', message: (error as Error).message }) }
  }
  return <section className="rescue-content"><header className="content-title resource-title"><div><h2>Rescue teams</h2><p>Manage operational teams, team members, and team vehicles.</p></div></header>
    {notice && <div className={notice.type === 'success' ? 'alert rescue-feedback--success' : 'alert'} role="alert">{notice.message}</div>}
    <form className="resource-add panel" onSubmit={createTeam}><h3>Add Rescue Team</h3><input aria-label="Team name" value={newTeam} onChange={(e) => setNewTeam(e.target.value)} placeholder="Team name" maxLength={150} required /><button className="btn" disabled={busy}>Create Team</button><BaseLocationFields key={createLocationVersion} /></form>
    {!teams.length && <p className="empty">No rescue teams yet. Add your first team.</p>}
    <div className="rescue-team-controls">
      <label>Search teams, members or vehicles<input type="search" placeholder="Search teams, members or vehicles" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
      <label>Team Status<select value={status} onChange={(event) => { setStatus(event.target.value as TeamStatus | ''); setPage(1) }}><option value="">All statuses</option><option value="Available">Available</option><option value="OnMission">OnMission</option><option value="OffDuty">OffDuty</option></select></label>
      <label>Sort<select value={sort} onChange={(event) => { setSort(event.target.value as 'asc' | 'desc'); setPage(1) }}><option value="asc">Team name A-Z</option><option value="desc">Team name Z-A</option></select></label>
    </div>
    <section className="team-map-section" ref={mapSection} tabIndex={-1} aria-labelledby={`${mapId}-title`}>
      <header><div><h3 id={`${mapId}-title`}>Team base locations</h3><p>Registered team base locations — not live tracking.</p></div>
        <button type="button" className="btn-ghost" aria-expanded={showMap} aria-controls={mapId} onClick={() => { setShowMap(!showMap); setMapFocus(null) }}>{showMap ? 'Hide map' : 'Show map'}</button></header>
      <div id={mapId} hidden={!showMap}>{showMap && <TeamLocationsMap teams={matchingTeams} focus={mapFocus} />}</div>
    </section>
    {teams.length > 0 && matchingTeams.length === 0 && <p className="empty">No rescue teams match your search or filter.</p>}
    <div className="team-grid">{visibleTeams.map((team) => <article className="team-card" key={team.id}><header><div><h2>{team.name}</h2><span className="status-chip" data-status={team.status}>{team.status}</span></div><span>{team.members.length} members · {team.vehicles.length} vehicles</span></header>
      <div className="team-card__location">{hasBaseLocation(team) ? <button type="button" className="btn-ghost" aria-label={`View ${team.name} on map`} onClick={() => { setShowMap(true); setMapFocus((previous) => ({ id: team.id, request: (previous?.request ?? 0) + 1 })) }}>View on map</button> : <span>No base location set</span>}</div>
      <details><summary>Edit Team</summary><form className="resource-form" onSubmit={(event) => editTeam(event, team)}><label>Team name<input name="name" defaultValue={team.name} required maxLength={150}/></label><label>Team status<select name="status" defaultValue={team.status}><option>Available</option><option>OnMission</option><option>OffDuty</option></select></label><BaseLocationFields key={`${team.baseLatitude}:${team.baseLongitude}`} team={team} /><button className="btn-ghost" disabled={busy}>Save</button></form></details>
      <section><h3>Team Members</h3>{team.members.map((member) => <details key={member.id} className="resource-item"><summary><strong>{member.fullName}</strong> · <span className="status-chip">{member.skill}</span> · <span className="status-chip" data-status={member.isAvailable ? 'Available' : 'Unavailable'}>{member.isAvailable ? 'Available' : 'Unavailable'}</span></summary><form className="resource-form" onSubmit={(e) => { e.preventDefault(); const f=new FormData(e.currentTarget); void run(() => updateTeamMember(team.id, member.id, { fullName:String(f.get('fullName')), phone:String(f.get('phone')), skill:String(f.get('skill')) as SkillType, isAvailable:f.get('isAvailable') === 'on' }), 'Team member updated.') }}><label>Full name<input name="fullName" defaultValue={member.fullName} required/></label><MemberPhoneField defaultValue={member.phone}/><label>Skill<select name="skill" defaultValue={member.skill}>{skills.map((skill)=><option key={skill}>{skill}</option>)}</select></label><label><input name="isAvailable" type="checkbox" defaultChecked={member.isAvailable}/> Available</label><button className="btn-ghost" disabled={busy}>Save</button><button type="button" className="btn-ghost rescue-destructive" disabled={busy} onClick={() => { if(confirm(`Remove ${member.fullName}?`)) void run(() => removeTeamMember(team.id, member.id), 'Team member removed.', 'destructive') }}>Remove</button></form></details>)}<details><summary>+ Add Team Member</summary><MemberForm busy={busy} onSubmit={(request) => run(() => addTeamMember(team.id, request), 'Team member added.')}/></details></section>
      <section><h3>Vehicles</h3><p>Register each physical vehicle separately. Each vehicle must have a unique registration number.</p>{team.vehicles.map((vehicle) => <VehicleEditor key={vehicle.id} vehicle={vehicle} teamId={team.id} busy={busy} refresh={refresh} onRemove={() => { if(confirm(`Remove ${vehicle.plateNumber}?`)) void run(() => removeVehicle(team.id, vehicle.id), 'Vehicle removed.', 'destructive') }} />)}<details><summary>+ Add Vehicle</summary><VehicleForm busy={busy} onSubmit={(request) => run(() => addVehicle(team.id, request), 'Vehicle added.')}/></details></section>
      <button type="button" className="btn-ghost rescue-destructive" disabled={busy} onClick={() => { if(confirm(`Delete ${team.name}? This is blocked when operational history exists.`)) void run(() => deleteRescueTeam(team.id), 'Rescue team deleted.', 'destructive') }}>Delete Team</button></article>)}</div>
    <nav className="rescue-team-pagination" aria-label="Rescue teams pagination">
      <span aria-live="polite">Showing {matchingTeams.length ? start + 1 : 0}-{Math.min(start + TEAM_PAGE_SIZE, matchingTeams.length)} of {matchingTeams.length} teams</span>
      <button type="button" className="btn-ghost" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>Previous</button>
      <span>Page {currentPage} of {pageCount}</span>
      <button type="button" className="btn-ghost" disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>Next</button>
    </nav>
  </section>
}

function MemberForm({ busy, onSubmit }: { busy:boolean; onSubmit:(request:CreateTeamMemberRequest)=>Promise<boolean> }) { return <form className="resource-form" onSubmit={(e)=>{e.preventDefault();const form=e.currentTarget;const f=new FormData(form);void onSubmit({fullName:String(f.get('fullName')),phone:String(f.get('phone')),skill:String(f.get('skill')) as SkillType}).then((success) => { if (success) form.reset() })}}><label>Full name<input name="fullName" placeholder="Full name" required/></label><MemberPhoneField/><label>Skill<select name="skill">{skills.map((skill)=><option key={skill}>{skill}</option>)}</select></label><button className="btn-ghost" disabled={busy}>Add Member</button></form> }
function VehicleForm({ busy, onSubmit }: { busy:boolean; onSubmit:(request:CreateVehicleRequest)=>Promise<boolean> }) { return <form className="resource-form" onSubmit={(e)=>{e.preventDefault();const form=e.currentTarget;const f=new FormData(form);void onSubmit({plateNumber:String(f.get('plateNumber')),type:String(f.get('type')) as VehicleType,capacity:Number(f.get('capacity'))}).then((success) => { if (success) form.reset() })}}><label>Registration number<input name="plateNumber" placeholder="Registration number" required/></label><label>Vehicle type<select name="type">{vehicleTypes.map((type)=><option key={type}>{type}</option>)}</select></label><label>People/Patients Capacity<input name="capacity" type="number" min="1" max="100" defaultValue="1" required/></label><button className="btn-ghost" disabled={busy}>Add Vehicle</button></form> }

function VehicleEditor({ vehicle, teamId, busy, refresh, onRemove }: {
  vehicle: VehicleDto; teamId: string; busy: boolean; refresh: () => Promise<void>; onRemove: () => void
}) {
  const [saved, setSaved] = useState(vehicle)
  const [source, setSource] = useState(vehicle)
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<{ message: string; type: 'success' | 'error' } | null>(null)
  if (source !== vehicle) { setSource(vehicle); setSaved(vehicle) }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const fields = new FormData(form)
    const capacity = Number(fields.get('capacity'))
    if (!Number.isInteger(capacity) || capacity < 1 || capacity > 100) {
      setFeedback({ message: 'Capacity must be a whole number between 1 and 100.', type: 'error' })
      return
    }
    if (!form.reportValidity()) return
    setSaving(true); setFeedback(null)
    try {
      const updated = await updateVehicle(teamId, vehicle.id, {
        plateNumber: String(fields.get('plateNumber')), type: String(fields.get('type')) as VehicleType,
        status: String(fields.get('status')) as VehicleDto['status'], capacity,
      })
      setSaved(updated)
      setFeedback({ message: 'Vehicle updated.', type: 'success' })
      try { await refresh() }
      catch (error) { setFeedback({ message: `Vehicle updated, but refresh failed: ${error instanceof Error ? error.message : 'Please retry.'}`, type: 'error' }) }
    } catch (error) {
      setFeedback({ message: error instanceof Error ? error.message : 'Unable to update this vehicle.', type: 'error' })
    } finally { setSaving(false) }
  }
  return <details className="resource-item">
    <summary><strong>{saved.plateNumber}</strong> · {saved.type} · carries {saved.capacity} {saved.capacity === 1 ? 'person' : 'people'} · <span className="status-chip" data-status={saved.status}>{saved.status}</span></summary>
    <form className="resource-form" onSubmit={(event) => void save(event)}>
      <label>Registration number<input name="plateNumber" defaultValue={vehicle.plateNumber} required/></label>
      <label>Vehicle type<select name="type" defaultValue={vehicle.type}>{vehicleTypes.map((type) => <option key={type}>{type}</option>)}</select></label>
      <label>Vehicle status<select name="status" defaultValue={vehicle.status}><option>Available</option><option>InUse</option><option>UnderMaintenance</option></select></label>
      <label>People/Patients Capacity<input name="capacity" type="number" min="1" max="100" step="1" defaultValue={vehicle.capacity} required
        onInvalid={() => setFeedback({ message: 'Capacity must be a whole number between 1 and 100.', type: 'error' })}/></label>
      <div className="vehicle-edit-actions"><button className="btn-ghost" disabled={busy || saving}>{saving ? 'Saving...' : 'Save'}</button>
      <button type="button" className="btn-ghost rescue-destructive" disabled={busy || saving} onClick={onRemove}>Remove</button></div>
    </form>
    {feedback && <div className={feedback.type === 'success' ? 'alert rescue-feedback--success' : 'alert'} role="alert">{feedback.message}</div>}
  </details>
}
