import 'package:flutter/material.dart';
import '../../../shared/core/theme.dart';

class MapLegend extends StatelessWidget {
  const MapLegend({super.key, required this.showZones});

  final bool showZones;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 9),
      decoration: BoxDecoration(
        color: AppColors.surface.withValues(alpha: 0.95),
        borderRadius: BorderRadius.circular(11),
        border: Border.all(color: AppColors.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          const Text(
            'SEVERITY',
            style: TextStyle(
              fontSize: 9.5,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.6,
              color: AppColors.body,
            ),
          ),
          const SizedBox(height: 6),
          for (final severity in ['Critical', 'High', 'Moderate', 'Low'])
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(AppColors.iconForSeverity(severity),
                      size: 13, color: AppColors.forSeverity(severity)),
                  const SizedBox(width: 6),
                  Text(severity,
                      style: const TextStyle(
                          fontSize: 11, color: AppColors.ink)),
                ],
              ),
            ),
          if (showZones) ...[
            const Divider(height: 11),
            const Text(
              'ZONES',
              style: TextStyle(
                fontSize: 9.5,
                fontWeight: FontWeight.w700,
                letterSpacing: 0.6,
                color: AppColors.body,
              ),
            ),
            const SizedBox(height: 6),
            for (final zone in ['Danger', 'Caution'])
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 13,
                      height: 13,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: AppColors.forZone(zone).withValues(alpha: 0.16),
                        border: Border.all(
                            color: AppColors.forZone(zone), width: 1.4),
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(zone,
                        style: const TextStyle(
                            fontSize: 11, color: AppColors.ink)),
                  ],
                ),
              ),
          ],
        ],
      ),
    );
  }
}
