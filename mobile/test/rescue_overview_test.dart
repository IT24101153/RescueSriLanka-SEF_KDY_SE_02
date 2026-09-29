import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/features/component_d/screens/rescue_overview_screen.dart';
import 'package:mobile/features/component_d/services/rescue_coordination_service.dart';
import 'package:mobile/shared/core/theme.dart';
import 'package:mobile/shared/models/auth.dart';

class _FakeService extends RescueCoordinationService {
  _FakeService(this.data);
  final Map<String, dynamic> data;

  @override
  Future<Map<String, dynamic>> getRescueOverview(AuthSession session) async =>
      data;
}

final _session = AuthSession(
  token: 't',
  expiresAt: DateTime.now().add(const Duration(hours: 1)),
  user: const AuthUser(
    id: '1',
    fullName: 'Nimali Perera',
    email: 'n@example.lk',
    role: 'Citizen',
  ),
);

Future<void> _pump(WidgetTester tester, Map<String, dynamic> data) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: buildAppTheme(),
      home: RescueOverviewScreen(
        session: _session,
        service: _FakeService(data),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('shows readiness and friendly team details', (
    tester,
  ) async {
    await _pump(tester, {
      'teamsAvailable': 1,
      'teamsOnMission': 1,
      'activeDispatches': 2,
      'teams': [
        {
          'name': 'Alpha Team',
          'status': 'Available',
          'memberCount': 4,
          'availableMemberCount': 3,
          'skills': ['WaterRescue', 'FirstAid'],
          'vehicles': [
            {'type': 'Boat', 'count': 2},
          ],
        },
        {
          'name': 'Bravo Team',
          'status': 'OnMission',
          'memberCount': 2,
          'availableMemberCount': 0,
          'skills': <String>[],
          'vehicles': <Map<String, dynamic>>[],
        },
      ],
    });

    expect(find.text('1 rescue team is ready'), findsOneWidget);
    expect(find.text('In immediate danger? Call now'), findsNothing);
    expect(find.text('Alpha Team'), findsOneWidget);
    expect(find.text('Water rescue'), findsOneWidget);
    expect(find.text('3 of 4 members free'), findsOneWidget);
    expect(find.text('2 Boat'), findsOneWidget);
  });

  testWidgets('the filter narrows the list', (tester) async {
    await _pump(tester, {
      'teamsAvailable': 1,
      'teamsOnMission': 1,
      'activeDispatches': 0,
      'teams': [
        {
          'name': 'Alpha Team',
          'status': 'Available',
          'memberCount': 1,
          'availableMemberCount': 1,
          'skills': <String>[],
          'vehicles': <Map<String, dynamic>>[],
        },
        {
          'name': 'Bravo Team',
          'status': 'OnMission',
          'memberCount': 1,
          'availableMemberCount': 0,
          'skills': <String>[],
          'vehicles': <Map<String, dynamic>>[],
        },
      ],
    });

    await tester.tap(find.widgetWithText(ChoiceChip, 'On a mission'));
    await tester.pumpAndSettle();

    expect(find.text('Bravo Team'), findsOneWidget);
    expect(find.text('Alpha Team'), findsNothing);
  });

  testWidgets('says so plainly when no team is free', (tester) async {
    await _pump(tester, {
      'teamsAvailable': 0,
      'teamsOnMission': 1,
      'activeDispatches': 1,
      'teams': [
        {
          'name': 'Bravo Team',
          'status': 'OnMission',
          'memberCount': 1,
          'availableMemberCount': 0,
          'skills': <String>[],
          'vehicles': <Map<String, dynamic>>[],
        },
      ],
    });

    expect(find.text('All teams are busy right now'), findsOneWidget);
  });
}
