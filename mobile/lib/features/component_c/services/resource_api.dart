import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../../../shared/core/config.dart';

class ResourceApi {
  // The base URL comes from the shared config, like every other section's
  // client, so this works on iOS and the web too and not only on the Android
  // emulator.
  ResourceApi({http.Client? client, String? baseUrl})
    : _client = client ?? http.Client(),
      _baseUrl = baseUrl ?? AppConfig.apiBaseUrl;

  final http.Client _client;
  final String _baseUrl;

  Future<HelpRequest> createHelpRequest({
    required String name,
    required String phone,
    required String needType,
    required String description,
  }) async {
    const path = '/api/resources/help-requests';
    final response = await _post(path, {
      'requesterName': name,
      'contactNumber': phone,
      'needType': needType,
      'description': description,
      'latitude': null,
      'longitude': null,
    });
    return _decode<HelpRequest>(response, path, HelpRequest.fromJson);
  }

  Future<Donation> createDonation({
    required String name,
    required String phone,
    required String donationType,
    required double quantity,
    required String unit,
    String? notes,
  }) async {
    const path = '/api/resources/donations';
    final response = await _post(path, {
      'donorName': name,
      'contactNumber': phone,
      'donationType': donationType,
      'quantity': quantity,
      'unit': unit,
      'notes': notes,
    });
    return _decode<Donation>(response, path, Donation.fromJson);
  }

  Future<List<HelpRequest>> getHelpRequests() async {
    const path = '/api/resources/help-requests';
    final response = await _get(path);
    return _decodeList(response, path, HelpRequest.fromJson);
  }

  void dispose() => _client.close();

  static const Duration _timeout = Duration(seconds: 15);

  Future<http.Response> _get(String path) =>
      _send(() => _client.get(Uri.parse('$_baseUrl$path')));

  Future<http.Response> _post(String path, Map<String, dynamic> body) => _send(
    () => _client.post(
      Uri.parse('$_baseUrl$path'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode(body),
    ),
  );

  /// Keeps a server that answered badly distinct from one that never
  /// answered, and names the address either way — on an emulator a wrong
  /// host is the likeliest reason nothing loads.
  Future<http.Response> _send(Future<http.Response> Function() request) async {
    try {
      return await request().timeout(_timeout);
    } on TimeoutException {
      throw Exception('The resource service at $_baseUrl did not respond.');
    } on http.ClientException {
      throw Exception('Cannot reach the resource service at $_baseUrl.');
    } catch (_) {
      throw Exception('Cannot reach the resource service at $_baseUrl.');
    }
  }

  /// Whether a response succeeded. Anything else carries a reason worth
  /// showing, so it is never silently turned into an empty list.
  bool _ok(http.Response response) =>
      response.statusCode >= 200 && response.statusCode < 300;

  /// What went wrong, in words the screen can show.
  ///
  /// The status is always named, because "HTTP 500" is what distinguishes a
  /// server fault from a rejected form. The API sends `{ "error": ... }` for
  /// the faults it expects; an unhandled one sends a plain-text stack trace
  /// instead, so the body is parsed defensively and its first line is used.
  Exception _failure(http.Response response, String path) {
    final status = response.statusCode;
    final body = response.body.trim();

    String? reason;
    try {
      final decoded = jsonDecode(body);
      if (decoded is Map<String, dynamic>) {
        reason =
            (decoded['error'] ?? decoded['detail'] ?? decoded['title'])
                as String?;
      }
    } catch (_) {
      // Not JSON: a stack trace or an HTML error page. Its first line says
      // more than nothing.
      if (body.isNotEmpty && !body.startsWith('<')) {
        reason = body.split('\n').first;
      }
    }

    if (status >= 500) {
      return Exception(
        'The resource service failed (HTTP $status) on $_baseUrl$path.'
        '${reason == null ? '' : ' $reason'}',
      );
    }
    return Exception(
      reason ?? 'The request could not be completed (HTTP $status).',
    );
  }

  T _decode<T>(
    http.Response response,
    String path,
    T Function(Map<String, dynamic>) factory,
  ) {
    if (!_ok(response)) throw _failure(response, path);
    return factory(jsonDecode(response.body) as Map<String, dynamic>);
  }

  List<T> _decodeList<T>(
    http.Response response,
    String path,
    T Function(Map<String, dynamic>) factory,
  ) {
    if (!_ok(response)) throw _failure(response, path);
    return (jsonDecode(response.body) as List<dynamic>)
        .map((item) => factory(item as Map<String, dynamic>))
        .toList();
  }
}

class HelpRequest {
  const HelpRequest({
    required this.needType,
    required this.description,
    required this.status,
    required this.createdAt,
  });

  final String needType;
  final String description;
  final String status;
  final DateTime createdAt;

  factory HelpRequest.fromJson(Map<String, dynamic> json) => HelpRequest(
    needType: json['needType'] as String,
    description: json['description'] as String,
    status: json['status'] as String,
    createdAt: DateTime.parse(json['createdAtUtc'] as String),
  );
}

class Donation {
  const Donation({
    required this.donationType,
    required this.quantity,
    required this.unit,
    required this.status,
  });

  final String donationType;
  final double quantity;
  final String unit;
  final String status;

  factory Donation.fromJson(Map<String, dynamic> json) => Donation(
    donationType: json['donationType'] as String,
    quantity: (json['quantity'] as num).toDouble(),
    unit: json['unit'] as String,
    status: json['status'] as String,
  );
}
