import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:mobile/features/component_b/screens/home_screen.dart';
import 'package:mobile/shared/core/theme.dart';
import 'package:mobile/shared/services/auth_service.dart';

String authBody() => jsonEncode({
  'token': 'test-token',
  'expiresAt': DateTime.now().add(const Duration(hours: 8)).toIso8601String(),
  'user': {
    'id': '11111111-1111-1111-1111-111111111111',
    'fullName': 'Nimali Perera',
    'email': 'nimali@example.lk',
    'role': 'Citizen',
  },
});

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  testWidgets('the Help tab shows its hub once signed in', (tester) async {
    final auth = AuthService(
      client: MockClient((_) async => http.Response(authBody(), 200)),
    );
    await auth.signIn(email: 'nimali@example.lk', password: 'Rescue@123');

    await tester.pumpWidget(
      MaterialApp(
        theme: buildAppTheme(),
        home: HelpRequestsTab(auth: auth),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Help is within reach.'), findsOneWidget);
    expect(find.text('Request help'), findsOneWidget);
    expect(find.text('My requests'), findsOneWidget);

    // The last card sits below the fold at test-window height.
    await tester.scrollUntilVisible(find.text('Safety check'), 200);
    expect(find.text('Safety check'), findsOneWidget);
  });

  testWidgets('signed out, it offers sign-in instead', (tester) async {
    final auth = AuthService(
      client: MockClient((_) async => http.Response(authBody(), 200)),
    );

    await tester.pumpWidget(
      MaterialApp(
        theme: buildAppTheme(),
        home: HelpRequestsTab(auth: auth),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Sign in to request help'), findsOneWidget);
  });
}
