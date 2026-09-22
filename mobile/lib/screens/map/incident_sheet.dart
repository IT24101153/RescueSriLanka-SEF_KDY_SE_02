import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import '../../core/theme.dart';
import '../../models/incident.dart';
import '../../widgets/severity_chip.dart';

/// Detail sheet shown when a marker or list row is tapped.
class IncidentSheet extends StatelessWidget {
  const IncidentSheet({super.key, required this.incident});

  final Incident incident;

  static void show(BuildContext context, Incident incident) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => IncidentSheet(incident: incident),
    );
  }

  @override
  Widget build(BuildContext context) {
    final reported = DateFormat('d MMM, HH:mm').format(incident.reportedAt);

    return DraggableScrollableSheet(
      initialChildSize: 0.55,
      minChildSize: 0.32,
      maxChildSize: 0.92,
      expand: false,
      builder: (context, controller) => ListView(
        controller: controller,
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 32),
        children: [
          Center(
            child: Container(
              width: 38,
              height: 4,
              decoration: BoxDecoration(
                color: AppColors.border,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 18),
          Row(
            children: [
              SeverityChip(severity: incident.severity),
              const SizedBox(width: 8),
              Text(
                incident.type,
                style: const TextStyle(
                    fontSize: 12.5, fontWeight: FontWeight.w500),
              ),
              const Spacer(),
              if (incident.distanceKm != null)
                Text('${incident.distanceKm!.toStringAsFixed(1)} km away',
                    style: const TextStyle(fontSize: 12)),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            incident.title,
            style: const TextStyle(
              fontSize: 19,
              fontWeight: FontWeight.w600,
              color: AppColors.ink,
              height: 1.25,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            '${incident.district ?? 'Unknown district'} · $reported',
            style: const TextStyle(fontSize: 12.5),
          ),
          const SizedBox(height: 16),
          Text(incident.description,
              style: const TextStyle(fontSize: 14, height: 1.5)),
          const SizedBox(height: 20),
          _facts(),
          if (incident.isAnalysed) ...[
            const SizedBox(height: 20),
            _analysis(),
          ],
        ],
      ),
    );
  }

  Widget _facts() {
    final people = incident.estimatedAffectedPeople;
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surfaceAlt,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        children: [
          _row('Status', incident.status),
          _row('Affected radius',
              '${(incident.affectedRadiusMeters / 1000).toStringAsFixed(1)} km'),
          _row('People affected',
              people == null ? 'Not reported' : NumberFormat.decimalPattern().format(people)),
          _row('Coordinates',
              '${incident.latitude.toStringAsFixed(4)}, ${incident.longitude.toStringAsFixed(4)}'),
        ],
      ),
    );
  }

  Widget _row(String label, String value) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 5),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 132,
              child: Text(label, style: const TextStyle(fontSize: 12.5)),
            ),
            Expanded(
              child: Text(
                value,
                style: const TextStyle(
                    fontSize: 12.5,
                    color: AppColors.ink,
                    fontWeight: FontWeight.w500),
              ),
            ),
          ],
        ),
      );

  /// What the Incident Analysis Agent concluded. Shown read-only — approving or
  /// overriding is the coordinator's job in the React console.
  Widget _analysis() {
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
              const Icon(Icons.auto_awesome, size: 15, color: AppColors.brand),
              const SizedBox(width: 7),
              const Text(
                'Automated assessment',
                style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w600,
                    color: AppColors.ink),
              ),
              const Spacer(),
              Text('${incident.aiSeverityScore}/100',
                  style: const TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w700,
                      color: AppColors.ink)),
            ],
          ),
          if (incident.aiRationale != null) ...[
            const SizedBox(height: 9),
            Text(incident.aiRationale!,
                style: const TextStyle(fontSize: 12.5, height: 1.45)),
          ],
        ],
      ),
    );
  }
}
