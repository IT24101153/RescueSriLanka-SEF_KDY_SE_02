import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'package:mobile/features/component_c/services/resource_api.dart';

/// A failure the Resources screen cannot explain is a failure nobody can fix,
/// so each kind has to reach the screen with its cause intact.
void main() {
  ResourceApi apiReturning(http.Response response) => ResourceApi(
    client: MockClient((_) async => response),
    baseUrl: 'http://localhost:5093',
  );

  test('a 500 with a stack trace names the status and its first line', () {
    final api = apiReturning(
      http.Response(
        'System.InvalidOperationException: Gemini:ApiKey is not configured.\n'
        '   at RescueSriLanka.Api.Something()',
        500,
      ),
    );

    expect(
      api.getHelpRequests(),
      throwsA(
        isA<Exception>().having(
          (error) => error.toString(),
          'message',
          allOf(
            contains('HTTP 500'),
            // The failing call names itself, so a screenshot of the banner
            // is enough to act on.
            contains('http://localhost:5093/api/resources/help-requests'),
            contains('Gemini:ApiKey is not configured.'),
          ),
        ),
      ),
    );
  });

  test('a 400 shows the reason the API gave', () {
    final api = apiReturning(
      http.Response(
        jsonEncode({
          'error':
              'Name, contact number, need type, and '
              'description are required.',
        }),
        400,
      ),
    );

    expect(
      api.createHelpRequest(
        name: '',
        phone: '',
        needType: 'Rescue',
        description: 'x',
      ),
      throwsA(
        isA<Exception>().having(
          (error) => error.toString(),
          'message',
          contains('are required'),
        ),
      ),
    );
  });

  test('an unreachable service names the address', () {
    final api = ResourceApi(
      client: MockClient(
        (_) async => throw http.ClientException('Connection refused'),
      ),
      baseUrl: 'http://10.0.2.2:5093',
    );

    expect(
      api.getHelpRequests(),
      throwsA(
        isA<Exception>().having(
          (error) => error.toString(),
          'message',
          contains('http://10.0.2.2:5093'),
        ),
      ),
    );
  });

  test('a good response still decodes', () async {
    final api = apiReturning(
      http.Response(
        jsonEncode([
          {
            'needType': 'Food and water',
            'description': 'Family of four',
            'status': 'Pending',
            'createdAtUtc': '2026-09-23T10:00:00Z',
          },
        ]),
        200,
      ),
    );

    final requests = await api.getHelpRequests();

    expect(requests, hasLength(1));
    expect(requests.single.needType, 'Food and water');
  });
}
