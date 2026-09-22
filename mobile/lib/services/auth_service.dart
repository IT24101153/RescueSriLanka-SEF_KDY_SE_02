import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:shared_preferences/shared_preferences.dart';

import '../core/config.dart';
import '../models/auth.dart';

/// Result of a sign-in or registration attempt. The API deliberately keeps its
/// failure messages vague, and so does this.
class AuthResult {
  const AuthResult.success(this.session)
      : message = null,
        ok = true;
  const AuthResult.failure(this.message)
      : session = null,
        ok = false;

  final bool ok;
  final AuthSession? session;
  final String? message;
}

/// Holds the signed-in session for the whole app.
///
/// A ChangeNotifier rather than a state-management package: one value, a
/// handful of listeners, and nothing here justifies another dependency.
/// Reading the map needs no session at all — only reporting does — so the app
/// starts signed out and stays usable that way.
class AuthService extends ChangeNotifier {
  AuthService({http.Client? client}) : _client = client ?? http.Client();

  static const String _storageKey = 'rsl.session';
  static const Duration _timeout = Duration(seconds: 15);

  final http.Client _client;

  AuthSession? _session;
  bool _restoring = true;

  AuthSession? get session => _session;
  AuthUser? get user => _session?.user;
  bool get isSignedIn => _session != null;

  /// True until the stored token has been read back at start-up, so the UI can
  /// avoid flashing "signed out" at someone who is in fact signed in.
  bool get isRestoring => _restoring;

  String? get token => _session?.token;

  /// Reads any saved session back. An expired token is dropped here rather
  /// than left to fail every later call.
  Future<void> restore() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final raw = prefs.getString(_storageKey);

      if (raw != null) {
        final restored =
            AuthSession.fromJson(jsonDecode(raw) as Map<String, dynamic>);
        if (!restored.isExpired) {
          _session = restored;
        } else {
          await prefs.remove(_storageKey);
        }
      }
    } catch (_) {
      // Corrupt or unreadable storage is not worth failing start-up over —
      // the person simply signs in again.
      _session = null;
    } finally {
      _restoring = false;
      notifyListeners();
    }
  }

  Future<AuthResult> signIn({
    required String email,
    required String password,
  }) =>
      _authenticate('/api/auth/login', {
        'email': email.trim(),
        'password': password,
      });

  Future<AuthResult> register({
    required String fullName,
    required String email,
    required String password,
    String? phoneNumber,
  }) =>
      _authenticate('/api/auth/register', {
        'fullName': fullName.trim(),
        'email': email.trim(),
        'password': password,
        if (phoneNumber != null && phoneNumber.trim().isNotEmpty)
          'phoneNumber': phoneNumber.trim(),
      });

  /// Saves the notification settings, then folds the user the API returns back
  /// into the stored session — otherwise the profile screen would show the new
  /// district while the app still remembered the old one.
  ///
  /// [clearDistrict] exists because null already means "leave this alone": the
  /// API distinguishes an absent field from an explicit null, and only the
  /// latter unsubscribes someone.
  Future<AuthResult> updatePreferences({
    String? district,
    bool clearDistrict = false,
    bool? emailNotificationsEnabled,
  }) async {
    final current = _session;
    if (current == null) {
      return const AuthResult.failure('Please sign in first.');
    }

    final body = <String, dynamic>{
      if (clearDistrict) 'district': null else 'district': ?district,
      'emailNotificationsEnabled': ?emailNotificationsEnabled,
    };

    http.Response response;
    try {
      response = await _client
          .patch(
            Uri.parse('${AppConfig.apiBaseUrl}/api/auth/me/preferences'),
            headers: {
              'Content-Type': 'application/json',
              'Authorization': 'Bearer ${current.token}',
            },
            body: jsonEncode(body),
          )
          .timeout(_timeout);
    } catch (_) {
      return AuthResult.failure(
        'Cannot reach the server at ${AppConfig.apiBaseUrl}.',
      );
    }

    if (response.statusCode == 401) {
      await handleUnauthorized();
      return const AuthResult.failure(
        'Your session has expired. Please sign in again.',
      );
    }

    if (response.statusCode != 200) {
      return AuthResult.failure(
        _readMessage(response.body) ??
            'Could not save your settings (HTTP ${response.statusCode}).',
      );
    }

    try {
      final updated = AuthSession(
        token: current.token,
        expiresAt: current.expiresAt,
        user: AuthUser.fromJson(
          jsonDecode(response.body) as Map<String, dynamic>,
        ),
      );

      _session = updated;
      notifyListeners();

      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_storageKey, jsonEncode(updated.toJson()));

      return AuthResult.success(updated);
    } catch (_) {
      return const AuthResult.failure(
        'The server sent a response the app could not read.',
      );
    }
  }

  Future<void> signOut() async {
    _session = null;
    notifyListeners();

    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.remove(_storageKey);
    } catch (_) {
      // The in-memory session is already gone, which is what matters.
    }
  }

  /// Called by the API client when the server rejects the token, so a stale
  /// session cannot sit there failing every request.
  Future<void> handleUnauthorized() => signOut();

  Future<AuthResult> _authenticate(
    String path,
    Map<String, dynamic> body,
  ) async {
    http.Response response;

    try {
      response = await _client
          .post(
            Uri.parse('${AppConfig.apiBaseUrl}$path'),
            headers: const {'Content-Type': 'application/json'},
            body: jsonEncode(body),
          )
          .timeout(_timeout);
    } catch (_) {
      return AuthResult.failure(
        'Cannot reach the server at ${AppConfig.apiBaseUrl}.\n'
        'Check that the API is running and the address is right for this device.',
      );
    }

    if (response.statusCode == 401) {
      return const AuthResult.failure('Invalid email or password.');
    }

    if (response.statusCode == 409) {
      return const AuthResult.failure(
        'An account with that email already exists.',
      );
    }

    if (response.statusCode == 400) {
      return AuthResult.failure(_readMessage(response.body) ??
          'Please check the details and try again.');
    }

    if (response.statusCode != 200) {
      return AuthResult.failure('Sign-in failed (HTTP ${response.statusCode}).');
    }

    try {
      final session = AuthSession.fromJson(
        jsonDecode(response.body) as Map<String, dynamic>,
      );

      _session = session;
      notifyListeners();

      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_storageKey, jsonEncode(session.toJson()));

      return AuthResult.success(session);
    } catch (_) {
      return const AuthResult.failure(
        'The server sent a response the app could not read.',
      );
    }
  }

  /// Pulls "message" out of an error body, tolerating ASP.NET's validation shape.
  static String? _readMessage(String body) {
    try {
      final decoded = jsonDecode(body);
      if (decoded is Map<String, dynamic>) {
        if (decoded['message'] is String) return decoded['message'] as String;

        final errors = decoded['errors'];
        if (errors is Map<String, dynamic> && errors.isNotEmpty) {
          final first = errors.values.first;
          if (first is List && first.isNotEmpty) return first.first.toString();
        }
      }
    } catch (_) {
      // Not JSON — fall through to the caller's default.
    }
    return null;
  }

  @override
  void dispose() {
    _client.close();
    super.dispose();
  }
}
