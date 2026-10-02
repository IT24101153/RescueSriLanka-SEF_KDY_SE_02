bool isTerminalDispatch(Map<String, dynamic> dispatch) =>
    dispatch['status'] == 'Resolved' || dispatch['status'] == 'Cancelled';

List<Map<String, dynamic>> activeDispatches(
  Iterable<Map<String, dynamic>> dispatches,
) => dispatches.where((item) => !isTerminalDispatch(item)).toList();

DateTime? _parseTimestamp(Object? value) =>
    value is String ? DateTime.tryParse(value) : null;

DateTime? terminalTimestamp(Map<String, dynamic> dispatch) {
  final terminalKey = dispatch['status'] == 'Resolved'
      ? 'resolvedAt'
      : 'cancelledAt';
  for (final key in [
    terminalKey,
    'onSceneAt',
    'enRouteAt',
    'dispatchedAt',
    'approvedAt',
  ]) {
    final timestamp = _parseTimestamp(dispatch[key]);
    if (timestamp != null) return timestamp;
  }
  return null;
}

List<Map<String, dynamic>> historyDispatches(
  Iterable<Map<String, dynamic>> dispatches,
) {
  final history = dispatches.where(isTerminalDispatch).toList();
  history.sort((a, b) {
    final aTime = terminalTimestamp(a);
    final bTime = terminalTimestamp(b);
    final byTime = aTime == null
        ? (bTime == null ? 0 : 1)
        : (bTime == null ? -1 : bTime.compareTo(aTime));
    return byTime != 0
        ? byTime
        : '${a['id'] ?? ''}'.compareTo('${b['id'] ?? ''}');
  });
  return history;
}

/// Missing or malformed values produce no milestone rather than a fake date.
String? formatDispatchDateTime(Object? value) {
  final timestamp = _parseTimestamp(value);
  if (timestamp == null) return null;
  final local = timestamp.toLocal();
  String pad(int number) => number.toString().padLeft(2, '0');
  return '${local.year}-${pad(local.month)}-${pad(local.day)} '
      '${pad(local.hour)}:${pad(local.minute)}';
}
