import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/screens/home_screen.dart';
import 'package:mobile/services/auth_service.dart';

void main() {
  testWidgets('Help tab asks a signed-out user to sign in', (tester) async {
    final auth = AuthService();
    addTearDown(auth.dispose);

    await tester.pumpWidget(MaterialApp(home: HelpRequestsTab(auth: auth)));

    expect(find.text('Sign in to request help'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Sign in'), findsOneWidget);
  });
}
