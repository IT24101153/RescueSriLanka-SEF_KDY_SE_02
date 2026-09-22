/// Mirrors SafetyZoneDto from the API.
class SafetyZone {
  const SafetyZone({
    required this.id,
    required this.name,
    required this.status,
    required this.source,
    required this.centerLatitude,
    required this.centerLongitude,
    required this.radiusMeters,
    this.district,
    this.rationale,
    this.sourceIncidentId,
  });

  final String id;
  final String name;
  final String status;
  final String source;
  final double centerLatitude;
  final double centerLongitude;
  final int radiusMeters;
  final String? district;
  final String? rationale;
  final String? sourceIncidentId;

  bool get isManual => source == 'ManualOverride';

  factory SafetyZone.fromJson(Map<String, dynamic> json) => SafetyZone(
        id: json['id'] as String,
        name: json['name'] as String? ?? 'Zone',
        status: json['status'] as String? ?? 'Caution',
        source: json['source'] as String? ?? 'DerivedFromIncident',
        centerLatitude: (json['centerLatitude'] as num).toDouble(),
        centerLongitude: (json['centerLongitude'] as num).toDouble(),
        radiusMeters: (json['radiusMeters'] as num?)?.toInt() ?? 1000,
        district: json['district'] as String?,
        rationale: json['rationale'] as String?,
        sourceIncidentId: json['sourceIncidentId'] as String?,
      );
}

/// Mirrors ZoneCheckResultDto — the answer to "is this spot safe?".
class ZoneCheck {
  const ZoneCheck({
    required this.status,
    required this.message,
    required this.nearbyIncidentCount,
  });

  final String status;
  final String message;
  final int nearbyIncidentCount;

  factory ZoneCheck.fromJson(Map<String, dynamic> json) => ZoneCheck(
        status: json['status'] as String? ?? 'Safe',
        message: json['message'] as String? ?? '',
        nearbyIncidentCount: (json['nearbyIncidentCount'] as num?)?.toInt() ?? 0,
      );
}
