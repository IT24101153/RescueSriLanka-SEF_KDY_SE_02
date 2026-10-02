import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:mobile/shared/models/auth.dart';
import 'package:mobile/features/component_d/screens/coordination_forms.dart';
import 'package:mobile/features/component_d/models/coordination_requests.dart';
import 'package:mobile/features/component_d/services/rescue_coordination_service.dart';

final session = AuthSession(token: 'test-only', expiresAt: DateTime(2100), user: const AuthUser(id: 'test', fullName: 'Test', email: 'test@example.test', role: 'RescueTeam'));
void main() {
  testWidgets('vehicle editor labels people/patients capacity', (tester) async {
    await tester.pumpWidget(MaterialApp(home: ResourceEditor(session: session, kind: ResourceKind.vehicle, teamId: 'team')));
    await tester.pumpAndSettle();
    expect(find.text('People/Patients Capacity'), findsOneWidget);
  });
  test('duplicate registration conflict is shown without hiding the reason', () async {
    await http.runWithClient(() async {
      await expectLater(RescueCoordinationService().addVehicle(session, '00000000-0000-4000-8000-000000000001', const VehicleInput(plateNumber: 'WP CAB-1234', type: 'Ambulance', capacity: 4)),
        throwsA(isA<RescueCoordinationException>().having((e) => e.message, 'message', contains('A vehicle with this registration number already exists.'))));
    }, () => MockClient((request) async {
      expect(jsonDecode(request.body)['capacity'], 4);
      return http.Response(jsonEncode({'error': 'A vehicle with this registration number already exists.'}), 409);
    }));
  });
}
