import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/shared/core/theme.dart';
import 'package:mobile/shared/models/auth.dart';
import 'package:mobile/features/component_d/models/coordination_requests.dart';
import 'package:mobile/features/component_d/screens/active_dispatches_screen.dart';
import 'package:mobile/features/component_d/services/rescue_coordination_service.dart';

class FakeDispatchService extends RescueCoordinationService {
  FakeDispatchService(this.items);
  List<Map<String, dynamic>> items;
  int loads = 0;
  int mutations = 0;
  bool failLoad = false;
  bool failMutation = false;
  Completer<List<dynamic>>? pending;

  @override
  Future<List<dynamic>> getDispatches(AuthSession session) async {
    loads++;
    if (failLoad) {
      throw const RescueCoordinationException('Unable to load dispatches.');
    }
    if (pending != null) return pending!.future;
    return items.map((item) => Map<String, dynamic>.of(item)).toList();
  }

  @override
  Future<Map<String, dynamic>> transitionDispatch(
    AuthSession s,
    String id,
    DispatchTransition status, {
    String? notes,
  }) async {
    mutations++;
    if (failMutation) {
      throw const RescueCoordinationException('Transition rejected.');
    }
    final item = items.singleWhere((d) => d['id'] == id);
    item['status'] = status == DispatchTransition.resolved
        ? 'Resolved'
        : 'Cancelled';
    item[status == DispatchTransition.resolved ? 'resolvedAt' : 'cancelledAt'] =
        '2026-09-29T10:00:00Z';
    return Map<String, dynamic>.of(item);
  }
}

Map<String, dynamic> dispatch(String id, String status) => {
  'id': id,
  'assignmentId': 'assignment-$id',
  'status': status,
  'approvalStatus': 'Approved',
  'notes': 'Mission $id',
};

final session = AuthSession(
  token: 'test-only',
  expiresAt: DateTime(2100),
  user: const AuthUser(
    id: 'coordinator',
    fullName: 'Coordinator',
    email: 'test@example.lk',
    role: 'EmergencyCoordinator',
  ),
);

Future<void> mount(
  WidgetTester tester,
  FakeDispatchService service, {
  bool settle = true,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: buildAppTheme(),
      home: ActiveDispatchesScreen(session: session, service: service),
    ),
  );
  if (settle) await tester.pumpAndSettle();
}

Future<void> history(WidgetTester tester) async {
  await tester.tap(find.text('History'));
  await tester.pumpAndSettle();
}

Finder reference(String id) =>
    find.text('Dispatch ID: $id', findRichText: true);

