import 'package:flutter/material.dart';

import '../../shared/core/theme.dart';

/// How coordination states are shown, in one place so the dashboard, teams,
/// assignments, dispatches and the safety review all agree.
///
/// Same rule as the rest of the app: a colour carries state, never decoration,
/// and the label always says the state too.
///
/// Statuses come from several places — team and vehicle status, assignment
/// status, dispatch status, and the AI validation's decision and per-check
/// result — so they are matched by name rather than by enum.
Color coordinationTone(String status) => switch (status) {
  'Available' ||
  'Approved' ||
  'APPROVE' ||
  'PASS' ||
  'Resolved' => AppColors.safe,
  'Rejected' || 'REJECT' || 'FAIL' => AppColors.critical,
  'Cancelled' || 'Unavailable' || 'Unknown' => AppColors.body,
  // Under way: committed, not yet finished.
  'OnMission' ||
  'InUse' ||
  'Dispatched' ||
  'EnRoute' ||
  'OnScene' => AppColors.ink,
  // Waiting on someone: Proposed, Pending, AwaitingApproval, and anything new.
  _ => AppColors.caution,
};

/// The icon beside a state, so colour is never the only cue.
IconData coordinationIcon(String status) => switch (status) {
  'Available' ||
  'Approved' ||
  'APPROVE' ||
  'PASS' ||
  'Resolved' => Icons.check_circle_outline,
  'Rejected' || 'REJECT' || 'FAIL' => Icons.cancel_outlined,
  'Cancelled' || 'Unavailable' => Icons.remove_circle_outline,
  'OnMission' ||
  'InUse' ||
  'Dispatched' ||
  'EnRoute' ||
  'OnScene' => Icons.local_shipping_outlined,
  _ => Icons.schedule,
};

/// 'OnMission' reads as 'On Mission'. The API's statuses are PascalCase.
String coordinationLabel(dynamic value) {
  if (value is! String || value.isEmpty) return 'Not provided';
  return value.replaceAllMapped(
    RegExp(r'([a-z])([A-Z])'),
    (match) => '${match[1]} ${match[2]}',
  );
}
