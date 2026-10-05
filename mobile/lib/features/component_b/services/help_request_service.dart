import 'dart:convert';

import 'package:http_parser/http_parser.dart';
import 'package:image_picker/image_picker.dart';

import 'help_request_api.dart';

// Matches HelpRequestType enum: Water=0, Food=1, Medical=2, Rescue=3, Shelter=4, Other=5
const List<String> helpRequestTypeLabels = [
  'Water',
  'Food',
  'Medical',
  'Rescue',
  'Shelter',
  'Other',
];

// Matches HelpRequestStatus enum
const List<String> helpRequestStatusLabels = [
  'Pending',
  'Assigned',
  'In Progress',
  'Resolved',
  'Cancelled',
];

// Matches VerificationStatus enum
const List<String> verificationStatusLabels = [
  'Pending Verification',
  'Verified',
  'Rejected (Fake)',
];

class HelpRequest {
  final String id;
  final int type;
  final String description;
  final int? estimatedPeopleCount;
  final double latitude;
  final double longitude;
  final int urgencyScore;
  final int status;
  final int verificationStatus;
  final String? verificationNotes;
  final String? imageUrl;
  final DateTime createdAt;

  HelpRequest({
    required this.id,
    required this.type,
    required this.description,
    this.estimatedPeopleCount,
    required this.latitude,
    required this.longitude,
    required this.urgencyScore,
    required this.status,
    required this.verificationStatus,
    required this.verificationNotes,
    required this.imageUrl,
    required this.createdAt,
  });

  factory HelpRequest.fromJson(Map<String, dynamic> json) => HelpRequest(
    id: json['id'],
    type: json['type'],
    description: json['description'],
    estimatedPeopleCount: json['estimatedPeopleCount'] as int?,
    latitude: (json['latitude'] as num).toDouble(),
    longitude: (json['longitude'] as num).toDouble(),
    urgencyScore: json['urgencyScore'],
    status: json['status'],
    verificationStatus: json['verificationStatus'],
    verificationNotes: json['verificationNotes'],
    imageUrl: json['imageUrl'],
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

  factory StatusHistoryEntry.fromJson(Map<String, dynamic> json) =>
      StatusHistoryEntry(
        oldStatus: json['oldStatus'],
        newStatus: json['newStatus'],
        notes: json['notes'],
        changedAt: DateTime.parse(json['changedAt']),
      );
}

/// A message from the response team: a note and/or Do and Don't safety advice.
class HelpRequestMessage {
  final String id;
  final String? message;
  final List<String> doItems;
  final List<String> dontItems;
  final bool isCritical;
  final DateTime createdAt;

  HelpRequestMessage({
    required this.id,
    required this.message,
    required this.doItems,
    required this.dontItems,
    required this.isCritical,
    required this.createdAt,
  });

  factory HelpRequestMessage.fromJson(Map<String, dynamic> json) =>
      HelpRequestMessage(
        id: json['id'] as String,
        message: json['message'] as String?,
        doItems: (json['doItems'] as List<dynamic>? ?? []).cast<String>(),
        dontItems: (json['dontItems'] as List<dynamic>? ?? []).cast<String>(),
        isCritical: json['isCritical'] as bool? ?? false,
        createdAt: DateTime.parse(json['createdAt'] as String).toLocal(),
      );
}

/// An active warning for an area. [safetyLevel] is 1 Caution or 2 Danger.
class TravelAdvisory {
  final String id;
  final String areaName;
  final double radiusMeters;
  final int safetyLevel;
  final String reason;
  final DateTime? expiresAt;

  TravelAdvisory({
    required this.id,
    required this.areaName,
    required this.radiusMeters,
    required this.safetyLevel,
    required this.reason,
    required this.expiresAt,
  });

