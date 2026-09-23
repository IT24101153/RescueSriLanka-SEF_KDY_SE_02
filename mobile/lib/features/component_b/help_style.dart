import 'package:flutter/material.dart';

import '../../shared/core/theme.dart';

/// How a help request's state and kind are shown, in one place so all five
/// Help screens agree.
///
/// Same colour rule as the rest of the app: green / amber / red carry status,
/// never decoration. Every colour here is paired with a label or an icon, so
/// colour is never the only cue.

/// Status codes are HelpRequestStatus: 0 Pending, 1 Assigned, 2 In Progress,
/// 3 Resolved, 4 Cancelled.
Color helpStatusTone(int status) => switch (status) {
  3 => AppColors.safe,
  4 => AppColors.body,
  1 || 2 => AppColors.ink,
  _ => AppColors.caution,
};

/// True while a request is still waiting on someone.
bool helpStatusIsOpen(int status) => status == 0 || status == 1 || status == 2;

/// Type codes are HelpRequestType: 0 Water, 1 Food, 2 Medical, 3 Rescue,
/// 4 Shelter, 5 Other. The icon is what tells the kinds apart — they are
/// categories, not severities, so they get no colour of their own.
IconData helpTypeIcon(int type) => switch (type) {
  0 => Icons.water_drop_outlined,
  1 => Icons.restaurant_outlined,
  2 => Icons.medical_services_outlined,
  3 => Icons.emergency_outlined,
  4 => Icons.home_outlined,
  _ => Icons.help_outline,
};

/// The AI's suggested priority, which is guidance and says so in its label.
Color helpPriorityTone(String priority) => switch (priority.toLowerCase()) {
  'high' => AppColors.critical,
  'medium' => AppColors.caution,
  'low' => AppColors.safe,
  _ => AppColors.body,
};

/// Travel-advisory levels: 0 Safe, 1 Caution, 2 Danger.
Color safetyLevelTone(int level) => switch (level) {
  2 => AppColors.danger,
  1 => AppColors.caution,
  _ => AppColors.safe,
};

String safetyLevelLabel(int level) => switch (level) {
  2 => 'Danger',
  1 => 'Caution',
  _ => 'Safe',
};

IconData safetyLevelIcon(int level) => switch (level) {
  2 => Icons.crisis_alert,
  1 => Icons.warning_amber_rounded,
  _ => Icons.check_circle_outline,
};
