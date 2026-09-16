/// Mirrors IncidentDto from the ASP.NET Core API.
class Incident {
  const Incident({
    required this.id,
    required this.title,
    required this.description,
    required this.type,
    required this.severity,
    required this.status,
    required this.latitude,
    required this.longitude,
    required this.affectedRadiusMeters,
    required this.reportedAt,
    this.district,
    this.addressText,
    this.estimatedAffectedPeople,
    this.aiSeverity,
    this.aiSeverityScore,
    this.aiConfidence,
    this.aiRationale,
    this.imageCount = 0,
    this.distanceKm,
  });

  final String id;
  final String title;
  final String description;
  final String type;
  final String severity;
  final String status;
  final double latitude;
  final double longitude;
  final int affectedRadiusMeters;
  final DateTime reportedAt;
  final String? district;
  final String? addressText;
  final int? estimatedAffectedPeople;
  final String? aiSeverity;
  final int? aiSeverityScore;
  final double? aiConfidence;
  final String? aiRationale;
  final int imageCount;
  final double? distanceKm;

  bool get isAnalysed => aiSeverityScore != null;

  factory Incident.fromJson(Map<String, dynamic> json) => Incident(
        id: json['id'] as String,
        title: json['title'] as String? ?? 'Untitled incident',
        description: json['description'] as String? ?? '',
        type: json['type'] as String? ?? 'Other',
        severity: json['severity'] as String? ?? 'Low',
        status: json['status'] as String? ?? 'Reported',
        latitude: (json['latitude'] as num).toDouble(),
        longitude: (json['longitude'] as num).toDouble(),
        affectedRadiusMeters: (json['affectedRadiusMeters'] as num?)?.toInt() ?? 1000,
        reportedAt:
            DateTime.tryParse(json['reportedAt'] as String? ?? '')?.toLocal() ??
                DateTime.now(),
        district: json['district'] as String?,
        addressText: json['addressText'] as String?,
        estimatedAffectedPeople: (json['estimatedAffectedPeople'] as num?)?.toInt(),
        aiSeverity: json['aiSeverity'] as String?,
        aiSeverityScore: (json['aiSeverityScore'] as num?)?.toInt(),
        aiConfidence: (json['aiConfidence'] as num?)?.toDouble(),
        aiRationale: json['aiRationale'] as String?,
        imageCount: (json['imageCount'] as num?)?.toInt() ?? 0,
        distanceKm: (json['distanceKm'] as num?)?.toDouble(),
      );
}
