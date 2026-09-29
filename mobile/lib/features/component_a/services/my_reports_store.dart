import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import '../models/incident.dart';

/// A copy of this account's reports kept on the phone.
///
/// Not the record — the database is, through `GET /api/incidents/mine`. This
/// copy only lets "My reports" show something the instant it opens, and
/// still show the last known list when there is no signal.
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
