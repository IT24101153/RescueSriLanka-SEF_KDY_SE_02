import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:mobile/features/component_b/screens/submit_request_screen.dart';
import 'package:mobile/features/component_b/services/help_request_service.dart';
import 'package:mobile/shared/services/auth_service.dart';
import 'package:mobile/features/component_b/widgets/estimated_people_field.dart';

Map<String, dynamic> response(int? count) => {
  'id': 'request-1', 'type': 3, 'description': 'Family needs rescue',
  'latitude': 7.0, 'longitude': 80.0, 'urgencyScore': 90,
  'status': 0, 'verificationStatus': 0, 'estimatedPeopleCount': count,
  'createdAt': '2026-09-25T08:00:00Z',
};

void main() {
  test('count and historical null parse safely', () {
    expect(HelpRequest.fromJson(response(12)).estimatedPeopleCount, 12);
    expect(HelpRequest.fromJson(response(null)).estimatedPeopleCount, isNull);
    expect(HelpRequest.fromJson(response(null)..remove('estimatedPeopleCount')).estimatedPeopleCount, isNull);
  });

  test('submission sends count and preserves existing payload fields', () async {
    await http.runWithClient(() async {
      final result = await HelpRequestService.submit(type: 3, description: 'Family needs rescue',
        estimatedPeopleCount: 12, latitude: 7, longitude: 80, imageUrl: 'https://example.test/photo.jpg');
      expect(result!.estimatedPeopleCount, 12);
    }, () => MockClient((request) async {
      expect(request.method, 'POST');
      expect(jsonDecode(request.body), {
        'type': 3, 'description': 'Family needs rescue', 'estimatedPeopleCount': 12,
        'latitude': 7.0, 'longitude': 80.0, 'relatedIncidentId': null,
        'imageUrl': 'https://example.test/photo.jpg',
      });
      return http.Response(jsonEncode(response(12)), 201);
    }));
  });

  testWidgets('citizen field is between description and photo', (tester) async {
    await tester.pumpWidget(MaterialApp(home: SubmitRequestScreen(auth: AuthService())));
    final description = tester.getTopLeft(find.byType(TextField).first).dy;
    final people = tester.getTopLeft(find.byType(EstimatedPeopleField)).dy;
    final photo = tester.getTopLeft(find.text('PHOTO (OPTIONAL)')).dy;
    expect(people, greaterThan(description));
    expect(people, lessThan(photo));
    expect(tester.widget<TextFormField>(find.byType(TextFormField)).controller, isNotNull);
  });

  testWidgets('requires positive whole numbers and rejects decimal and negative entry', (tester) async {
    final controller = TextEditingController();
    final key = GlobalKey<FormState>();
    await tester.pumpWidget(MaterialApp(home: Scaffold(body:
      Form(key: key, child: EstimatedPeopleField(controller: controller)))));
    expect(key.currentState!.validate(), isFalse);
    await tester.pump();
    expect(find.text('Please enter the number of people affected.'), findsOneWidget);
    await tester.enterText(find.byType(TextFormField), '0');
    expect(key.currentState!.validate(), isFalse);
    for (final value in ['-2', '1.5']) {
      await tester.enterText(find.byType(TextFormField), value);
      expect(controller.text, '0');
      expect(key.currentState!.validate(), isFalse);
    }
    controller.text = '-1';
    expect(key.currentState!.validate(), isFalse);
    await tester.enterText(find.byType(TextFormField), '12');
    expect(key.currentState!.validate(), isTrue);
    expect(controller.text, '12');
    await tester.pumpWidget(const SizedBox());
    controller.dispose();
  });
}
