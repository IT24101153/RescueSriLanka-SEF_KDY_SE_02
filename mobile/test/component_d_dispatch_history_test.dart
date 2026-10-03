import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/component_d/models/dispatch_history.dart';

void main() {
  const statuses = [
    'Pending',
    'Dispatched',
    'EnRoute',
    'OnScene',
    'Resolved',
    'Cancelled',
  ];
  for (final status in statuses) {
    test('$status belongs to the correct view', () {
      final item = {'id': status, 'status': status};
      final terminal = status == 'Resolved' || status == 'Cancelled';
      expect(isTerminalDispatch(item), terminal);
      expect(activeDispatches([item]), terminal ? isEmpty : [item]);
      expect(historyDispatches([item]), terminal ? [item] : isEmpty);
    });
  }
  test('splits all six statuses without changing the original list', () {
    final items = statuses
        .map((s) => <String, dynamic>{'id': s, 'status': s})
        .toList();
    final original = List<Map<String, dynamic>>.of(items);
    expect(activeDispatches(items).map((d) => d['status']), statuses.take(4));
    expect(historyDispatches(items).map((d) => d['status']), [
      'Cancelled',
      'Resolved',
    ]);
    expect(items, original);
  });
  test('history uses the appropriate terminal date newest first', () {
    final items = [
      {
        'id': 'old',
        'status': 'Resolved',
        'resolvedAt': '2026-09-01T10:00:00Z',
        'cancelledAt': '2026-10-01T10:00:00Z',
      },
      {
        'id': 'new',
        'status': 'Cancelled',
        'cancelledAt': '2026-09-02T10:00:00Z',
      },
    ];
    expect(historyDispatches(items).map((d) => d['id']), ['new', 'old']);
    expect(items.first['id'], 'old');
  });
  test('missing terminal dates fall back to existing milestones', () {
    final item = <String, dynamic>{
      'status': 'Resolved',
      'resolvedAt': 'broken',
      'onSceneAt': '2026-09-01T10:00:00Z',
    };
    expect(terminalTimestamp(item), DateTime.utc(2026, 9, 1, 10));
    item.remove('onSceneAt');
    item['dispatchedAt'] = '2026-08-01T10:00:00Z';
    expect(terminalTimestamp(item), DateTime.utc(2026, 8, 1, 10));
  });
  test('missing or malformed dates sort last without throwing', () {
    final items = <Map<String, dynamic>>[
      {'id': 'missing', 'status': 'Resolved'},
      {'id': 'bad', 'status': 'Cancelled', 'cancelledAt': 42},
      {
        'id': 'dated',
        'status': 'Resolved',
        'approvedAt': '2026-09-01T10:00:00Z',
      },
    ];
    expect(historyDispatches(items).map((d) => d['id']), [
      'dated',
      'bad',
      'missing',
    ]);
    expect(terminalTimestamp(items[0]), isNull);
  });
  test('equal terminal dates are ordered deterministically by ID', () {
    final items = ['b', 'a'].map(
      (id) => <String, dynamic>{
        'id': id,
        'status': 'Resolved',
        'resolvedAt': '2026-09-01T10:00:00Z',
      },
    );
    expect(historyDispatches(items).map((d) => d['id']), ['a', 'b']);
  });
  test('formats a valid timestamp in local time', () {
    final local = DateTime(2026, 9, 1, 10, 15);
    final raw = local.toUtc().toIso8601String();
    expect(formatDispatchDateTime(raw), '2026-09-01 10:15');
    expect(formatDispatchDateTime(raw), isNot(contains('T10:15')));
  });
  test('missing and malformed timestamp formatting returns no date', () {
    for (final value in [null, '', 'not-a-date', 123, <String, dynamic>{}]) {
      expect(formatDispatchDateTime(value), isNull);
    }
  });
}
