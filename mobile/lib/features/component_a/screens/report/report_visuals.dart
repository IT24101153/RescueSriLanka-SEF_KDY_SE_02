import 'package:flutter/material.dart';

import '../../../../shared/core/theme.dart';

/// Shared by the report form and "My reports", so a disaster type or a
/// review status reads the same in both.

/// The disaster types the API accepts, in IncidentType order.
const List<String> incidentTypes = [
  'Flood',
  'Landslide',
  'Fire',
  'Accident',
  'Storm',
  'Tsunami',
  'Other',
];

const Map<String, IconData> incidentTypeIcons = {
  'Flood': Icons.water_drop_outlined,
  'Landslide': Icons.terrain_outlined,
  'Fire': Icons.local_fire_department_outlined,
  'Accident': Icons.car_crash_outlined,
  'Storm': Icons.thunderstorm_outlined,
  'Tsunami': Icons.tsunami_outlined,
  'Other': Icons.report_problem_outlined,
};

IconData iconForType(String type) =>
    incidentTypeIcons[type] ?? Icons.report_problem_outlined;

/// The path a report takes once it is sent: a coordinator approves it as true
/// (or rejects it as false), and it is resolved when the situation is over.
/// Rejected leaves the path, so it is not a step on it.
const List<String> reportSteps = ['Reported', 'Verified', 'Resolved'];

/// Where [status] sits on [reportSteps]; -1 for a rejected report.
/// "InProgress" is an older status that means the report was approved.
int stepIndexFor(String status) =>
    status == 'InProgress' ? 1 : reportSteps.indexOf(status);

/// True once a coordinator has approved the report as true.
bool isApproved(String status) =>
    status == 'Verified' || status == 'InProgress' || status == 'Resolved';

/// Words a reporter understands — "Reported" is what they did, so the first
/// step reads "Sent".
String stepLabel(String status) => switch (status) {
  'Reported' => 'Sent',
  'Verified' || 'InProgress' => 'Approved',
  _ => status,
};

String statusLabel(String status) => switch (status) {
  'Reported' => 'Under review',
  'Verified' || 'InProgress' => 'Approved',
  _ => status,
};

String statusExplainer(String status) => switch (status) {
  'Reported' =>
    'A coordinator is checking whether it is true. The AI is already '
        'working out its condition.',
  'Verified' ||
  'InProgress' => 'A coordinator confirmed it is true. It is on the live map.',
  'Resolved' => 'Closed. The situation has been dealt with.',
  'Rejected' =>
    'A coordinator found this report was not true, so it is not on the map.',
  _ => '',
};

Color statusTone(String status) => switch (status) {
  'Verified' || 'InProgress' => AppColors.low,
  'Rejected' => AppColors.critical,
  _ => AppColors.body,
};

/// The condition is shown only once a coordinator has approved the report,
/// so a reporter never sees an unreviewed machine grade.
bool showsSeverity(String status) => isApproved(status);

String timeAgo(DateTime when) {
  final minutes = DateTime.now().difference(when).inMinutes;
  if (minutes < 1) return 'just now';
  if (minutes < 60) return '${minutes}m ago';
  final hours = (minutes / 60).round();
  if (hours < 24) return '${hours}h ago';
  return '${(hours / 24).round()}d ago';
}

/// The rounded icon tile a report's type is shown in.
class TypeBadge extends StatelessWidget {
  const TypeBadge(this.type, {super.key, this.size = 42});

  final String type;
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: AppColors.brand.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(size * 0.28),
      ),
      child: Icon(iconForType(type), size: size * 0.52, color: AppColors.brandInk),
    );
  }
}
