import '../core/config.dart';

/// Mirrors UserDto from the API.
class AuthUser {
  const AuthUser({
    required this.id,
    required this.fullName,
    required this.email,
    required this.role,
    this.phoneNumber,
    this.district,
    this.photoUrl,
    this.emailNotificationsEnabled = true,
    this.pushNotificationsEnabled = false,
  });

  final String id;
  final String fullName;
  final String email;
  final String role;
  final String? phoneNumber;

  /// District this person wants disaster warnings for. Null means none set, so
  /// no area warnings are sent.
  final String? district;

  /// Absolute or server-relative URL of the profile photo. Null until one is
  /// uploaded.
  final String? photoUrl;

  final bool emailNotificationsEnabled;

  /// Whether push notifications are on for this account. Off until the person
  /// turns them on in the profile, which also registers the phone.
  final bool pushNotificationsEnabled;

  /// [photoUrl] as a loadable URL. Photos are on Cloudinary, so it is already
  /// absolute; a site-relative value could only be a pre-Cloudinary upload.
  String? get resolvedPhotoUrl {
    final url = photoUrl;
    if (url == null || url.isEmpty) return null;
    return url.startsWith('http') ? url : '${AppConfig.apiBaseUrl}$url';
  }

  /// First name only — all the greeting needs, and kinder to narrow screens.
  String get shortName => fullName.split(' ').first;

  factory AuthUser.fromJson(Map<String, dynamic> json) => AuthUser(
        id: json['id'] as String,
        fullName: json['fullName'] as String? ?? 'Citizen',
        email: json['email'] as String? ?? '',
        role: json['role'] as String? ?? 'Citizen',
        phoneNumber: json['phoneNumber'] as String?,
        district: json['district'] as String?,
        photoUrl: json['photoUrl'] as String?,
        // Defaults to on, matching the API, so a session stored by an older
        // build does not read as opted out.
        emailNotificationsEnabled:
            json['emailNotificationsEnabled'] as bool? ?? true,
        pushNotificationsEnabled:
            json['pushNotificationsEnabled'] as bool? ?? false,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'fullName': fullName,
        'email': email,
        'role': role,
        'phoneNumber': phoneNumber,
        'district': district,
        'photoUrl': photoUrl,
        'emailNotificationsEnabled': emailNotificationsEnabled,
        'pushNotificationsEnabled': pushNotificationsEnabled,
      };
}

/// Mirrors AuthResponse — the token plus who it belongs to.
class AuthSession {
  const AuthSession({
    required this.token,
    required this.expiresAt,
    required this.user,
  });

  final String token;
  final DateTime expiresAt;
  final AuthUser user;

  bool get isExpired => DateTime.now().isAfter(expiresAt);

  factory AuthSession.fromJson(Map<String, dynamic> json) => AuthSession(
        token: json['token'] as String,
        expiresAt:
            DateTime.tryParse(json['expiresAt'] as String? ?? '')?.toLocal() ??
                DateTime.now().add(const Duration(hours: 8)),
        user: AuthUser.fromJson(json['user'] as Map<String, dynamic>),
      );

  Map<String, dynamic> toJson() => {
        'token': token,
        'expiresAt': expiresAt.toIso8601String(),
        'user': user.toJson(),
      };
}