void main() {
  testWidgets('defaults to Active and excludes terminal records', (
    tester,
  ) async {
    final service = FakeDispatchService([
      dispatch('active', 'OnScene'),
      dispatch('resolved', 'Resolved'),
      dispatch('cancelled', 'Cancelled'),
    ]);
    await mount(tester, service);
    expect(
      tester
          .widget<SegmentedButton<bool>>(find.byType(SegmentedButton<bool>))
          .selected,
      {false},
    );
    expect(reference('active'), findsOneWidget);
    expect(reference('resolved'), findsNothing);
    expect(reference('cancelled'), findsNothing);
  });

  testWidgets('History shows only terminal records without mutation buttons', (
    tester,
  ) async {
    await mount(
      tester,
      FakeDispatchService([
        dispatch('active', 'OnScene'),
        dispatch('resolved', 'Resolved'),
        dispatch('cancelled', 'Cancelled'),
      ]),
    );
    await history(tester);
    expect(reference('resolved'), findsOneWidget);
    expect(reference('cancelled'), findsOneWidget);
    expect(reference('active'), findsNothing);
    expect(find.byType(OutlinedButton), findsNothing);
  });

  testWidgets('empty Active still allows switching to populated History', (
    tester,
  ) async {
    await mount(tester, FakeDispatchService([dispatch('done', 'Resolved')]));
    expect(find.text('No active dispatches.'), findsOneWidget);
    await history(tester);
    expect(reference('done'), findsOneWidget);
  });

  testWidgets('empty History has its own message and selector remains usable', (
    tester,
  ) async {
    await mount(tester, FakeDispatchService([dispatch('active', 'OnScene')]));
    await history(tester);
    expect(find.text('No completed dispatch history yet.'), findsOneWidget);
    await tester.tap(find.text('Active'));
    await tester.pumpAndSettle();
    expect(reference('active'), findsOneWidget);
  });

  testWidgets('loading shows progress and no empty state', (tester) async {
    final service = FakeDispatchService([])
      ..pending = Completer<List<dynamic>>();
    await mount(tester, service, settle: false);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    expect(find.text('No active dispatches.'), findsNothing);
    expect(find.text('History'), findsOneWidget);
    service.pending!.complete([]);
    await tester.pumpAndSettle();
    expect(find.text('No active dispatches.'), findsOneWidget);
  });

  testWidgets('load error has Retry without misleading empty message', (
    tester,
  ) async {
    final service = FakeDispatchService([])..failLoad = true;
    await mount(tester, service);
    expect(find.text('Unable to load dispatches.'), findsOneWidget);
    expect(find.text('No active dispatches.'), findsNothing);
    service.failLoad = false;
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();
    expect(service.loads, 2);
    expect(find.text('No active dispatches.'), findsOneWidget);
  });

  testWidgets('Refresh reloads full data for both views', (tester) async {
    final service = FakeDispatchService([]);
    await mount(tester, service);
    service.items = [
      dispatch('active', 'OnScene'),
      dispatch('done', 'Resolved'),
    ];
    await tester.tap(find.byTooltip('Refresh'));
    await tester.pumpAndSettle();
    expect(service.loads, 2);
    expect(reference('active'), findsOneWidget);
    await history(tester);
    expect(reference('done'), findsOneWidget);
  });

  for (final action in ['Resolve', 'Cancel mission']) {
    testWidgets(
      '$action moves record to History after confirmation and reload',
      (tester) async {
        final service = FakeDispatchService([dispatch('mission', 'OnScene')]);
        await mount(tester, service);
        await tester.tap(find.text(action));
        await tester.pump(const Duration(milliseconds: 300));
        expect(find.byType(LinearProgressIndicator), findsOneWidget);
        expect(find.text('Confirm action'), findsOneWidget);
        expect(service.mutations, 0);
        await tester.tap(find.text('Confirm'));
        await tester.pumpAndSettle();
        expect(service.loads, 2);
        expect(service.mutations, 1);
        expect(reference('mission'), findsNothing);
        await history(tester);
        expect(reference('mission'), findsOneWidget);
        expect(find.byType(OutlinedButton), findsNothing);
      },
    );
  }

  testWidgets('dismissed confirmation does not mutate or remove a dispatch', (
    tester,
  ) async {
    final service = FakeDispatchService([dispatch('mission', 'OnScene')]);
    await mount(tester, service);
    await tester.tap(find.text('Resolve'));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(service.mutations, 0);
    expect(reference('mission'), findsOneWidget);
  });

  testWidgets('mutation failure preserves the active record and shows error', (
    tester,
  ) async {
    final service = FakeDispatchService([dispatch('mission', 'OnScene')])
      ..failMutation = true;
    await mount(tester, service);
    await tester.tap(find.text('Resolve'));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.tap(find.text('Confirm'));
    await tester.pumpAndSettle();
    expect(reference('mission'), findsOneWidget);
    expect(find.text('Transition rejected.'), findsOneWidget);
  });

  testWidgets('Pending only offers dispatch when approved', (tester) async {
    final item = dispatch('pending', 'Pending')
      ..['approvalStatus'] = 'PendingApproval';
    final service = FakeDispatchService([item]);
    await mount(tester, service);
    expect(find.text('Mark dispatched'), findsNothing);
    expect(find.text('Cancel mission'), findsOneWidget);
    item['approvalStatus'] = 'Approved';
    await tester.tap(find.byTooltip('Refresh'));
    await tester.pumpAndSettle();
    expect(find.text('Mark dispatched'), findsOneWidget);
  });

  testWidgets('history formats milestones and skips absent or invalid dates', (
    tester,
  ) async {
    final item = dispatch('done', 'Resolved')
      ..addAll({
        'approvedAt': '2026-09-01T10:00:00Z',
        'resolvedAt': '2026-09-02T10:00:00Z',
        'onSceneAt': 'bad',
      });
    await mount(tester, FakeDispatchService([item]));
    await history(tester);
    expect(
      find.textContaining('Approved at:', findRichText: true),
      findsOneWidget,
    );
    expect(
      find.textContaining('Resolved at:', findRichText: true),
      findsOneWidget,
    );
    expect(
      find.textContaining('On scene at:', findRichText: true),
      findsNothing,
    );
    expect(
      find.textContaining('Cancelled at:', findRichText: true),
      findsNothing,
    );
    expect(find.textContaining('T10:00:00Z', findRichText: true), findsNothing);
  });

  testWidgets('narrow mobile layout renders both views without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 700);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await mount(
      tester,
      FakeDispatchService([
        dispatch('active', 'OnScene'),
        dispatch('done', 'Cancelled'),
      ]),
    );
    expect(tester.takeException(), isNull);
    await history(tester);
    expect(tester.takeException(), isNull);
    expect(reference('done'), findsOneWidget);
  });
}
