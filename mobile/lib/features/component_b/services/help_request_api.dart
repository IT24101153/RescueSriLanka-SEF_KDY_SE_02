import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../../shared/core/config.dart';
import '../../../shared/services/auth_service.dart';

/// HTTP helper for the help-request screens.
///
/// Uses the app-wide session from [AuthService] and the API address from
/// [AppConfig], and returns the raw response so each caller decides how to
/// handle errors. [auth] is set by the Help tab before any request is made.
class HelpRequestApi {
  const HelpRequestApi._();

  static AuthService? _auth;
  static const FlutterSecureStorage _secureStorage = FlutterSecureStorage();
  static Future<void> _tokenWrite = Future<void>.value();
  static const String _tokenKey = 'component_b.jwt';

  static set auth(AuthService? value) {
    _auth = value;
    final token = value?.token;
    _tokenWrite = token == null
        ? _secureStorage.delete(key: _tokenKey)
        : _secureStorage.write(key: _tokenKey, value: token);
  }

  static Future<http.Response> get(String path) {
    return http.get(_uri(path), headers: await _headers());
  }

  static Future<http.Response> post(String path, Map<String, dynamic> body) {
    return http.post(_uri(path), headers: await _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> patch(String path, Map<String, dynamic> body) {
    return http.patch(_uri(path), headers: await _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> put(String path, Map<String, dynamic> body) {
    return http.put(_uri(path), headers: await _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> delete(String path) {
    return http.delete(_uri(path), headers: await _headers());
  }

  static Uri _uri(String path) => Uri.parse('${AppConfig.apiBaseUrl}$path');

  static Future<Map<String, String>> _headers() async {
    await _tokenWrite;
    final headers = {'Content-Type': 'application/json'};
    final currentToken = _auth?.token;
    if (currentToken == null) {
      await _secureStorage.delete(key: _tokenKey);
    } else {
      await _secureStorage.write(key: _tokenKey, value: currentToken);
    }
    final token = await _secureStorage.read(key: _tokenKey);
    if (token != null) {
      headers['Authorization'] = 'Bearer $token';
    }
    return headers;
  }
}
