import 'package:flutter/material.dart';

import '../models/auth_models.dart';
import '../models/coordination_requests.dart';
import '../services/rescue_coordination_service.dart';
import 'assignments_screen.dart';
import 'coordination_forms.dart';

class ActiveDispatchesScreen extends StatefulWidget {
  const ActiveDispatchesScreen({super.key, required this.session});
  final AuthSession session;

  @override
  State<ActiveDispatchesScreen> createState() => _ActiveDispatchesScreenState();
}

class _ActiveDispatchesScreenState extends State<ActiveDispatchesScreen> {
  final _service = RescueCoordinationService();
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
      final dispatches = data
          .cast<Map<String, dynamic>>()
          .where(
            (item) =>
                item['status'] != 'Resolved' && item['status'] != 'Cancelled',
          )
          .toList();
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
    DispatchTransition.dispatched => 'Mark Dispatched',
    DispatchTransition.enRoute => 'Mark En Route',
    DispatchTransition.onScene => 'Mark On Scene',
    DispatchTransition.resolved => 'Resolve',
    DispatchTransition.cancelled => 'Cancel mission',
  };

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy,
    child: CoordinationPage(
      title: 'Active Dispatches',
      loading: _loading,
      busy: _busy,
      notice: _busy ? const LinearProgressIndicator() : null,
      error: _error,
      onRefresh: _load,
      empty: _dispatches.isEmpty,
      emptyMessage: 'No active dispatches.',
      children: [
        for (final dispatch in _dispatches)
          Card(
            color: Colors.white,
            margin: const EdgeInsets.only(bottom: 16),
            child: Padding(
              padding: const EdgeInsets.all(20),
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
                  }.entries)
                    if (dispatch[field.key] != null)
                      CoordinationField(field.value, dispatch[field.key]),
                  if (dispatch['notes'] != null)
                    CoordinationField('Notes', dispatch['notes']),
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    children: [
                      for (final next in _next(dispatch))
                        OutlinedButton(
                          onPressed: _busy || _loading
                              ? null
                              : () => _transition(dispatch, next),
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
