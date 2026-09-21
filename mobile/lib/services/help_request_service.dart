import 'dart:convert';
import '../services/api_client.dart';

// Matches HelpRequestType enum: Water=0, Food=1, Medical=2, Rescue=3, Shelter=4, Other=5
const List<String> helpRequestTypeLabels = ['Water', 'Food', 'Medical', 'Rescue', 'Shelter', 'Other'];

// Matches HelpRequestStatus enum
const List<String> helpRequestStatusLabels = ['Pending', 'Assigned', 'In Progress', 'Resolved', 'Cancelled'];

// Matches VerificationStatus enum
const List<String> verificationStatusLabels = ['Pending Verification', 'Verified', 'Rejected (Fake)'];

class HelpRequest {
  final String id;
  final int type;
  final String description;
  final double latitude;
  final double longitude;
  final int urgencyScore;
  final int status;
  final int verificationStatus;
  final String? verificationNotes;
  final DateTime createdAt;

  HelpRequest({
    required this.id,
    required this.type,
    required this.description,
    required this.latitude,
    required this.longitude,
    required this.urgencyScore,
    required this.status,
    required this.verificationStatus,
    required this.verificationNotes,
    required this.createdAt,
  });

  factory HelpRequest.fromJson(Map<String, dynamic> json) => HelpRequest(
        id: json['id'],
        type: json['type'],
        description: json['description'],
        latitude: (json['latitude'] as num).toDouble(),
        longitude: (json['longitude'] as num).toDouble(),
        urgencyScore: json['urgencyScore'],
        status: json['status'],
        verificationStatus: json['verificationStatus'],
        verificationNotes: json['verificationNotes'],
        createdAt: DateTime.parse(json['createdAt']),
      );
}

class StatusHistoryEntry {
  final int oldStatus;
  final int newStatus;
  final String? notes;
  final DateTime changedAt;

  StatusHistoryEntry({
    required this.oldStatus,
    required this.newStatus,
    required this.notes,
    required this.changedAt,
  });

  factory StatusHistoryEntry.fromJson(Map<String, dynamic> json) => StatusHistoryEntry(
        oldStatus: json['oldStatus'],
        newStatus: json['newStatus'],
        notes: json['notes'],
        changedAt: DateTime.parse(json['changedAt']),
      );
}

class HelpRequestService {
  static Future<HelpRequest?> submit({
    required int type,
    required String description,
    required double latitude,
    required double longitude,
  }) async {
    final res = await ApiClient.post('/api/HelpRequests', {
      'type': type,
      'description': description,
      'latitude': latitude,
      'longitude': longitude,
      'relatedIncidentId': null,
      'imageUrl': null,
    });

    if (res.statusCode == 200 || res.statusCode == 201) {
      return HelpRequest.fromJson(jsonDecode(res.body));
    }
    return null;
  }

  static Future<List<HelpRequest>> getMine() async {
    final res = await ApiClient.get('/api/HelpRequests/mine');
    if (res.statusCode != 200) return [];

    final List<dynamic> data = jsonDecode(res.body);
    return data.map((json) => HelpRequest.fromJson(json)).toList();
  }

  static Future<List<StatusHistoryEntry>> getHistory(String id) async {
    final res = await ApiClient.get('/api/HelpRequests/$id/history');
    if (res.statusCode != 200) return [];

    final List<dynamic> data = jsonDecode(res.body);
    return data.map((json) => StatusHistoryEntry.fromJson(json)).toList();
  }
}