import 'dart:convert';

import 'package:http/http.dart' as http;

class ResourceApi {
  ResourceApi({http.Client? client, String? baseUrl})
      : _client = client ?? http.Client(),
        _baseUrl = baseUrl ?? 'http://10.0.2.2:5093';

  final http.Client _client;
  final String _baseUrl;

  Future<HelpRequest> createHelpRequest({
    required String name,
    required String phone,
    required String needType,
    required String description,
  }) async {
    final response = await _client.post(
      Uri.parse('$_baseUrl/api/resources/help-requests'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'requesterName': name,
        'contactNumber': phone,
        'needType': needType,
        'description': description,
        'latitude': null,
        'longitude': null,
      }),
    );
    return _decode<HelpRequest>(response, HelpRequest.fromJson);
  }

  Future<Donation> createDonation({
    required String name,
    required String phone,
    required String donationType,
    required double quantity,
    required String unit,
    String? notes,
  }) async {
    final response = await _client.post(
      Uri.parse('$_baseUrl/api/resources/donations'),
      headers: {'Content-Type': 'application/json'},
      body: jsonEncode({
        'donorName': name,
        'contactNumber': phone,
        'donationType': donationType,
        'quantity': quantity,
        'unit': unit,
        'notes': notes,
      }),
    );
    return _decode<Donation>(response, Donation.fromJson);
  }

  Future<List<HelpRequest>> getHelpRequests() async {
    final response = await _client.get(Uri.parse('$_baseUrl/api/resources/help-requests'));
    return _decodeList(response, HelpRequest.fromJson);
  }

  T _decode<T>(http.Response response, T Function(Map<String, dynamic>) factory) {
    final body = jsonDecode(response.body) as Map<String, dynamic>;
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(body['error'] ?? 'The request could not be completed.');
    }
    return factory(body);
  }

  List<T> _decodeList<T>(http.Response response, T Function(Map<String, dynamic>) factory) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception('Unable to load your requests.');
    }
    return (jsonDecode(response.body) as List<dynamic>)
        .map((item) => factory(item as Map<String, dynamic>))
        .toList();
  }
}

class HelpRequest {
  const HelpRequest({required this.needType, required this.description, required this.status, required this.createdAt});

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
  const Donation({required this.donationType, required this.quantity, required this.unit, required this.status});

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
