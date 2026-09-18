/// Mirrors UserDto from the API.
class AuthUser {
  const AuthUser({
    required this.id,
    required this.fullName,
    required this.email,
    required this.role,
    this.phoneNumber,
  });

  final String id;
  final String fullName;
  final String email;
  final String role;
  final String? phoneNumber;

  /// First name only — all the greeting needs, and kinder to narrow screens.
  String get shortName => fullName.split(' ').first;

  factory AuthUser.fromJson(Map<String, dynamic> json) => AuthUser(
        id: json['id'] as String,
        fullName: json['fullName'] as String? ?? 'Citizen',
        email: json['email'] as String? ?? '',
        role: json['role'] as String? ?? 'Citizen',
        phoneNumber: json['phoneNumber'] as String?,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'fullName': fullName,
        'email': email,
        'role': role,
        'phoneNumber': phoneNumber,
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
