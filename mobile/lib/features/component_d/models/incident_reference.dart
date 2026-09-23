class IncidentReference {
  const IncidentReference({
    required this.id,
    required this.title,
    required this.type,
    required this.severity,
    required this.status,
    this.district,
    this.addressText,
    required this.isActive,
    required this.reportedAt,
  });

  final String id;
  final String title;
  final String type;
  final String severity;
  final String status;
  final String? district;
  final String? addressText;
  final bool isActive;
  final DateTime reportedAt;

  factory IncidentReference.fromJson(Map<String, dynamic> json) {
    final id = json['id'] as String;
    if (!RegExp(
          r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
        ).hasMatch(id) ||
        id == '00000000-0000-0000-0000-000000000000') {
      throw const FormatException('Invalid incident reference.');
    }
    return IncidentReference(
      id: id,
      title: json['title'] as String,
      type: json['type'] as String,
      severity: json['severity'] as String,
      status: json['status'] as String,
      district: json['district'] as String?,
      addressText: json['addressText'] as String?,
      isActive: json['isActive'] as bool,
      reportedAt: DateTime.parse(json['reportedAt'] as String),
    );
  }

  String get label => [
    type,
    if (district?.trim().isNotEmpty == true)
      district!.trim()
    else if (addressText?.trim().isNotEmpty == true)
      addressText!.trim(),
    title,
  ].join(' — ');
}
