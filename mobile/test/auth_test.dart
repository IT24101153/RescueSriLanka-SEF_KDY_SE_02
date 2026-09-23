import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:mobile/shared/models/auth.dart';
import 'package:mobile/features/component_a/services/api_client.dart';
import 'package:mobile/shared/services/auth_service.dart';

/// Builds an AuthResponse body the way the API returns one.
String authBody({DateTime? expiresAt}) => jsonEncode({
      'token': 'test-token',
      'expiresAt': (expiresAt ?? DateTime.now().add(const Duration(hours: 8)))
          .toIso8601String(),
      'user': {
        'id': '11111111-1111-1111-1111-111111111111',
        'fullName': 'Nimali Perera',
        'email': 'nimali@example.lk',
        'role': 'Citizen',
      },
    });

void main() {
  setUp(() {
    // Each test starts with empty storage, so a saved session cannot leak
    // between them.
    SharedPreferences.setMockInitialValues({});
  });

  group('AuthSession', () {
    test('round-trips through JSON', () {
      final original = AuthSession.fromJson(
        jsonDecode(authBody()) as Map<String, dynamic>,
      );

      final restored = AuthSession.fromJson(
        jsonDecode(jsonEncode(original.toJson())) as Map<String, dynamic>,
      );

      expect(restored.token, original.token);
      expect(restored.user.email, 'nimali@example.lk');
      expect(restored.user.shortName, 'Nimali');
    });

    test('knows when it has expired', () {
      final stale = AuthSession.fromJson(
        jsonDecode(authBody(
          expiresAt: DateTime.now().subtract(const Duration(minutes: 1)),
        )) as Map<String, dynamic>,
      );

      expect(stale.isExpired, isTrue);
    });
  });

  group('AuthService', () {
    test('signing in stores the session', () async {
      final auth = AuthService(
        client: MockClient((_) async => http.Response(authBody(), 200)),
      );

      final result = await auth.signIn(
        email: 'nimali@example.lk',
        password: 'password123',
      );

      expect(result.ok, isTrue);
      expect(auth.isSignedIn, isTrue);
      expect(auth.token, 'test-token');

      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('rsl.session'), isNotNull);
    });

    test('reports bad credentials without signing in', () async {
      final auth = AuthService(
        client: MockClient((_) async => http.Response('{}', 401)),
      );

      final result = await auth.signIn(
        email: 'nimali@example.lk',
        password: 'wrong',
      );

      expect(result.ok, isFalse);
      expect(result.message, contains('Invalid email or password'));
      expect(auth.isSignedIn, isFalse);
    });

    test('reports a duplicate email on registration', () async {
      final auth = AuthService(
        client: MockClient((_) async => http.Response('{}', 409)),
      );

      final result = await auth.register(
        fullName: 'Nimali Perera',
        email: 'taken@example.lk',
        password: 'password123',
      );

      expect(result.ok, isFalse);
      expect(result.message, contains('already exists'));
    });

    test('surfaces an unreachable server rather than throwing', () async {
      final auth = AuthService(
        client: MockClient((_) async => throw const SocketExceptionStub()),
      );

      final result = await auth.signIn(
        email: 'nimali@example.lk',
        password: 'password123',
      );

      expect(result.ok, isFalse);
      expect(result.message, contains('Cannot reach the server'));
    });

    test('signing out clears both memory and storage', () async {
      final auth = AuthService(
        client: MockClient((_) async => http.Response(authBody(), 200)),
      );
      await auth.signIn(email: 'nimali@example.lk', password: 'password123');

      await auth.signOut();

      expect(auth.isSignedIn, isFalse);
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('rsl.session'), isNull);
    });

    test('restore brings back a valid stored session', () async {
      SharedPreferences.setMockInitialValues({'rsl.session': authBody()});

      final auth = AuthService(client: MockClient((_) async => http.Response('{}', 500)));
      await auth.restore();

      expect(auth.isSignedIn, isTrue);
      expect(auth.isRestoring, isFalse);
    });

    test('restore discards an expired token instead of using it', () async {
      SharedPreferences.setMockInitialValues({
        'rsl.session': authBody(
          expiresAt: DateTime.now().subtract(const Duration(hours: 1)),
        ),
      });

      final auth = AuthService(client: MockClient((_) async => http.Response('{}', 500)));
      await auth.restore();

      // An expired token would otherwise fail every later call with a 401.
      expect(auth.isSignedIn, isFalse);
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('rsl.session'), isNull);
    });
  });

  group('ApiClient writes', () {
    test('refuse to send without a session', () async {
      final api = ApiClient(auth: AuthService(client: MockClient((_) async {
        fail('No request should reach the network without a token.');
      })));

      await expectLater(
        api.createIncident(
          title: 'Road flooded',
          description: 'Water is waist deep across the road.',
          type: 'Flood',
          latitude: 6.9271,
          longitude: 79.8612,
        ),
        throwsA(isA<ApiException>().having((e) => e.isUnauthorized, 'isUnauthorized', isTrue)),
      );
    });

    test('attach the bearer token and omit blank optional fields', () async {
      final auth = AuthService(
        client: MockClient((_) async => http.Response(authBody(), 200)),
      );
      await auth.signIn(email: 'nimali@example.lk', password: 'password123');

      late http.Request captured;

      final api = ApiClient(
        auth: auth,
        client: MockClient((request) async {
          captured = request;
          return http.Response(
            jsonEncode({
              'id': 'abc',
              'title': 'Road flooded',
              'description': 'Water is waist deep across the road.',
              'type': 'Flood',
              'severity': 'Moderate',
              'status': 'Reported',
              'latitude': 6.9271,
              'longitude': 79.8612,
              'affectedRadiusMeters': 1000,
              'reportedAt': DateTime.now().toIso8601String(),
            }),
            201,
          );
        }),
      );

      await api.createIncident(
        title: 'Road flooded',
        description: 'Water is waist deep across the road.',
        type: 'Flood',
        latitude: 6.9271,
        longitude: 79.8612,
        district: '   ',
      );

      expect(captured.headers['Authorization'], 'Bearer test-token');

      final body = jsonDecode(captured.body) as Map<String, dynamic>;
      expect(body['type'], 'Flood');
      // A whitespace-only district must not reach the API as a value.
      expect(body.containsKey('district'), isFalse);
      // Severity is the agent's call, never the reporter's.
      expect(body.containsKey('severity'), isFalse);
    });

    test('a rejected token clears the session', () async {
      final auth = AuthService(
        client: MockClient((_) async => http.Response(authBody(), 200)),
      );
      await auth.signIn(email: 'nimali@example.lk', password: 'password123');
      expect(auth.isSignedIn, isTrue);

      final api = ApiClient(
        auth: auth,
        client: MockClient((_) async => http.Response('{}', 401)),
      );

      await expectLater(
        api.createIncident(
          title: 'Road flooded',
          description: 'Water is waist deep across the road.',
          type: 'Flood',
          latitude: 6.9271,
          longitude: 79.8612,
        ),
        throwsA(isA<ApiException>()),
      );

      expect(auth.isSignedIn, isFalse);
    });
  });
}

/// Stands in for a connection failure without importing dart:io into a test
/// that otherwise needs none.
class SocketExceptionStub implements Exception {
  const SocketExceptionStub();
}
