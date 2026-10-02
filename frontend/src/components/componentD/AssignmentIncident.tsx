import { useEffect, useState } from 'react'
import type { Incident } from '../componentA/types'
import { getIncident } from './api'

import { incidentLabel } from './incidentOptions'

export function LockedIncident({ id, incident }: { id: string | null; incident?: Incident }) {
  const [detail, setDetail] = useState<Incident | null>(null)
  useEffect(() => {
    if (!id || incident) return
    const controller = new AbortController()
    void getIncident(id, controller.signal).then((result) => {
      if (!controller.signal.aborted) setDetail(result)
    }).catch(() => { /* The preserved reference remains visible if details are unavailable. */ })
    return () => controller.abort()
  }, [id, incident])
  const displayed = incident ?? (detail?.id === id ? detail : null)
  return <div className="incident-reference">
    <span>Incident</span>
    <strong>{displayed ? incidentLabel(displayed) : id ? 'Existing incident' : 'Assignment references a help request'}</strong>
    {id && <code>{id}</code>}
    <small>Incident is preserved from the selected assignment.</small>
  </div>
}
