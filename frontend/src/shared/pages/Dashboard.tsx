import { useState } from 'react'
import ComponentADashboard from '../../components/componentA/ComponentADashboard'
import ComponentBDashboard from '../../components/componentB/ComponentBDashboard'
import './Dashboard.css'

type View = 'incidents' | 'help-requests'

const VIEWS: { id: View; label: string }[] = [
  { id: 'incidents', label: 'Incidents' },
  { id: 'help-requests', label: 'Help requests' },
]

/**
 * The console's home page. Layout only: each component owns its dashboard
 * (components/componentA, components/componentB) and edits it there.
 */
export default function Dashboard() {
  const [view, setView] = useState<View>('incidents')

  return (
    <div className="home">
      <div className="home__switch" role="group" aria-label="Dashboard view">
        {VIEWS.map((entry) => (
          <button
            key={entry.id}
            type="button"
            className={`home__option${view === entry.id ? ' is-active' : ''}`}
            aria-pressed={view === entry.id}
            onClick={() => setView(entry.id)}
          >
            {entry.label}
          </button>
        ))}
      </div>

      {view === 'incidents' ? <ComponentADashboard /> : <ComponentBDashboard />}
    </div>
  )
}
