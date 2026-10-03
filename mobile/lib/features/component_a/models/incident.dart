import '../../../shared/core/config.dart';

/// A photo attached to a report. Mirrors IncidentImageDto.
class IncidentPhoto {
  const IncidentPhoto({required this.id, required this.url, this.caption});

  final String id;

  /// As the API stores it: an absolute Cloudinary URL. Use [resolvedUrl] to load it.
  final String url;
  final String? caption;

  String get resolvedUrl =>
      url.startsWith('http') ? url : '${AppConfig.apiBaseUrl}$url';

  factory IncidentPhoto.fromJson(Map<String, dynamic> json) => IncidentPhoto(
        id: json['id'] as String? ?? '',
        url: json['url'] as String? ?? '',
        caption: json['caption'] as String?,
      );

  Map<String, dynamic> toJson() => {'id': id, 'url': url, 'caption': caption};
}

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
    this.images = const [],
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

  /// The photos themselves; the list and nearby queries send them along.
  final List<IncidentPhoto> images;
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
        images: [
          for (final image in (json['images'] as List? ?? const []))
            IncidentPhoto.fromJson(image as Map<String, dynamic>),
        ].where((photo) => photo.url.isNotEmpty).toList(),
        distanceKm: (json['distanceKm'] as num?)?.toDouble(),
      );

  /// The same shape [Incident.fromJson] reads, so a report can be kept on
  /// the device and read back.
  Map<String, dynamic> toJson() => {
        'id': id,
        'title': title,
        'description': description,
        'type': type,
        'severity': severity,
        'status': status,
        'latitude': latitude,
        'longitude': longitude,
        'affectedRadiusMeters': affectedRadiusMeters,
        'reportedAt': reportedAt.toUtc().toIso8601String(),
        'district': district,
        'addressText': addressText,
        'estimatedAffectedPeople': estimatedAffectedPeople,
        'aiSeverity': aiSeverity,
        'aiSeverityScore': aiSeverityScore,
        'aiConfidence': aiConfidence,
        'aiRationale': aiRationale,
        'imageCount': imageCount,
        'images': [for (final photo in images) photo.toJson()],
      };
}
