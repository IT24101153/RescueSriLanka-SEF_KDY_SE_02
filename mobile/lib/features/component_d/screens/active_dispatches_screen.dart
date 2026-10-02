import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/widgets/app_ui.dart';
import '../models/coordination_requests.dart';
import '../models/dispatch_history.dart';
import '../rescue_style.dart';
import '../services/rescue_coordination_service.dart';
import 'assignments_screen.dart';
import 'coordination_forms.dart';

class ActiveDispatchesScreen extends StatefulWidget {
  const ActiveDispatchesScreen({
    super.key,
    required this.session,
    this.service,
  });
  final AuthSession session;
  final RescueCoordinationService? service;

  @override
  State<ActiveDispatchesScreen> createState() => _ActiveDispatchesScreenState();
}

class _ActiveDispatchesScreenState extends State<ActiveDispatchesScreen> {
  late final _service = widget.service ?? RescueCoordinationService();
  bool _showHistory = false;
  List<Map<String, dynamic>> _dispatches = [];
  bool _loading = false;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (_loading || _busy) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await _service.getDispatches(widget.session);
      if (!mounted) return;
      final dispatches = data.cast<Map<String, dynamic>>().toList();
      setState(() => _dispatches = dispatches);
    } catch (error) {
      if (mounted) setState(() => _error = coordinationError(error));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  List<DispatchTransition> _next(
    Map<String, dynamic> dispatch,
  ) => switch (dispatch['status']) {
    'Pending' => [
      if (dispatch['approvalStatus'] == 'Approved')
        DispatchTransition.dispatched,
      DispatchTransition.cancelled,
    ],
    'Dispatched' => [DispatchTransition.enRoute, DispatchTransition.cancelled],
    'EnRoute' => [DispatchTransition.onScene, DispatchTransition.cancelled],
    'OnScene' => [DispatchTransition.resolved, DispatchTransition.cancelled],
    _ => [],
  };

  Future<void> _transition(
    Map<String, dynamic> dispatch,
    DispatchTransition status,
  ) async {
    if (_busy || _loading || !_next(dispatch).contains(status)) return;
    setState(() => _busy = true);
    try {
      if (status == DispatchTransition.resolved ||
          status == DispatchTransition.cancelled) {
        if (!await confirmCoordinationAction(
          context,
          status == DispatchTransition.resolved
              ? 'Resolve this mission and release its resources?'
              : 'Cancel this mission and release its resources?',
        )) {
          return;
        }
      }
      if (!mounted) return;
      await _service.transitionDispatch(widget.session, dispatch['id'], status);
      if (!mounted) return;
      setState(() => _busy = false);
      await _load();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(mutationError(error))));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _actionLabel(DispatchTransition status) => switch (status) {
    DispatchTransition.dispatched => 'Mark dispatched',
    DispatchTransition.enRoute => 'Mark en route',
    DispatchTransition.onScene => 'Mark on scene',
    DispatchTransition.resolved => 'Resolve',
    DispatchTransition.cancelled => 'Cancel mission',
  };

  @override
  Widget build(BuildContext context) {
    final visible = _showHistory
        ? historyDispatches(_dispatches)
        : activeDispatches(_dispatches);
    return PopScope(
      canPop: !_busy,
      child: CoordinationPage(
        title: 'Dispatches',
        loading: _loading,
        busy: _busy,
        notice: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SegmentedButton<bool>(
              segments: const [
                ButtonSegment(value: false, label: Text('Active')),
                ButtonSegment(value: true, label: Text('History')),
              ],
              selected: {_showHistory},
              onSelectionChanged: (value) =>
                  setState(() => _showHistory = value.single),
            ),
            const SizedBox(height: AppSpacing.gap),
            if (_busy) const LinearProgressIndicator(),
          ],
        ),
        error: _error,
        onRefresh: _load,
        empty: !_loading && _error == null && visible.isEmpty,
        emptyMessage: _showHistory
            ? 'No completed dispatch history yet.'
            : 'No active dispatches.',
        children: [
          for (final dispatch in visible)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.gap),
              child: AppCard(
                accent: coordinationTone(
                  dispatch['status'] as String? ?? 'Unknown',
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    CoordinationField('Dispatch ID', dispatch['id']),
                    CoordinationStatus(
                      dispatch['status'] as String? ?? 'Unknown',
                    ),
                    CoordinationField('Assignment', dispatch['assignmentId']),
                    CoordinationField(
                      'Approval status',
                      dispatch['approvalStatus'],
                    ),
                    for (final field in const {
                      'approvedAt': 'Approved at',
                      'dispatchedAt': 'Dispatched at',
                      'enRouteAt': 'En route at',
                      'onSceneAt': 'On scene at',
                      'resolvedAt': 'Resolved at',
                      'cancelledAt': 'Cancelled at',
                    }.entries)
                      if (formatDispatchDateTime(dispatch[field.key])
                          case final String formatted)
                        CoordinationField(field.value, formatted),
                    if (dispatch['notes'] != null)
                      CoordinationField('Notes', dispatch['notes']),
                    const SizedBox(height: 6),
                    if (!isTerminalDispatch(dispatch))
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (final next in _next(dispatch))
                            OutlinedButton(
                              onPressed: _busy || _loading
                                  ? null
                                  : () => _transition(dispatch, next),
                              style: next == DispatchTransition.cancelled
                                  ? OutlinedButton.styleFrom(
                                      foregroundColor: AppColors.critical,
                                    )
                                  : null,
                              child: Text(_actionLabel(next)),
                            ),
                        ],
                      ),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}
