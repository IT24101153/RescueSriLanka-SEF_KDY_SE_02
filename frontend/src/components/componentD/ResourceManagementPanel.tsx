import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { addTeamMember, addVehicle, createRescueTeam, deleteRescueTeam, removeTeamMember, removeVehicle, updateRescueTeam, updateTeamMember, updateVehicle } from './api'
import type { CreateTeamMemberRequest, CreateVehicleRequest, RescueTeamDto, SkillType, TeamStatus, VehicleType } from './types'

const skills: SkillType[] = ['WaterRescue', 'FirstAid', 'Paramedic', 'StructuralCollapse', 'FireResponse', 'Logistics', 'Driving']
const vehicleTypes: VehicleType[] = ['Ambulance', 'Boat', 'FireTruck', 'FourByFour', 'Truck']

type FeedbackType = 'success' | 'error' | 'destructive'
const TEAM_PAGE_SIZE = 5

function MemberPhoneField({ defaultValue = '' }: { defaultValue?: string }) {
  const errorId = useId()
  const [invalid, setInvalid] = useState(false)

  return <div className="rescue-member-phone">
    <input name="phone" type="tel" inputMode="numeric" aria-label="Phone" placeholder="Phone" defaultValue={defaultValue} required pattern="[0-9]{10}"
      aria-invalid={invalid} aria-describedby={invalid ? errorId : undefined}
      onInvalid={(event) => { event.preventDefault(); setInvalid(true) }}
      onChange={(event) => { if (/^[0-9]{10}$/.test(event.currentTarget.value)) setInvalid(false) }} />
    {invalid && <span id={errorId} className="rescue-member-phone__error" role="alert">Invalid phone number, Please check</span>}
  </div>
}

