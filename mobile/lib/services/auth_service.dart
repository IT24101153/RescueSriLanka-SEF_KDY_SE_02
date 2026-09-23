import 'dart:convert';

import 'package:http/http.dart' as http;

import '../config/api_config.dart';
import '../models/auth_models.dart';

class AuthService {
  Future<AuthSession> login({
    required String email,
    required String password,
  }) async {
    final http.Response response;
    try {
      response = await http.post(
        Uri.parse('${ApiConfig.baseUrl}/api/auth/login'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'email': email.trim(), 'password': password}),
      );
    } on http.ClientException {
      throw Exception('Cannot reach the RescueSriLanka server.');
    }

    if (response.statusCode == 200) {
      return AuthSession.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>,
      );
    }

    if (response.statusCode == 401) {
      throw Exception('Invalid email or password.');
    }

    throw Exception('Sign-in failed (HTTP ${response.statusCode}).');
  }
}