  factory TravelAdvisory.fromJson(Map<String, dynamic> json) => TravelAdvisory(
    id: json['id'] as String,
    areaName: json['areaName'] as String,
    radiusMeters: (json['radiusMeters'] as num).toDouble(),
    safetyLevel: json['safetyLevel'] as int,
    reason: json['reason'] as String? ?? '',
    expiresAt: json['expiresAt'] == null
        ? null
        : DateTime.parse(json['expiresAt'] as String).toLocal(),
  );
}

/// A public emergency number, national or for one district.
class EmergencyContact {
  final String name;
  final String category;
  final String phoneNumber;
  final String? secondaryPhoneNumber;
  final String? description;
  final String? district;
  final bool isAvailable24x7;

  EmergencyContact({
    required this.name,
    required this.category,
    required this.phoneNumber,
    required this.secondaryPhoneNumber,
    required this.description,
    required this.district,
    required this.isAvailable24x7,
  });

  factory EmergencyContact.fromJson(Map<String, dynamic> json) =>
      EmergencyContact(
        name: json['name'] as String,
        category: json['category'] as String,
        phoneNumber: json['phoneNumber'] as String,
        secondaryPhoneNumber: json['secondaryPhoneNumber'] as String?,
        description: json['description'] as String?,
        district: json['district'] as String?,
        isAvailable24x7: json['isAvailable24x7'] as bool? ?? false,
      );
}

/// The numbers that apply to one place. [matchedDistrict] is null when no
/// district unit matched, so only the national numbers are listed.
class EmergencyContacts {
  final String? matchedDistrict;
  final List<EmergencyContact> contacts;

  EmergencyContacts({required this.matchedDistrict, required this.contacts});

  factory EmergencyContacts.fromJson(Map<String, dynamic> json) =>
      EmergencyContacts(
        matchedDistrict: json['matchedDistrict'] as String?,
        contacts: (json['contacts'] as List<dynamic>)
            .map((c) => EmergencyContact.fromJson(c as Map<String, dynamic>))
            .toList(),
      );
}

class HelpRequestLoadResult {
  final List<HelpRequest> requests;
  final String? error;

  const HelpRequestLoadResult({required this.requests, this.error});
}

class HelpRequestService {
  /// Uploads [photo] through the API, which stores it on Cloudinary, and
  /// returns the hosted URL for the request to carry. Null when the upload fails.
  static Future<String?> uploadPhoto(XFile photo) async {
    try {
      final bytes = await photo.readAsBytes();
      final res = await HelpRequestApi.postFile(
        '/api/HelpRequests/photo',
        field: 'photo',
        bytes: bytes,
        filename: photo.name,
        contentType: MediaType('image', _imageSubtype(photo.name)),
      );
      if (res.statusCode != 200) return null;
      final body = jsonDecode(res.body) as Map<String, dynamic>;
      return body['url'] as String?;
    } catch (_) {
      return null;
    }
  }

  // The API accepts only these four; anything else is sent as jpeg and the
  // server's signature check refuses it with a clear message.
  static String _imageSubtype(String filename) {
    final extension = filename.split('.').last.toLowerCase();
    return switch (extension) {
      'png' => 'png',
      'webp' => 'webp',
      'heic' => 'heic',
      _ => 'jpeg',
    };
  }

  static Future<HelpRequest?> submit({
    required int estimatedPeopleCount,
    required int type,
    required String description,
    required double latitude,
    required double longitude,
    String? imageUrl,
  }) async {
    final res = await HelpRequestApi.post('/api/HelpRequests', {
      'type': type,
      'description': description,
      'estimatedPeopleCount': estimatedPeopleCount,
      'latitude': latitude,
      'longitude': longitude,
      'relatedIncidentId': null,
      'imageUrl': imageUrl,
    });

    if (res.statusCode == 200 || res.statusCode == 201) {
      return HelpRequest.fromJson(jsonDecode(res.body));
    }
    return null;
  }

  static Future<List<HelpRequest>> getMine() async {
    return (await getMineWithStatus()).requests;
  }

  static Future<HelpRequestLoadResult> getAllWithStatus() =>
      _loadRequests('/api/HelpRequests');

  /// Keeps a failed request fetch distinct from a genuine empty request list.
  static Future<HelpRequestLoadResult> getMineWithStatus() async {
    return _loadRequests('/api/HelpRequests/mine');
  }

