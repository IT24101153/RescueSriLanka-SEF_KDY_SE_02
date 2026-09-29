import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../../../shared/core/theme.dart';
import '../../models/incident.dart';
import '../../widgets/severity_chip.dart';
import '../report/report_visuals.dart';

/// Detail sheet shown when a marker or list row is tapped.
class IncidentSheet extends StatelessWidget {
  const IncidentSheet({super.key, required this.incident});

  final Incident incident;

  static void show(BuildContext context, Incident incident) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
      ),
      builder: (_) => IncidentSheet(incident: incident),
    );
  }

  @override
  Widget build(BuildContext context) {
    final reported = DateFormat('d MMM, h:mm a').format(incident.reportedAt);
    final tone = AppColors.forSeverity(incident.severity);

    return DraggableScrollableSheet(
      initialChildSize: 0.6,
      minChildSize: 0.32,
      maxChildSize: 0.92,
      expand: false,
      builder: (context, controller) => ListView(
        controller: controller,
        padding: EdgeInsets.fromLTRB(
          20,
          12,
          20,
          28 + MediaQuery.paddingOf(context).bottom,
        ),
        children: [
          Center(
            child: Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: AppColors.border,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 18),

          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 52,
                height: 52,
                decoration: BoxDecoration(
                  color: tone.withValues(alpha: 0.13),
                  borderRadius: BorderRadius.circular(15),
                ),
                child: Icon(iconForType(incident.type), size: 28, color: tone),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: 6,
                      runSpacing: 6,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        SeverityChip(severity: incident.severity),
                        Text(
                          incident.type,
                          style: const TextStyle(
                            fontSize: 12.5,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Text(
                      incident.title,
                      style: const TextStyle(
                        fontSize: 19,
                        fontWeight: FontWeight.w700,
                        color: AppColors.ink,
                        height: 1.25,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 14),

          Wrap(
            spacing: 16,
            runSpacing: 6,
            children: [
              _meta(Icons.place_outlined, incident.district ?? 'Unknown district'),
              _meta(Icons.schedule, '$reported · ${timeAgo(incident.reportedAt)}'),
              if (incident.distanceKm != null)
                _meta(
                  Icons.near_me_outlined,
                  '${incident.distanceKm!.toStringAsFixed(1)} km away',
                ),
            ],
          ),
          const SizedBox(height: 18),

          Text(
            incident.description,
            style: const TextStyle(
              fontSize: 14.5,
              height: 1.55,
              color: AppColors.ink,
            ),
          ),
          const SizedBox(height: 20),

          _facts(),
          if (incident.isAnalysed) ...[
            const SizedBox(height: 16),
            _analysis(),
          ],
        ],
      ),
    );
  }

  Widget _meta(IconData icon, String text) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Icon(icon, size: 16, color: AppColors.body),
      const SizedBox(width: 5),
      Text(text, style: const TextStyle(fontSize: 12.5)),
    ],
  );

  /// The four figures as tiles, two to a row, so each is read at a glance.
  Widget _facts() {
    final people = incident.estimatedAffectedPeople;
    final tiles = [
      _tile(Icons.verified_outlined, 'Status', statusLabel(incident.status)),
      _tile(
        Icons.radar,
        'Affected radius',
        '${(incident.affectedRadiusMeters / 1000).toStringAsFixed(1)} km',
      ),
      _tile(
        Icons.groups_outlined,
        'People affected',
        people == null
            ? 'Not reported'
            : NumberFormat.decimalPattern().format(people),
      ),
      _tile(
        Icons.my_location,
        'Coordinates',
        '${incident.latitude.toStringAsFixed(4)}, '
            '${incident.longitude.toStringAsFixed(4)}',
      ),
    ];

    return Column(
      children: [
        Row(
          children: [
            Expanded(child: tiles[0]),
            const SizedBox(width: 10),
            Expanded(child: tiles[1]),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Expanded(child: tiles[2]),
            const SizedBox(width: 10),
            Expanded(child: tiles[3]),
          ],
        ),
      ],
    );
  }

  Widget _tile(IconData icon, String label, String value) => Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: AppColors.surfaceAlt,
      borderRadius: BorderRadius.circular(12),
    ),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(icon, size: 15, color: AppColors.body),
            const SizedBox(width: 5),
            Flexible(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 11.5),
              ),
            ),
          ],
        ),
        const SizedBox(height: 5),
        Text(
          value,
          style: const TextStyle(
            fontSize: 13.5,
            fontWeight: FontWeight.w600,
            color: AppColors.ink,
          ),
        ),
      ],
    ),
  );

  /// What the Incident Analysis Agent concluded. Shown read-only — approving or
  /// overriding is the coordinator's job in the React console.
  Widget _analysis() {
    final score = incident.aiSeverityScore ?? 0;

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        border: Border.all(color: AppColors.border),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.auto_awesome, size: 16, color: AppColors.brand),
              const SizedBox(width: 7),
              const Expanded(
                child: Text(
                  'Automated assessment',
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: AppColors.ink,
                  ),
                ),
              ),
              Text(
                '$score/100',
                style: const TextStyle(
                  fontSize: 13.5,
                  fontWeight: FontWeight.w700,
                  color: AppColors.ink,
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          ClipRRect(
            borderRadius: BorderRadius.circular(99),
            child: LinearProgressIndicator(
              value: (score / 100).clamp(0, 1).toDouble(),
              minHeight: 6,
              backgroundColor: AppColors.surfaceAlt,
              valueColor: AlwaysStoppedAnimation(
                AppColors.forSeverity(incident.aiSeverity ?? incident.severity),
              ),
            ),
          ),
          if (incident.aiRationale != null) ...[
            const SizedBox(height: 10),
            Text(
              incident.aiRationale!,
              style: const TextStyle(fontSize: 12.5, height: 1.45),
            ),
          ],
        ],
      ),
    );
  }
}
