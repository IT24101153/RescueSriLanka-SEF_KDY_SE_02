class AppUser {
  final String id;
  final String fullName;
  final String email;
  final String role;
  final String? phoneNumber;

  const AppUser({
    required this.id,
    required this.fullName,
    required this.email,
    required this.role,
    this.phoneNumber,
  });

  factory AppUser.fromJson(Map<String, dynamic> json) {
    return AppUser(
      id: json['id'] as String,
      fullName: json['fullName'] as String,
      email: json['email'] as String,
      role: json['role'] as String,
      phoneNumber: json['phoneNumber'] as String?,
    );
  }
}

class AuthSession {
  final String token;
  final DateTime expiresAt;
  final AppUser user;

  const AuthSession({
    required this.token,
    required this.expiresAt,
    required this.user,
  });

  factory AuthSession.fromJson(Map<String, dynamic> json) {
    return AuthSession(
      token: json['token'] as String,
      expiresAt: DateTime.parse(json['expiresAt'] as String),
      user: AppUser.fromJson(json['user'] as Map<String, dynamic>),
    );
  }
}
