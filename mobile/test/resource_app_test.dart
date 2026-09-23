// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/resource_app.dart';

void main() {
  testWidgets('resource request and donation tabs are available', (WidgetTester tester) async {
    await tester.pumpWidget(const MyApp());
    await tester.pumpAndSettle();

    expect(find.text('What do you need?'), findsOneWidget);
    expect(find.text('Request help'), findsOneWidget);
    expect(find.text('Donate'), findsOneWidget);

    await tester.tap(find.text('Donate'));
    await tester.pumpAndSettle();

    expect(find.text('Give what you can'), findsOneWidget);
    expect(find.text('Offer donation'), findsOneWidget);
  });
}
