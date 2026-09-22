import type {
  DashboardStatistics,
  Incident,
  SafetyZone,
} from '../types'
import StatRow from './StatRow'
import DistributionPanel from './DistributionPanel'
import ActivityFeed from './ActivityFeed'
import CoveragePanel from './CoveragePanel'
import ZonePanel from './ZonePanel'

type OverviewSectionProps = {
  stats: DashboardStatistics
  incidents: Incident[]
  zones: SafetyZone[]
  onSelect: (incident: Incident) => void
}

export default function OverviewSection({
  stats,
  incidents,
  zones,
  onSelect,
}: OverviewSectionProps) {
  return (
    <div className="section">
      <StatRow stats={stats} />

      <div className="grid grid--2">
        <ActivityFeed incidents={incidents} onSelect={onSelect} />
        <CoveragePanel stats={stats} />
      </div>

      <div className="grid grid--3">
        <DistributionPanel stats={stats} />
      </div>

      <ZonePanel zones={zones} />
    </div>
  )
}
