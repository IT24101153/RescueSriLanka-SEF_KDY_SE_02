import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/component_b/screens/submit_request_screen.dart';
import 'package:mobile/features/component_b/services/help_request_service.dart';

void main() {
  group('Component B request contract', () {
    test('decodes citizen status and history values from the API contract', () {
      final request = HelpRequest.fromJson({
        'id': 'request-1',
        'type': 3,
        'description': 'Need rescue support',
        'latitude': 7.2,
        'longitude': 80.6,
        'urgencyScore': 90,
        'status': 2,
        'verificationStatus': 1,
        'verificationNotes': null,
        'imageUrl': null,
        'createdAt': '2026-09-25T08:00:00Z',
      });
      final history = StatusHistoryEntry.fromJson({
        'oldStatus': 1,
        'newStatus': 2,
        'notes': 'Responder started travel',
        'changedAt': '2026-09-25T08:05:00Z',
      });

      expect(helpRequestTypeLabels[request.type], 'Rescue');
      expect(helpRequestStatusLabels[request.status], 'In Progress');
      expect(request.urgencyScore, 90);
      expect(helpRequestStatusLabels[history.oldStatus], 'Assigned');
      expect(helpRequestStatusLabels[history.newStatus], 'In Progress');
      expect(history.notes, 'Responder started travel');
    });
  });

  testWidgets('request submission requires a description and location', (
    tester,
  ) async {
    await tester.pumpWidget(const MaterialApp(home: SubmitRequestScreen()));

    await tester.tap(find.text('Submit request'));
    await tester.pump();
    expect(find.text('Please describe what help you need.'), findsOneWidget);

    await tester.enterText(
      find.byType(TextField).first,
      'Need water for my family',
    );
    await tester.tap(find.text('Submit request'));
    await tester.pump();
    expect(find.text('Please share your location first.'), findsOneWidget);
  });
}
