import 'package:flutter/material.dart';

/// Shared palette. Mirrors the React console exactly so the two clients read as
/// one product.
///
/// COLOUR RULE: amber is the brand accent; green / amber / orange / red are
/// RESERVED for severity and zone status and are never decorative.
class AppColors {
  const AppColors._();

  static const Color brand = Color(0xFFFAA71B);
  static const Color brandInk = Color(0xFF17120A);
  static const Color ink = Color(0xFF0B0E13);
  static const Color body = Color(0xFF5B6675);
  static const Color surface = Color(0xFFFFFFFF);
  static const Color surfaceAlt = Color(0xFFF5F7FA);
  static const Color border = Color(0xFFE4E8EE);

  // Severity scale — validated for colour-blind separation.
  static const Color low = Color(0xFF4C9F38);
  static const Color moderate = Color(0xFFD19A00);
  static const Color high = Color(0xFFE8590C);
  static const Color critical = Color(0xFF9C1C3D);

  static const Color safe = low;
  static const Color caution = moderate;
  static const Color danger = critical;

  static Color forSeverity(String severity) => switch (severity) {
        'Critical' => critical,
        'High' => high,
        'Moderate' => moderate,
        _ => low,
      };

  static Color forZone(String status) => switch (status) {
        'Danger' => danger,
        'Caution' => caution,
        _ => safe,
      };

  /// Marker size doubles as a severity cue, so colour is never the only signal.
  static double radiusForSeverity(String severity) => switch (severity) {
        'Critical' => 13,
        'High' => 11,
        'Moderate' => 9,
        _ => 7,
      };

  /// Shape is the third cue — readable in greyscale and for CVD users.
  static IconData iconForSeverity(String severity) => switch (severity) {
        'Critical' => Icons.crisis_alert,
        'High' => Icons.warning_amber_rounded,
        'Moderate' => Icons.error_outline,
        _ => Icons.info_outline,
      };
}

ThemeData buildAppTheme() {
  final base = ThemeData(
    useMaterial3: true,
    colorScheme: ColorScheme.fromSeed(
      seedColor: AppColors.brand,
      primary: AppColors.brand,
      onPrimary: AppColors.brandInk,
      surface: AppColors.surface,
    ),
    scaffoldBackgroundColor: AppColors.surface,
  );

  return base.copyWith(
    appBarTheme: const AppBarTheme(
      backgroundColor: AppColors.surface,
      foregroundColor: AppColors.ink,
      elevation: 0,
      scrolledUnderElevation: 0.5,
    ),
    textTheme: base.textTheme.apply(
      bodyColor: AppColors.body,
      displayColor: AppColors.ink,
    ),
    dividerTheme: const DividerThemeData(color: AppColors.border, space: 1),
  );
}
