import 'dart:convert';

import 'package:http/http.dart' as http;

import '../core/config.dart';
import 'auth_service.dart';

/// HTTP helper for the help-request screens.
///
/// Uses the app-wide session from [AuthService] and the API address from
/// [AppConfig], and returns the raw response so each caller decides how to
/// handle errors. [auth] is set by the Help tab before any request is made.
class HelpRequestApi {
  const HelpRequestApi._();

  static AuthService? auth;

  static Future<http.Response> get(String path) {
    return http.get(_uri(path), headers: _headers());
  }

  static Future<http.Response> post(String path, Map<String, dynamic> body) {
    return http.post(_uri(path), headers: _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> patch(String path, Map<String, dynamic> body) {
    return http.patch(_uri(path), headers: _headers(), body: jsonEncode(body));
  }

  static Uri _uri(String path) => Uri.parse('${AppConfig.apiBaseUrl}$path');

  static Map<String, String> _headers() {
    final headers = {'Content-Type': 'application/json'};
    final token = auth?.token;
    if (token != null) {
      headers['Authorization'] = 'Bearer $token';
    }
    return headers;
  }
}
