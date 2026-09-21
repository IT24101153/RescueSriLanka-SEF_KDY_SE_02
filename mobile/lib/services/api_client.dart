import 'dart:convert';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

/// Central place for API config.
class ApiConfig {
  // 10.0.2.2 is the Android emulator's alias for the host machine's localhost.
  // Web/desktop can reach localhost directly.
  // For a physical device, replace with your machine's local network IP,
  // e.g. http://192.168.1.5:5093
  static String get baseUrl {
    if (kIsWeb) return 'http://localhost:5093';
    return 'http://10.0.2.2:5093'; // Android emulator default
  }
}

class ApiClient {
  static const _tokenKey = 'authToken';

  static Future<String?> getToken() async {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getString(_tokenKey);
  }

  static Future<void> setToken(String token) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_tokenKey, token);
  }

  static Future<void> clearToken() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_tokenKey);
  }

  static Future<http.Response> get(String path) async {
    final token = await getToken();
    return http.get(
      Uri.parse('${ApiConfig.baseUrl}$path'),
      headers: _headers(token),
    );
  }

  static Future<http.Response> post(String path, Map<String, dynamic> body) async {
    final token = await getToken();
    return http.post(
      Uri.parse('${ApiConfig.baseUrl}$path'),
      headers: _headers(token),
      body: jsonEncode(body),
    );
  }

  static Future<http.Response> patch(String path, Map<String, dynamic> body) async {
    final token = await getToken();
    return http.patch(
      Uri.parse('${ApiConfig.baseUrl}$path'),
      headers: _headers(token),
      body: jsonEncode(body),
    );
  }

  static Map<String, String> _headers(String? token) {
    final headers = {'Content-Type': 'application/json'};
    if (token != null) {
      headers['Authorization'] = 'Bearer $token';
    }
    return headers;
  }
}