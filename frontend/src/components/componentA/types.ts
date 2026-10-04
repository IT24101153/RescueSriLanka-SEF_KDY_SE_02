export type IncidentSeverity = 'Low' | 'Moderate' | 'High' | 'Critical'
export type IncidentStatus =
  | 'Reported'
  | 'Verified'
  | 'InProgress'
  | 'Resolved'
  | 'Rejected'
  /** Folded into another report of the same event by the Enrichment Agent. */
  | 'Merged'
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
  /** Absolute Cloudinary URL. */
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
  /** Set once merged: the report this one was folded into. */
  duplicateOfIncidentId?: string | null
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
  expiresAt: string | null
  /** The Zone Planning Agent run whose approved plan drew it, if any. */
  sourceAgentRunId: string | null
}

/** A manual zone as the console sends it — declared, edited, or an edited agent draft. */
export type SafetyZoneInput = {
  name: string
  status: ZoneStatus
  centerLatitude: number
  centerLongitude: number
  radiusMeters: number
  district: string | null
  rationale: string | null
  expiresAt: string | null
}

/** Incident fields a coordinator may create or correct. */
export type IncidentInput = {
  title: string
  description: string
  type: IncidentType
  latitude: number
  longitude: number
  affectedRadiusMeters: number
  district: string | null
  addressText: string | null
  estimatedAffectedPeople: number | null
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

/** Field names the Incident Enrichment Agent may propose a change to. */
export type EnrichmentField =
  | 'title'
  | 'type'
  | 'district'
  | 'estimatedAffectedPeople'
  | 'addressText'

/** Structured output of the Incident Enrichment Agent. */
export type EnrichmentProposal = {
  suggestions: {
    field: EnrichmentField
    current: string | null
    proposed: string
    reason: string
  }[]
  duplicate: {
    incidentId: string
    title: string
    distanceKm: number
    similarity: number
    reason: string
  } | null
  summary: string
  usedFallback: boolean
}

/** One change in a Zone Planning Agent plan. */
export type ZoneProposal = {
  action: 'create' | 'retire'
  zoneId: string | null
  name: string
  status: ZoneStatus
  centerLatitude: number
  centerLongitude: number
  radiusMeters: number
  district: string | null
  expiresInHours: number | null
  rationale: string
  basedOnIncidentIds: string[]
}

/** Structured output of the Zone Planning Agent. */
export type ZonePlan = {
  zones: ZoneProposal[]
  summary: string
  usedFallback: boolean
}

/** Agent names as recorded on their runs. */
export const AGENTS = {
  analysis: 'IncidentAnalysisAgent',
  enrichment: 'IncidentEnrichmentAgent',
  zonePlanning: 'ZonePlanningAgent',
} as const