export default function ResourceManagementPanel({ teams, refresh }: { teams: RescueTeamDto[]; refresh: () => Promise<void> }) {
  const [newTeam, setNewTeam] = useState('')
  const [notice, setNotice] = useState<{ message: string; type: FeedbackType } | null>(null)
  const [busy, setBusy] = useState(false)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<TeamStatus | ''>('')
  const [sort, setSort] = useState<'asc' | 'desc'>('asc')
  const [page, setPage] = useState(1)
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
  const createTeam = (event: FormEvent) => { event.preventDefault(); if (!newTeam.trim()) return; void run(() => createRescueTeam({ name: newTeam.trim() }), 'Rescue team created.').then((success) => { if (success) setNewTeam('') }) }
  return <section className="rescue-content"><header className="content-title resource-title"><div><h2>Rescue teams</h2><p>Manage operational teams, team members, and team vehicles.</p></div></header>
    {notice && <div className={notice.type === 'success' ? 'alert rescue-feedback--success' : 'alert'} role="alert">{notice.message}</div>}
    <form className="resource-add panel" onSubmit={createTeam}><h3>Add Rescue Team</h3><input value={newTeam} onChange={(e) => setNewTeam(e.target.value)} placeholder="Team name" maxLength={150} required /><button className="btn" disabled={busy}>Create Team</button></form>
    {!teams.length && <p className="empty">No rescue teams yet. Add your first team.</p>}
    <div className="rescue-team-controls">
      <label>Search teams, members or vehicles<input type="search" placeholder="Search teams, members or vehicles" value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }} /></label>
      <label>Team Status<select value={status} onChange={(event) => { setStatus(event.target.value as TeamStatus | ''); setPage(1) }}><option value="">All statuses</option><option value="Available">Available</option><option value="OnMission">OnMission</option><option value="OffDuty">OffDuty</option></select></label>
      <label>Sort<select value={sort} onChange={(event) => { setSort(event.target.value as 'asc' | 'desc'); setPage(1) }}><option value="asc">Team name A-Z</option><option value="desc">Team name Z-A</option></select></label>
    </div>
    {teams.length > 0 && matchingTeams.length === 0 && <p className="empty">No rescue teams match your search or filter.</p>}
    <div className="team-grid">{visibleTeams.map((team) => <article className="team-card" key={team.id}><header><div><h2>{team.name}</h2><span className="status-chip">{team.status}</span></div><span>{team.members.length} members · {team.vehicles.length} vehicles</span></header>
      <details><summary>Edit Team</summary><form className="resource-form" onSubmit={(e) => { e.preventDefault(); const f=new FormData(e.currentTarget); void run(() => updateRescueTeam(team.id, { ...team, name: String(f.get('name')), status: String(f.get('status')) as RescueTeamDto['status'], baseLatitude: team.baseLatitude, baseLongitude: team.baseLongitude }), 'Team updated.') }}><input name="name" defaultValue={team.name} required maxLength={150}/><select name="status" defaultValue={team.status}><option>Available</option><option>OnMission</option><option>OffDuty</option></select><button className="btn-ghost" disabled={busy}>Save</button></form></details>
      <section><h3>Team Members</h3>{team.members.map((member) => <details key={member.id} className="resource-item"><summary><strong>{member.fullName}</strong> · {member.skill} · {member.isAvailable ? 'Available' : 'Unavailable'}</summary><form className="resource-form" onSubmit={(e) => { e.preventDefault(); const f=new FormData(e.currentTarget); void run(() => updateTeamMember(team.id, member.id, { fullName:String(f.get('fullName')), phone:String(f.get('phone')), skill:String(f.get('skill')) as SkillType, isAvailable:f.get('isAvailable') === 'on' }), 'Team member updated.') }}><input name="fullName" defaultValue={member.fullName} required/><MemberPhoneField defaultValue={member.phone}/><select name="skill" defaultValue={member.skill}>{skills.map((skill)=><option key={skill}>{skill}</option>)}</select><label><input name="isAvailable" type="checkbox" defaultChecked={member.isAvailable}/> Available</label><button className="btn-ghost" disabled={busy}>Save</button><button type="button" className="btn-ghost" disabled={busy} onClick={() => { if(confirm(`Remove ${member.fullName}?`)) void run(() => removeTeamMember(team.id, member.id), 'Team member removed.', 'destructive') }}>Remove</button></form></details>)}<details><summary>+ Add Team Member</summary><MemberForm busy={busy} onSubmit={(request) => run(() => addTeamMember(team.id, request), 'Team member added.')}/></details></section>
      <section><h3>Vehicles</h3>{team.vehicles.map((vehicle) => <details key={vehicle.id} className="resource-item"><summary><strong>{vehicle.plateNumber}</strong> · {vehicle.type} · capacity {vehicle.capacity} · {vehicle.status}</summary><form className="resource-form" onSubmit={(e) => { e.preventDefault(); const f=new FormData(e.currentTarget); void run(() => updateVehicle(team.id, vehicle.id, { plateNumber:String(f.get('plateNumber')), type:String(f.get('type')) as VehicleType, status:String(f.get('status')) as typeof vehicle.status, capacity:Number(f.get('capacity')) }), 'Vehicle updated.') }}><input name="plateNumber" defaultValue={vehicle.plateNumber} required/><select name="type" defaultValue={vehicle.type}>{vehicleTypes.map((type)=><option key={type}>{type}</option>)}</select><select name="status" defaultValue={vehicle.status}><option>Available</option><option>InUse</option><option>UnderMaintenance</option></select><input name="capacity" type="number" min="1" max="100" defaultValue={vehicle.capacity} required/><button className="btn-ghost" disabled={busy}>Save</button><button type="button" className="btn-ghost" disabled={busy} onClick={() => { if(confirm(`Remove ${vehicle.plateNumber}?`)) void run(() => removeVehicle(team.id, vehicle.id), 'Vehicle removed.', 'destructive') }}>Remove</button></form></details>)}<details><summary>+ Add Vehicle</summary><VehicleForm busy={busy} onSubmit={(request) => run(() => addVehicle(team.id, request), 'Vehicle added.')}/></details></section>
      <button type="button" className="btn-ghost" disabled={busy} onClick={() => { if(confirm(`Delete ${team.name}? This is blocked when operational history exists.`)) void run(() => deleteRescueTeam(team.id), 'Rescue team deleted.', 'destructive') }}>Delete Team</button></article>)}</div>
    <nav className="rescue-team-pagination" aria-label="Rescue teams pagination">
      <span aria-live="polite">Showing {matchingTeams.length ? start + 1 : 0}-{Math.min(start + TEAM_PAGE_SIZE, matchingTeams.length)} of {matchingTeams.length} teams</span>
      <button type="button" className="btn-ghost" disabled={currentPage === 1} onClick={() => setPage(currentPage - 1)}>Previous</button>
      <span>Page {currentPage} of {pageCount}</span>
      <button type="button" className="btn-ghost" disabled={currentPage === pageCount} onClick={() => setPage(currentPage + 1)}>Next</button>
    </nav>
  </section>
}

function MemberForm({ busy, onSubmit }: { busy:boolean; onSubmit:(request:CreateTeamMemberRequest)=>Promise<boolean> }) { return <form className="resource-form" onSubmit={(e)=>{e.preventDefault();const form=e.currentTarget;const f=new FormData(form);void onSubmit({fullName:String(f.get('fullName')),phone:String(f.get('phone')),skill:String(f.get('skill')) as SkillType}).then((success) => { if (success) form.reset() })}}><input name="fullName" placeholder="Full name" required/><MemberPhoneField/><select name="skill">{skills.map((skill)=><option key={skill}>{skill}</option>)}</select><button className="btn-ghost" disabled={busy}>Add Member</button></form> }
function VehicleForm({ busy, onSubmit }: { busy:boolean; onSubmit:(request:CreateVehicleRequest)=>Promise<boolean> }) { return <form className="resource-form" onSubmit={(e)=>{e.preventDefault();const form=e.currentTarget;const f=new FormData(form);void onSubmit({plateNumber:String(f.get('plateNumber')),type:String(f.get('type')) as VehicleType,capacity:Number(f.get('capacity'))}).then((success) => { if (success) form.reset() })}}><input name="plateNumber" placeholder="Registration number" required/><select name="type">{vehicleTypes.map((type)=><option key={type}>{type}</option>)}</select><input name="capacity" type="number" min="1" max="100" defaultValue="1" required/><button className="btn-ghost" disabled={busy}>Add Vehicle</button></form> }