  static Future<HelpRequestLoadResult> _loadRequests(String path) async {
    try {
      final res = await HelpRequestApi.get(path);
      if (res.statusCode == 401) {
        return const HelpRequestLoadResult(
          requests: [],
          error: 'Your session has expired. Please sign in again.',
        );
      }
      if (res.statusCode != 200) {
        return const HelpRequestLoadResult(
          requests: [],
          error: 'Could not load your requests. Pull down or tap refresh to try again.',
        );
      }
      final List<dynamic> data = jsonDecode(res.body);
      return HelpRequestLoadResult(
        requests: data.map((json) => HelpRequest.fromJson(json)).toList(),
      );
    } catch (_) {
      return const HelpRequestLoadResult(
        requests: [],
        error: 'Could not reach the request service. Check your connection and API server.',
      );
    }
  }

  static Future<List<StatusHistoryEntry>> getHistory(String id) async {
    final res = await HelpRequestApi.get('/api/HelpRequests/$id/history');
    if (res.statusCode != 200) return [];

    final List<dynamic> data = jsonDecode(res.body);
    return data.map((json) => StatusHistoryEntry.fromJson(json)).toList();
  }

  /// What the response team has told the citizen about [id], urgent first.
  /// Null when it cannot be loaded, which is not the same as no messages.
  static Future<List<HelpRequestMessage>?> getMessages(String id) async {
    try {
      final res = await HelpRequestApi.get('/api/HelpRequests/$id/messages');
      if (res.statusCode != 200) return null;
      return (jsonDecode(res.body) as List<dynamic>)
          .map((m) => HelpRequestMessage.fromJson(m as Map<String, dynamic>))
          .toList();
    } catch (_) {
      return null;
    }
  }

  /// Warnings in force now, worst first. Null when they cannot be loaded.
  static Future<List<TravelAdvisory>?> getActiveAdvisories() async {
    try {
      final res = await HelpRequestApi.get('/api/TravelAdvisories');
      if (res.statusCode != 200) return null;
      final advisories = (jsonDecode(res.body) as List<dynamic>)
          .map((a) => TravelAdvisory.fromJson(a as Map<String, dynamic>))
          .toList();
      advisories.sort((a, b) => b.safetyLevel.compareTo(a.safetyLevel));
      return advisories;
    } catch (_) {
      return null;
    }
  }

  /// Numbers for where [id] was requested. Null when they cannot be loaded.
  static Future<EmergencyContacts?> getRequestEmergencyContacts(String id) =>
      _loadContacts('/api/HelpRequests/$id/emergency-contacts');

  /// Numbers for a place; with no coordinates only the national ones come back.
  static Future<EmergencyContacts?> getEmergencyContacts({
    double? latitude,
    double? longitude,
  }) {
    final query = [
      if (latitude != null && longitude != null) 'latitude=$latitude',
      if (latitude != null && longitude != null) 'longitude=$longitude',
    ].join('&');
    return _loadContacts(
      '/api/HelpRequests/emergency-contacts${query.isEmpty ? '' : '?$query'}',
    );
  }

  static Future<EmergencyContacts?> _loadContacts(String path) async {
    try {
      final res = await HelpRequestApi.get(path);
      if (res.statusCode != 200) return null;
      return EmergencyContacts.fromJson(
        jsonDecode(res.body) as Map<String, dynamic>,
      );
    } catch (_) {
      return null;
    }
  }

  static Future<bool> update({
    required int estimatedPeopleCount,
    required HelpRequest request,
    required int type,
    required String description,
  }) async {
    final res = await HelpRequestApi.put('/api/HelpRequests/${request.id}', {
      'estimatedPeopleCount': estimatedPeopleCount,
      'type': type,
      'description': description,
      'latitude': request.latitude,
      'longitude': request.longitude,
      'relatedIncidentId': null,
      'imageUrl': request.imageUrl,
    });
    return res.statusCode == 200;
  }

  static Future<bool> cancel(String id) async {
    final res = await HelpRequestApi.delete('/api/HelpRequests/$id');
    return res.statusCode == 204;
  }
}
