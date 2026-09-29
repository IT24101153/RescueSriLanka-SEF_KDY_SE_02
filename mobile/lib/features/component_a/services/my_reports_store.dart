import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../models/incident.dart';

/// The reports this person filed from this device, so the Report tab can list
/// them and follow each one through review.
///
/// Kept per account on the device: the list is what was sent from here, and
/// each entry is re-read by id on refresh for its live status.
class MyReportsStore {
  const MyReportsStore._();

  /// Enough history to be useful without the preferences file growing forever.
  static const int _limit = 50;

  static String _key(String userId) => 'rsl.myReports.$userId';

  static Future<List<Incident>> load(String userId) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final raw = prefs.getString(_key(userId));
      if (raw == null) return const [];
      return (jsonDecode(raw) as List)
          .map((item) => Incident.fromJson(item as Map<String, dynamic>))
          .toList();
    } catch (_) {
      // Unreadable history is not worth an error screen — start it afresh.
      return const [];
    }
  }

  static Future<void> save(String userId, List<Incident> reports) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(
        _key(userId),
        jsonEncode([for (final report in reports.take(_limit)) report.toJson()]),
      );
    } catch (_) {
      // The list on screen is still right; it just will not survive a restart.
    }
  }
}
