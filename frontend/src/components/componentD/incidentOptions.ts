import type { Incident } from '../componentA/types'

// Component A closes incidents on Resolved/Rejected; verification is not a
// prerequisite for proposal creation in the existing Component D workflow.
export const isEligibleIncident = (incident: Incident) => incident.isActive
  && incident.status !== 'Resolved' && incident.status !== 'Rejected'

export const incidentLabel = (incident: Incident) => [
  incident.title,
  incident.district?.trim() || incident.addressText?.trim(),
  `${incident.severity} / ${incident.status}`,
].filter(Boolean).join(' — ')
