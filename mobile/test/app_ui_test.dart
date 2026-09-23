import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/shared/core/theme.dart';
import 'package:mobile/shared/widgets/app_ui.dart';

/// The kit's pieces are used inside scrolling lists on every section, where
/// the available height is unbounded. A widget that takes its height from its
/// parent throws there and blanks the whole screen, so each one is rendered
/// in a list here.
void main() {
  Future<void> pumpInList(WidgetTester tester, List<Widget> children) =>
      tester.pumpWidget(
        MaterialApp(
          theme: buildAppTheme(),
          home: Scaffold(body: ListView(children: children)),
        ),
      );

  testWidgets('an accented card lays out inside a list', (tester) async {
    await pumpInList(tester, const [
      AppCard(accent: AppColors.caution, child: Text('Accented')),
      AppCard(child: Text('Plain')),
    ]);

    expect(tester.takeException(), isNull);
    expect(find.text('Accented'), findsOneWidget);
    expect(find.text('Plain'), findsOneWidget);
  });

  testWidgets('the empty state, error banner and stat bar lay out too', (
    tester,
  ) async {
    await pumpInList(tester, [
      const AppStatBar(
        stats: [
          AppStat(value: '2', label: 'Requests'),
          AppStat(value: '1', label: 'Active', tone: AppColors.caution),
        ],
      ),
      AppErrorBanner(message: 'Something failed.', onRetry: () {}),
      const AppEmptyState(
        icon: Icons.inbox_outlined,
        title: 'Nothing yet',
        message: 'It will show up here.',
      ),
      AppPrimaryButton(label: 'Send', onPressed: () {}),
    ]);

    expect(tester.takeException(), isNull);
    expect(find.text('Requests'), findsOneWidget);
    expect(find.text('Something failed.'), findsOneWidget);
    expect(find.text('Nothing yet'), findsOneWidget);
    expect(find.text('Send'), findsOneWidget);
  });

  testWidgets('a busy button keeps its label', (tester) async {
    await pumpInList(tester, [
      const AppPrimaryButton(
        label: 'Uploading photo…',
        busy: true,
        onPressed: null,
      ),
    ]);

    expect(tester.takeException(), isNull);
    expect(find.text('Uploading photo…'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });
}
