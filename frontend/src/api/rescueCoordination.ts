import { apiFetch } from './client'
import type { AssignmentDto, CreateAssignmentRequest, ReviseAssignmentRequest, SafetyValidationWorkflowResultDto, CoordinatorDecisionRequest, CoordinatorDecisionResultDto, DispatchDto, DispatchStatus, MatchRequest, RescueTeamDto, TeamMatchResultDto } from '../types/rescueCoordination'

export const getRescueTeams = (signal?: AbortSignal) => apiFetch<RescueTeamDto[]>('/api/rescueteams', { signal })
export const getAssignments = (signal?: AbortSignal) => apiFetch<AssignmentDto[]>('/api/assignments', { signal })
export const getDispatches = (signal?: AbortSignal) => apiFetch<DispatchDto[]>('/api/dispatches', { signal })
export const matchTeams = (request: MatchRequest) => apiFetch<TeamMatchResultDto[]>('/api/assignments/match', { method: 'POST', body: JSON.stringify(request) })
export const createAssignment = (request: CreateAssignmentRequest) => apiFetch<AssignmentDto>('/api/assignments', { method: 'POST', body: JSON.stringify(request) })
export const reviseAssignment = (id: string, request: ReviseAssignmentRequest) => apiFetch<AssignmentDto>(`/api/assignments/${id}/revise`, { method: 'POST', body: JSON.stringify(request) })
export const validateAssignment = (id: string) => apiFetch<SafetyValidationWorkflowResultDto>(`/api/assignments/${id}/validate`, { method: 'POST' })
export const decideAssignment = (id: string, request: CoordinatorDecisionRequest) => apiFetch<CoordinatorDecisionResultDto>(`/api/assignments/${id}/decision`, { method: 'POST', body: JSON.stringify(request) })
export const transitionDispatch = (id: string, newStatus: DispatchStatus) => apiFetch<DispatchDto>(`/api/dispatches/${id}/status`, { method: 'PATCH', body: JSON.stringify({ newStatus }) })
