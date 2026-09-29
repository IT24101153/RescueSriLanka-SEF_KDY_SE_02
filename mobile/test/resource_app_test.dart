// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/features/component_c/screens/resource_home_screen.dart';
import 'package:mobile/shared/core/theme.dart';
import 'package:mobile/shared/models/auth.dart';
import 'package:mobile/shared/services/auth_service.dart';

void main() {
  testWidgets('resource request and donation tabs are available', (
    WidgetTester tester,
  ) async {
    // The section is a tab inside the app shell now, so the test supplies
    // the frame the shell would.
    await tester.pumpWidget(
      MaterialApp(theme: buildAppTheme(), home: const ResourceHomePage()),
    );
    await tester.pumpAndSettle();

    expect(find.text('What do you need?'), findsOneWidget);
    expect(find.text('Resource request'), findsOneWidget);
    expect(find.text('Donate'), findsOneWidget);

    await tester.tap(find.text('Donate'));
    await tester.pumpAndSettle();

    expect(find.text('Give what you can'), findsOneWidget);
    expect(find.text('Offer donation'), findsOneWidget);
  });

  testWidgets('request form omits profile fields and shelter category', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(theme: buildAppTheme(), home: const ResourceHomePage()),
    );
    await tester.pumpAndSettle();

    expect(find.text('Your name'), findsNothing);
    expect(find.text('Contact number'), findsNothing);

    final requestTypeDropdown = find.byType(DropdownButtonFormField<String>).first;
    await tester.ensureVisible(requestTypeDropdown);
    await tester.pumpAndSettle();
    await tester.tap(requestTypeDropdown);
    await tester.pumpAndSettle();
    expect(find.text('Shelter'), findsNothing);
    expect(find.text('Rescue'), findsNothing);
    expect(find.text('Food'), findsWidgets);
  });

  testWidgets('request form validates profile phone and required item fields', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildAppTheme(),
        home: ResourceHomePage(auth: _TestAuthService('12345')),
      ),
    );
    await tester.pumpAndSettle();
    final submitButton = find.text('Send request');
    await tester.ensureVisible(submitButton);
    await tester.pumpAndSettle();
    await tester.tap(submitButton);
    await tester.pumpAndSettle();
    expect(
      find.text(
        'Your profile phone number must be exactly 10 digits before sending a request.',
      ),
      findsOneWidget,
    );

    await tester.pumpWidget(
      MaterialApp(
        theme: buildAppTheme(),
        home: ResourceHomePage(auth: _TestAuthService('0771234567')),
      ),
    );
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Send request'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Send request'));
    await tester.pumpAndSettle();
    expect(find.text('Enter a positive quantity.'), findsOneWidget);
    expect(find.text('Enter a unit.'), findsOneWidget);
  });

  testWidgets('donation categories show medical subcategories and custom item', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(theme: buildAppTheme(), home: const ResourceHomePage()),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Donate'));
    await tester.pumpAndSettle();

    final dropdowns = find.byType(DropdownButtonFormField<String>);
    await tester.ensureVisible(dropdowns.first);
    await tester.tap(dropdowns.first);
    await tester.pumpAndSettle();
    expect(find.text('Food'), findsWidgets);
    expect(find.text('Water'), findsWidgets);
    expect(find.text('Medical'), findsWidgets);
    expect(find.text('Sanitary products'), findsWidgets);
    expect(find.text('Hygiene items'), findsWidgets);

    await tester.tap(find.text('Water').last);
    await tester.pumpAndSettle();
    final waterItemDropdown = find.byType(DropdownButtonFormField<String>).last;
    await tester.ensureVisible(waterItemDropdown);
    await tester.tap(waterItemDropdown);
    await tester.pumpAndSettle();
    expect(find.text('Bottled water'), findsWidgets);
    expect(find.text('Water containers'), findsOneWidget);

    await tester.tap(find.text('Bottled water').last);
    await tester.pumpAndSettle();
    final donationCategoryDropdown = find.byType(
      DropdownButtonFormField<String>,
    ).first;
    await tester.ensureVisible(donationCategoryDropdown);
    await tester.pumpAndSettle();
    await tester.tap(donationCategoryDropdown);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Medical').last);
    await tester.pumpAndSettle();
    final medicalItemDropdown = find.byType(DropdownButtonFormField<String>).last;
    await tester.ensureVisible(medicalItemDropdown);
    await tester.tap(medicalItemDropdown);
    await tester.pumpAndSettle();
    expect(find.text('Bandages'), findsWidgets);
    expect(find.text('Plasters'), findsOneWidget);
    expect(find.text('Saline'), findsOneWidget);

    await tester.tap(find.text('Other').last);
    await tester.pumpAndSettle();
    expect(find.text('Specify item'), findsOneWidget);
  });

  testWidgets('request and donation forms can add another item row', (
    WidgetTester tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(theme: buildAppTheme(), home: const ResourceHomePage()),
    );
    await tester.pumpAndSettle();

    expect(find.text('Item 1'), findsOneWidget);
    final requestAddButton = find.text('Add another item').first;
    await tester.ensureVisible(requestAddButton);
    await tester.pumpAndSettle();
    await tester.tap(requestAddButton);
    await tester.pumpAndSettle();
    expect(find.text('Item 1'), findsOneWidget);
    expect(find.text('Item 2'), findsOneWidget);

    await tester.tap(find.text('Donate'));
    await tester.pumpAndSettle();
    expect(find.text('Item 1'), findsOneWidget);
    final donationAddButton = find.text('Add another item').last;
    await tester.ensureVisible(donationAddButton);
    await tester.pumpAndSettle();
    await tester.tap(donationAddButton);
    await tester.pumpAndSettle();
    expect(find.text('Item 2'), findsOneWidget);
  });
}

class _TestAuthService extends AuthService {
  _TestAuthService(String phoneNumber)
      : _user = AuthUser(
          id: 'test-user',
          fullName: 'Test User',
          email: 'test@example.com',
          role: 'Citizen',
          phoneNumber: phoneNumber,
        );

  final AuthUser _user;

  @override
  AuthUser? get user => _user;

  @override
  String? get token => 'test-token';
}
