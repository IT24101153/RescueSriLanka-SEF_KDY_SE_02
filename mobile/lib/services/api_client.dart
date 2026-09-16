import 'dart:convert';
import 'package:http/http.dart' as http;
import '../core/config.dart';
import '../models/incident.dart';
import '../models/safety_zone.dart';

/// Thrown when the API cannot be reached or replies with an error.
class ApiException implements Exception {
  ApiException(this.message);
  final String message;
  @override
  String toString() => message;
}

/// Read-only client for the public disaster data. These endpoints are anonymous
/// by design — a tourist sees hazards without creating an account.
class ApiClient {
  ApiClient({http.Client? client}) : _client = client ?? http.Client();

  final http.Client _client;
  static const Duration _timeout = Duration(seconds: 15);

  Future<List<Incident>> fetchIncidents() async {
    final data = await _getJson('/api/incidents?activeOnly=true');
    return (data as List)
        .map((item) => Incident.fromJson(item as Map<String, dynamic>))
        .toList();
  }

  Future<List<Incident>> fetchNearby({
    required double latitude,
    required double longitude,
    double radiusKm = AppConfig.defaultRadiusKm,
  }) async {
    final data = await _getJson(
      '/api/incidents/nearby?lat=$latitude&lng=$longitude&radiusKm=$radiusKm',
    );
    return (data as List)
        .map((item) => Incident.fromJson(item as Map<String, dynamic>))
        .toList();
  }

  Future<List<SafetyZone>> fetchZones() async {
    final data = await _getJson('/api/safetyzones');
    return (data as List)
        .map((item) => SafetyZone.fromJson(item as Map<String, dynamic>))
        .toList();
  }

  Future<ZoneCheck> checkZone({
    required double latitude,
    required double longitude,
  }) async {
    final data = await _getJson('/api/safetyzones/check?lat=$latitude&lng=$longitude');
    return ZoneCheck.fromJson(data as Map<String, dynamic>);
  }

  Future<dynamic> _getJson(String path) async {
    final uri = Uri.parse('${AppConfig.apiBaseUrl}$path');

    http.Response response;
    try {
      response = await _client.get(uri).timeout(_timeout);
    } catch (_) {
      throw ApiException(
        'Cannot reach the server at ${AppConfig.apiBaseUrl}.\n'
        'Check that the API is running and the address is right for this device.',
      );
    }

    if (response.statusCode != 200) {
      throw ApiException('Server returned HTTP ${response.statusCode}.');
    }

    try {
      return jsonDecode(response.body);
    } on FormatException {
      throw ApiException('The server sent a response the app could not read.');
    }
  }

  void dispose() => _client.close();
}
