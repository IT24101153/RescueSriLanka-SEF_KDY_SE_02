import { useEffect, useState } from 'react'
import { apiFetch } from '../../shared/api/client'

/**
 * The 25 districts, from the API — the same list warnings are matched on, so
 * a district picked here always reaches its subscribers.
 */
export function useDistricts(): string[] {
  const [districts, setDistricts] = useState<string[]>([])

  useEffect(() => {
    const controller = new AbortController()
    apiFetch<string[]>('/api/districts', { signal: controller.signal })
      .then((list) => {
        if (!controller.signal.aborted) setDistricts(list)
      })
      .catch(() => {
        // The form still works with a free-text district.
      })
    return () => controller.abort()
  }, [])

  return districts
}
