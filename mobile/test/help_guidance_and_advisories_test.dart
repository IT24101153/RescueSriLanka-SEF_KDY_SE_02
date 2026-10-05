import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'package:mobile/features/component_b/screens/travel_advisories_screen.dart';
import 'package:mobile/features/component_b/widgets/guidance_section.dart';
import 'package:mobile/shared/core/theme.dart';

/// Runs [body] with every [http] call answered by [handler], which is how the
/// Help screens reach the API.
Future<void> withApi(
  http.Response Function(http.Request request) handler,
  Future<void> Function() body,
) => http.runWithClient(body, () => MockClient((r) async => handler(r)));

Widget host(Widget child) =>
    MaterialApp(theme: buildAppTheme(), home: Scaffold(body: child));

void main() {
  testWidgets('guidance shows the Do and Don\'t lists and the urgent flag', (
    tester,
  ) async {
    await withApi(
      (request) {
        expect(request.url.path, '/api/HelpRequests/r-1/messages');
        return http.Response(
          jsonEncode([
            {
              'id': 'm-1',
              'message': 'A team has been informed.',
              'doItems': ['Move to the upper floor'],
              'dontItems': ['Do not go outside'],
              'isCritical': true,
              'createdAt': '2026-10-05T10:00:00Z',
            },
          ]),
          200,
        );
      },
      () async {
        await tester.pumpWidget(host(const GuidanceSection(requestId: 'r-1')));
        await tester.pump();
        await tester.pump();

        expect(find.text('Urgent'), findsOneWidget);
        expect(find.text('A team has been informed.'), findsOneWidget);
        expect(find.text('Move to the upper floor'), findsOneWidget);
        expect(find.text('Do not go outside'), findsOneWidget);
        expect(find.text('Do'), findsOneWidget);
        expect(find.text("Don't"), findsOneWidget);
      },
    );
  });

  testWidgets('guidance says so when nothing has been sent', (tester) async {
    await withApi((_) => http.Response('[]', 200), () async {
      await tester.pumpWidget(host(const GuidanceSection(requestId: 'r-1')));
      await tester.pump();
      await tester.pump();

      expect(find.textContaining('No messages from the response team'), findsOneWidget);
    });
  });

  testWidgets('guidance tells a failed load apart from no messages', (
    tester,
  ) async {
    await withApi((_) => http.Response('nope', 500), () async {
      await tester.pumpWidget(host(const GuidanceSection(requestId: 'r-1')));
      await tester.pump();
      await tester.pump();

      expect(find.textContaining('Could not load messages'), findsOneWidget);
    });
  });

  testWidgets('advisories list the worst first, with their level', (
    tester,
  ) async {
    await withApi(
      (request) {
        expect(request.url.path, '/api/TravelAdvisories');
        return http.Response(
          jsonEncode([
            {
              'id': 'a-1',
              'areaName': 'Kalutara town centre',
              'latitude': 6.58,
              'longitude': 79.96,
              'radiusMeters': 1500,
              'safetyLevel': 1,
              'reason': 'Water on the road.',
              'expiresAt': null,
            },
            {
              'id': 'a-2',
              'areaName': 'Kelani river bank',
              'latitude': 6.95,
              'longitude': 79.9,
              'radiusMeters': 3000,
              'safetyLevel': 2,
              'reason': 'River is above the danger level.',
              'expiresAt': null,
            },
          ]),
          200,
        );
      },
      () async {
        await tester.pumpWidget(
          MaterialApp(theme: buildAppTheme(), home: const TravelAdvisoriesScreen()),
        );
        await tester.pump();
        await tester.pump();

        expect(find.text('Danger'), findsOneWidget);
        expect(find.text('Caution'), findsOneWidget);
        expect(
          tester.getTopLeft(find.text('Kelani river bank')).dy,
          lessThan(tester.getTopLeft(find.text('Kalutara town centre')).dy),
        );
        expect(find.text('River is above the danger level.'), findsOneWidget);
      },
    );
  });

  testWidgets('advisories say when none are in force', (tester) async {
    await withApi((_) => http.Response('[]', 200), () async {
      await tester.pumpWidget(
        MaterialApp(theme: buildAppTheme(), home: const TravelAdvisoriesScreen()),
      );
      await tester.pump();
      await tester.pump();

      expect(find.text('No active advisories'), findsOneWidget);
    });
  });
}
