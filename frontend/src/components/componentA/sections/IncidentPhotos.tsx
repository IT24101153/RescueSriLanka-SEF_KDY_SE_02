import { useState } from 'react'
import { API_BASE } from '../../../shared/api/client'
import type { IncidentImage } from '../types'

type IncidentPhotosProps = {
  images: IncidentImage[]
}

/**
 * Photos are stored on Cloudinary, so the URL is already absolute. Anything
 * older that is still site-relative is resolved against the API host.
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
