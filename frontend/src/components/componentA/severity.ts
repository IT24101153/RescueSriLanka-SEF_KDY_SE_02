import type { IncidentSeverity, ZoneStatus } from './types'

/**
 * Severity is never encoded by colour alone — every use pairs the colour with
 * this label, and the map pairs it with marker size too.
 */
export const SEVERITY_TOKEN: Record<IncidentSeverity, string> = {
  Low: 'safe',
  Moderate: 'caution',
  High: 'high',
  Critical: 'critical',
}

export const SEVERITY_HEX: Record<IncidentSeverity, string> = {
  Low: '#12946a',
  Moderate: '#e5a800',
  High: '#e35d0b',
  Critical: '#9c1c3d',
}

/** Marker radius doubles as a redundant encoding of severity on the map. */
export const SEVERITY_RADIUS: Record<IncidentSeverity, number> = {
  Low: 6,
  Moderate: 7.5,
  High: 9.5,
  Critical: 12,
}

export const ZONE_HEX: Record<ZoneStatus, string> = {
  Safe: '#12946a',
  Caution: '#e5a800',
  Danger: '#9c1c3d',
}

export const ZONE_TOKEN: Record<ZoneStatus, string> = {
  Safe: 'safe',
  Caution: 'caution',
  Danger: 'critical',
}

export const STATUS_LABEL: Record<string, string> = {
  Reported: 'Reported',
  Verified: 'Verified',
  InProgress: 'In progress',
  Resolved: 'Resolved',
  Rejected: 'Rejected',
}

export function timeAgo(iso: string): string {
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.round(hours / 24)}d ago`
}
