import 'package:flutter/material.dart';
import '../core/theme.dart';

/// Severity is never colour alone — icon + label always travel with it, so it
/// stays readable for colour-blind users and in greyscale.
class SeverityChip extends StatelessWidget {
  const SeverityChip({super.key, required this.severity, this.compact = false});

  final String severity;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final color = AppColors.forSeverity(severity);

    return Container(
      padding: EdgeInsets.symmetric(
        horizontal: compact ? 7 : 9,
        vertical: compact ? 3 : 4,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.13),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(AppColors.iconForSeverity(severity),
              size: compact ? 12 : 14, color: color),
          SizedBox(width: compact ? 4 : 5),
          Text(
            severity,
            style: TextStyle(
              color: color,
              fontSize: compact ? 11 : 12,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
