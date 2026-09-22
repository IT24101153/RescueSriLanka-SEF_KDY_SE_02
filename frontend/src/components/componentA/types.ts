export type IncidentSeverity = 'Low' | 'Moderate' | 'High' | 'Critical'
export type IncidentStatus =
  | 'Reported'
  | 'Verified'
  | 'InProgress'
  | 'Resolved'
  | 'Rejected'
export type IncidentType =
  | 'Flood'
  | 'Landslide'
  | 'Fire'
  | 'Accident'
  | 'Storm'
  | 'Tsunami'
  | 'Other'
export type ZoneStatus = 'Safe' | 'Caution' | 'Danger'

export const SEVERITY_ORDER: IncidentSeverity[] = [
  'Low',
  'Moderate',
  'High',
  'Critical',
]

export type IncidentImage = {
  id: string
  incidentId: string
  /** Absolute (Cloudinary) or API-relative (local disk) — resolve before use. */
  url: string
  fileName: string | null
  contentType: string | null
  sizeBytes: number
  caption: string | null
  uploadedAt: string
}

export type Incident = {
  id: string
  title: string
  description: string
  type: IncidentType
  severity: IncidentSeverity
  status: IncidentStatus
  latitude: number
  longitude: number
  affectedRadiusMeters: number
  district: string | null
  addressText: string | null
  estimatedAffectedPeople: number | null
  aiSeverity: IncidentSeverity | null
  aiSeverityScore: number | null
  aiConfidence: number | null
  aiRationale: string | null
  aiAnalysedAt: string | null
  severityOverridden: boolean
  isActive: boolean
  reportedAt: string
  resolvedAt: string | null
  imageCount: number
  images: IncidentImage[]
  distanceKm: number | null
}

export type SafetyZone = {
  id: string
  name: string
  status: ZoneStatus
  source: 'DerivedFromIncident' | 'ManualOverride'
  centerLatitude: number
  centerLongitude: number
  radiusMeters: number
  district: string | null
  rationale: string | null
  sourceIncidentId: string | null
  computedAt: string
}

export type DashboardStatistics = {
  activeIncidents: number
  criticalIncidents: number
  awaitingVerification: number
  reportedLast24Hours: number
  peopleAffected: number
  activeDangerZones: number
  awaitingAiAnalysis: number
  bySeverity: Record<string, number>
  byType: Record<string, number>
  byStatus: Record<string, number>
  byDistrict: Record<string, number>
}

/** Structured output produced by the Incident Analysis Agent. */
export type AnalysisProposal = {
  severity: IncidentSeverity
  severityScore: number
  confidence: number
  recommendedZoneStatus: ZoneStatus
  recommendedRadiusMeters: number
  rationale: string
}
