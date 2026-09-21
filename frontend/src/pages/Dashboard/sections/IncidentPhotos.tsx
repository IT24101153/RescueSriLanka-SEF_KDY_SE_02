import { useState } from 'react'
import { API_BASE } from '../../../api/client'
import type { IncidentImage } from '../../../types/incidents'

type IncidentPhotosProps = {
  images: IncidentImage[]
}

/**
 * Cloudinary hands back an absolute https URL; the local-disk fallback stores a
 * site-relative path served by the API. Both reach this component, so resolve
 * rather than assuming either one.
 */
function resolve(url: string): string {
  return /^https?:\/\//i.test(url) ? url : `${API_BASE}${url}`
}

/** Photos attached to the report — the evidence behind the AI's severity call. */
export default function IncidentPhotos({ images }: IncidentPhotosProps) {
  const [zoomed, setZoomed] = useState<IncidentImage | null>(null)

  if (images.length === 0) {
    return (
      <section className="photos">
        <h4 className="photos__title">Photos</h4>
        <p className="agent__pending">No photos were attached to this report.</p>
      </section>
    )
  }

  return (
    <section className="photos">
      <h4 className="photos__title">
        Photos <span className="photos__count">{images.length}</span>
      </h4>

      <ul className="photos__strip">
        {images.map((image) => (
          <li key={image.id}>
            <button
              type="button"
              className="photos__thumb"
              onClick={() => setZoomed(image)}
              aria-label={image.caption ?? `Open photo ${image.fileName ?? ''}`}
            >
              <img src={resolve(image.url)} alt={image.caption ?? ''} loading="lazy" />
            </button>
          </li>
        ))}
      </ul>

      {zoomed && (
        <div
          className="lightbox"
          role="dialog"
          aria-modal="true"
          aria-label="Photo"
          onClick={() => setZoomed(null)}
        >
          <img src={resolve(zoomed.url)} alt={zoomed.caption ?? ''} />
          {zoomed.caption && <p className="lightbox__caption">{zoomed.caption}</p>}
        </div>
      )}
    </section>
  )
}
