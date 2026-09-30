import type { RescueTeamDto } from './types'

export type LocatedTeam = RescueTeamDto & { baseLatitude: number; baseLongitude: number }

export function hasBaseLocation<T extends Pick<RescueTeamDto, 'baseLatitude' | 'baseLongitude'>>(team: T): team is T & { baseLatitude: number; baseLongitude: number } {
  return typeof team.baseLatitude === 'number' && Number.isFinite(team.baseLatitude)
    && typeof team.baseLongitude === 'number' && Number.isFinite(team.baseLongitude)
    && Math.abs(team.baseLatitude) <= 90 && Math.abs(team.baseLongitude) <= 180
}

export function readBaseLocation(form: HTMLFormElement) {
  const data = new FormData(form)
  const latitude = String(data.get('baseLatitude') ?? '').trim()
  const longitude = String(data.get('baseLongitude') ?? '').trim()
  if (!latitude && !longitude) return { baseLatitude: null, baseLongitude: null }
  if (!latitude || !longitude) throw new Error('Supply both base latitude and base longitude, or leave both blank.')
  const baseLatitude = Number(latitude), baseLongitude = Number(longitude)
  if (!Number.isFinite(baseLatitude) || Math.abs(baseLatitude) > 90) throw new Error('Base latitude must be a finite number between -90 and 90.')
  if (!Number.isFinite(baseLongitude) || Math.abs(baseLongitude) > 180) throw new Error('Base longitude must be a finite number between -180 and 180.')
  return { baseLatitude, baseLongitude }
}
