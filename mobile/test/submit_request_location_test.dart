import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:mobile/features/component_b/screens/submit_request_screen.dart';
import 'package:mobile/shared/core/theme.dart';
import 'package:mobile/shared/services/auth_service.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
    FlutterSecureStorage.setMockInitialValues({});
  });

  Future<void> open(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildAppTheme(),
        home: SubmitRequestScreen(auth: AuthService()),
      ),
    );
    await tester.pump();
  }

  testWidgets('the form offers no AI report guidance', (tester) async {
    await open(tester);

    expect(find.textContaining('AI'), findsNothing);
  });

  testWidgets('a spot can be picked on the map', (tester) async {
    await open(tester);

    await tester.tap(find.text('Pick on the map'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 500));

    expect(find.text('Mark the spot'), findsOneWidget);
    expect(find.text('Use this spot'), findsOneWidget);

    await tester.tap(find.text('Use this spot'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 500));

    // Back on the form, with the pin's coordinates shown.
    expect(find.textContaining('Pin at 7.87310, 80.77180'), findsOneWidget);
    expect(find.text('Change spot'), findsOneWidget);
  });
}
