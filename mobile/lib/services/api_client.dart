import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

import '../core/config.dart';
import '../models/incident.dart';
import '../models/safety_zone.dart';
import 'auth_service.dart';

/// Thrown when the API cannot be reached or replies with an error.
class ApiException implements Exception {
  ApiException(this.message, {this.statusCode});
  final String message;
  final int? statusCode;

  bool get isUnauthorized => statusCode == 401;

  @override
  String toString() => message;
}

/// Client for the disaster data.
///
/// Reads are anonymous by design — a tourist sees hazards without creating an
/// account. Writes carry the bearer token from [AuthService]; the API rejects
/// them outright without one.
class ApiClient {
  ApiClient({http.Client? client, this.auth})
      : _client = client ?? http.Client();

  /// Read-only client for the anonymous map data, with no session attached.
  ApiClient.anonymous() : this();

  final http.Client _client;

  /// Null for the anonymous read-only client; set for anything that writes.
  final AuthService? auth;

  static const Duration _timeout = Duration(seconds: 15);

  /// Photo upload waits on the network twice over — the phone's connection and
  /// the API's own upload to Cloudinary — so it gets a longer leash.
  static const Duration _uploadTimeout = Duration(seconds: 45);

  // ------------------------------------------------------------------ reads

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

  /// The 25 districts, for the notification settings picker. Anonymous, and
  /// server-side so the app cannot drift from the list warnings are matched on.
  Future<List<String>> fetchDistricts() async {
    final data = await _getJson('/api/districts');
    return (data as List).map((item) => item as String).toList();
  }

  Future<ZoneCheck> checkZone({
    required double latitude,
    required double longitude,
  }) async {
    final data =
        await _getJson('/api/safetyzones/check?lat=$latitude&lng=$longitude');
    return ZoneCheck.fromJson(data as Map<String, dynamic>);
  }

  // ----------------------------------------------------------------- writes

  /// Files a disaster report. Requires a signed-in citizen.
  ///
  /// The severity is deliberately not sent: grading is the Incident Analysis
  /// Agent's job, confirmed by a coordinator. A reporter says what they see.
  Future<Incident> createIncident({
    required String title,
    required String description,
    required String type,
    required double latitude,
    required double longitude,
    int affectedRadiusMeters = 1000,
    String? district,
    String? addressText,
    int? estimatedAffectedPeople,
  }) async {
    final response = await _send(
      http.Request('POST', _uri('/api/incidents'))
        ..headers['Content-Type'] = 'application/json'
        ..body = jsonEncode({
          'title': title.trim(),
          'description': description.trim(),
          'type': type,
          'latitude': latitude,
          'longitude': longitude,
          'affectedRadiusMeters': affectedRadiusMeters,
          if (district != null && district.trim().isNotEmpty)
            'district': district.trim(),
          if (addressText != null && addressText.trim().isNotEmpty)
            'addressText': addressText.trim(),
          'estimatedAffectedPeople': ?estimatedAffectedPeople,
        }),
      timeout: _timeout,
    );

    return Incident.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }

  /// Attaches a photo to a report that already exists.
  Future<void> uploadIncidentImage({
    required String incidentId,
    required File file,
    String? caption,
  }) async {
    final request =
        http.MultipartRequest('POST', _uri('/api/incidents/$incidentId/images'));

    final extension = file.path.split('.').last.toLowerCase();
    final subtype = switch (extension) {
      'png' => 'png',
      'webp' => 'webp',
      'heic' => 'heic',
      _ => 'jpeg',
    };

    request.files.add(
      await http.MultipartFile.fromPath(
        // Must match the IFormFile parameter name on the controller.
        'file',
        file.path,
        contentType: MediaType('image', subtype),
      ),
    );

    if (caption != null && caption.trim().isNotEmpty) {
      request.fields['caption'] = caption.trim();
    }

    await _send(request, timeout: _uploadTimeout);
  }

  // ---------------------------------------------------------------- plumbing

  Uri _uri(String path) => Uri.parse('${AppConfig.apiBaseUrl}$path');

  Future<dynamic> _getJson(String path) async {
    http.Response response;
    try {
      response = await _client.get(_uri(path)).timeout(_timeout);
    } catch (_) {
      throw ApiException(_unreachable);
    }

    if (response.statusCode != 200) {
      throw ApiException(
        'Server returned HTTP ${response.statusCode}.',
        statusCode: response.statusCode,
      );
    }

    try {
      return jsonDecode(response.body);
    } on FormatException {
      throw ApiException('The server sent a response the app could not read.');
    }
  }

  /// Sends an authenticated request and turns a failure into an ApiException
  /// carrying whatever the API was willing to explain.
  Future<http.Response> _send(
    http.BaseRequest request, {
    required Duration timeout,
  }) async {
    final token = auth?.token;
    if (token == null) {
      throw ApiException('Please sign in first.', statusCode: 401);
    }
    request.headers['Authorization'] = 'Bearer $token';

    http.Response response;
    try {
      final streamed = await _client.send(request).timeout(timeout);
      response = await http.Response.fromStream(streamed);
    } catch (_) {
      throw ApiException(_unreachable);
    }

    if (response.statusCode == 401) {
      // The token was rejected — clear it so the UI asks for a fresh sign-in
      // rather than retrying with something the server will keep refusing.
      await auth?.handleUnauthorized();
      throw ApiException(
        'Your session has expired. Please sign in again.',
        statusCode: 401,
      );
    }

    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw ApiException(
        _readMessage(response.body) ??
            'Request failed (HTTP ${response.statusCode}).',
        statusCode: response.statusCode,
      );
    }

    return response;
  }

  static String get _unreachable =>
      'Cannot reach the server at ${AppConfig.apiBaseUrl}.\n'
      'Check that the API is running and the address is right for this device.';

  static String? _readMessage(String body) {
    try {
      final decoded = jsonDecode(body);
      if (decoded is Map<String, dynamic>) {
        if (decoded['message'] is String) return decoded['message'] as String;

        final errors = decoded['errors'];
        if (errors is Map<String, dynamic> && errors.isNotEmpty) {
          final first = errors.values.first;
          if (first is List && first.isNotEmpty) return first.first.toString();
        }
      }
    } catch (_) {
      // Not JSON — the caller's default message stands.
    }
    return null;
  }

  void dispose() => _client.close();
}
