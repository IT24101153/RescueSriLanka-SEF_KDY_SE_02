import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

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

  static set auth(AuthService? value) {
    _auth = value;
  }

  static Future<http.Response> get(String path) {
    return http.get(_uri(path), headers: _headers());
  }

  static Future<http.Response> post(String path, Map<String, dynamic> body) {
    return http.post(_uri(path), headers: _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> patch(String path, Map<String, dynamic> body) {
    return http.patch(_uri(path), headers: _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> put(String path, Map<String, dynamic> body) {
    return http.put(_uri(path), headers: _headers(), body: jsonEncode(body));
  }

  static Future<http.Response> delete(String path) {
    return http.delete(_uri(path), headers: _headers());
  }

  /// Sends one file as a multipart form. The content type is set by the
  /// request itself, so [contentType] must describe the bytes exactly.
  static Future<http.Response> postFile(
    String path, {
    required String field,
    required List<int> bytes,
    required String filename,
    required MediaType contentType,
  }) async {
    final request = http.MultipartRequest('POST', _uri(path))
      ..headers.addAll(_authHeaders())
      ..files.add(
        http.MultipartFile.fromBytes(
          field,
          bytes,
          filename: filename,
          contentType: contentType,
        ),
      );
    final streamed = await request.send();
    return http.Response.fromStream(streamed);
  }

  static Uri _uri(String path) => Uri.parse('${AppConfig.apiBaseUrl}$path');

  static Map<String, String> _headers() {
    return {'Content-Type': 'application/json', ..._authHeaders()};
  }

  static Map<String, String> _authHeaders() {
    final token = _auth?.token;
    return token == null ? {} : {'Authorization': 'Bearer $token'};
  }
}
