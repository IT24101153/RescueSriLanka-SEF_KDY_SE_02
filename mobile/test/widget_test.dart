import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/core/theme.dart';
import 'package:mobile/models/incident.dart';
import 'package:mobile/models/safety_zone.dart';
import 'package:mobile/widgets/severity_chip.dart';

void main() {
  group('Incident parsing', () {
    test('reads the API payload', () {
      final incident = Incident.fromJson(const {
        'id': 'a1',
        'title': 'Kelani River flooding',
        'description': 'Homes inundated.',
        'type': 'Flood',
        'severity': 'Critical',
        'status': 'InProgress',
        'latitude': 6.935,
        'longitude': 79.889,
        'affectedRadiusMeters': 3200,
        'reportedAt': '2026-09-09T10:00:00Z',
        'district': 'Colombo',
        'estimatedAffectedPeople': 2400,
        'aiSeverityScore': 88,
        'imageCount': 2,
      });

      expect(incident.title, 'Kelani River flooding');
      expect(incident.severity, 'Critical');
      expect(incident.estimatedAffectedPeople, 2400);
      expect(incident.isAnalysed, isTrue);
    });

    test('survives missing optional fields', () {
      final incident = Incident.fromJson(const {
        'id': 'a2',
        'latitude': 7.0,
        'longitude': 80.0,
      });

      expect(incident.district, isNull);
      expect(incident.isAnalysed, isFalse);
      expect(incident.severity, 'Low');
    });
  });

  test('SafetyZone distinguishes manual overrides', () {
    final derived = SafetyZone.fromJson(const {
      'id': 'z1',
      'name': 'Flood — Colombo',
      'status': 'Danger',
      'source': 'DerivedFromIncident',
      'centerLatitude': 6.9,
      'centerLongitude': 79.9,
      'radiusMeters': 3200,
    });

    expect(derived.isManual, isFalse);
    expect(derived.status, 'Danger');
  });

  group('Severity encoding', () {
    test('every level has a distinct colour, size and icon', () {
      const levels = ['Low', 'Moderate', 'High', 'Critical'];

      final colours = levels.map(AppColors.forSeverity).toSet();
      final sizes = levels.map(AppColors.radiusForSeverity).toSet();
      final icons = levels.map(AppColors.iconForSeverity).toSet();

      // Severity must never be encoded by colour alone.
      expect(colours.length, levels.length);
      expect(sizes.length, levels.length);
      expect(icons.length, levels.length);
    });

    testWidgets('chip shows the label, not just a colour', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(body: SeverityChip(severity: 'Critical')),
        ),
      );

      expect(find.text('Critical'), findsOneWidget);
      expect(find.byIcon(Icons.crisis_alert), findsOneWidget);
    });
  });
}
