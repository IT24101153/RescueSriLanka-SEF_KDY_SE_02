import 'package:flutter/material.dart';
import '../../../shared/core/theme.dart';
import '../models/safety_zone.dart';

/// "You are in a CAUTION zone" — the single most useful thing a tourist sees.
class ZoneBanner extends StatelessWidget {
  const ZoneBanner({super.key, required this.check, required this.onDismiss});

  final ZoneCheck check;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) {
    final color = AppColors.forZone(check.status);
    final icon = switch (check.status) {
      'Danger' => Icons.dangerous,
      'Caution' => Icons.warning_amber_rounded,
      _ => Icons.verified_user_outlined,
    };

    return Material(
      color: Colors.transparent,
      child: Container(
        padding: const EdgeInsets.fromLTRB(14, 12, 8, 12),
        decoration: BoxDecoration(
          color: AppColors.surface,
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: color.withValues(alpha: 0.45)),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: 0.1),
              blurRadius: 18,
              offset: const Offset(0, 6),
            ),
          ],
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: color, size: 22),
            const SizedBox(width: 11),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'You are in a ${check.status.toUpperCase()} area',
                    style: TextStyle(
                      color: color,
                      fontSize: 13.5,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    check.message,
                    style: const TextStyle(
                        color: AppColors.body, fontSize: 12.5, height: 1.35),
                  ),
                ],
              ),
            ),
            IconButton(
              onPressed: onDismiss,
              icon: const Icon(Icons.close, size: 18),
              color: AppColors.body,
              visualDensity: VisualDensity.compact,
              tooltip: 'Dismiss',
            ),
          ],
        ),
      ),
    );
  }
}
