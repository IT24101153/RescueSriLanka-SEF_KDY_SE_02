import 'dart:convert';
import '../services/api_client.dart';

class AuthUser {
  final String id;
  final String fullName;
  final String email;
  final String role;

  AuthUser({required this.id, required this.fullName, required this.email, required this.role});

  factory AuthUser.fromJson(Map<String, dynamic> json) => AuthUser(
        id: json['id'],
        fullName: json['fullName'],
        email: json['email'],
        role: json['role'],
      );
}

class AuthResult {
  final bool success;
  final String? errorMessage;
  final AuthUser? user;

  AuthResult.success(this.user)
      : success = true,
        errorMessage = null;

  AuthResult.failure(this.errorMessage)
      : success = false,
        user = null;
}

class AuthService {
  // Matches AuthController.cs / AuthDtos.cs exactly:
  // POST /api/auth/login and /api/auth/register
  // returns { token, expiresAt, user: { id, fullName, email, role, phoneNumber } }

  static Future<AuthResult> login(String email, String password) async {
    try {
      final res = await ApiClient.post('/api/auth/login', {
        'email': email,
        'password': password,
      });

      if (res.statusCode == 200) {
        final data = jsonDecode(res.body);
        await ApiClient.setToken(data['token']);
        return AuthResult.success(AuthUser.fromJson(data['user']));
      }

      if (res.statusCode == 401) {
        return AuthResult.failure('Incorrect email or password.');
      }
      return AuthResult.failure('Login failed. Try again.');
    } catch (_) {
      return AuthResult.failure('Could not reach the server. Check your connection.');
    }
  }

  static Future<AuthResult> register({
    required String fullName,
    required String email,
    required String password,
    String? phoneNumber,
  }) async {
    try {
      final res = await ApiClient.post('/api/auth/register', {
        'fullName': fullName,
        'email': email,
        'password': password,
        'phoneNumber': phoneNumber,
      });

      if (res.statusCode == 200) {
        final data = jsonDecode(res.body);
        await ApiClient.setToken(data['token']);
        return AuthResult.success(AuthUser.fromJson(data['user']));
      }

      if (res.statusCode == 409) {
        return AuthResult.failure('An account with that email already exists.');
      }
      return AuthResult.failure('Registration failed. Try again.');
    } catch (_) {
      return AuthResult.failure('Could not reach the server. Check your connection.');
    }
  }

  static Future<void> logout() async {
    await ApiClient.clearToken();
  }

  static Future<bool> isLoggedIn() async {
    final token = await ApiClient.getToken();
    return token != null;
  }
}