import 'package:flutter/material.dart';

import '../models/auth_models.dart';
import '../services/rescue_coordination_service.dart';
import 'coordination_forms.dart';

class AssignmentsScreen extends StatefulWidget {
  const AssignmentsScreen({super.key, required this.session});
  final AuthSession session;

  @override
  State<AssignmentsScreen> createState() => _AssignmentsScreenState();
}

class _AssignmentsScreenState extends State<AssignmentsScreen> {
  final _service = RescueCoordinationService();
  List<Map<String, dynamic>> _assignments = [];
  bool _loading = false;
  bool _editing = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (_loading || _editing) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await _service.getAssignments(widget.session);
      if (!mounted) return;
      final assignments = data.cast<Map<String, dynamic>>().toList();
      setState(() => _assignments = assignments);
    } catch (error) {
      if (mounted) setState(() => _error = coordinationError(error));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _edit([Map<String, dynamic>? assignment]) async {
    if (_loading || _editing) return;
    setState(() => _editing = true);
    final changed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) =>
            AssignmentEditor(session: widget.session, existing: assignment),
      ),
    );
    if (!mounted) return;
    setState(() => _editing = false);
    if (changed == true) await _load();
  }

  @override
  Widget build(BuildContext context) => CoordinationPage(
    title: 'Assignments',
    loading: _loading,
    busy: _editing,
    notice: FilledButton.icon(
      onPressed: _loading || _editing ? null : () => _edit(),
      icon: const Icon(Icons.add),
      label: const Text('Create Assignment'),
    ),
    error: _error,
    onRefresh: _load,
    empty: _assignments.isEmpty,
    emptyMessage: 'No assignments available.',
    children: [
      for (final assignment in _assignments)
        AssignmentCard(
          assignment: assignment,
          onRevise: assignment['status'] == 'Proposed' && !_loading && !_editing
              ? () => _edit(assignment)
              : null,
        ),
    ],
  );
}

// Shared presentation for the three coordination detail screens.
String coordinationError(Object error) => error is RescueCoordinationException
    ? error.message
    : 'Unable to load rescue coordination data.';

class CoordinationPage extends StatelessWidget {
  const CoordinationPage({
    super.key,
    required this.title,
    required this.loading,
    required this.error,
    required this.onRefresh,
    required this.empty,
    required this.emptyMessage,
    required this.children,
    this.busy = false,
    this.notice,
  });
  final String title;
  final bool loading;
  final bool busy;
  final String? error;
  final VoidCallback onRefresh;
  final bool empty;
  final String emptyMessage;
  final List<Widget> children;
  final Widget? notice;

  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: const Color(0xFF14283F),
    appBar: AppBar(
      title: Text(title),
      backgroundColor: const Color(0xFF14283F),
      foregroundColor: Colors.white,
      actions: [
        IconButton(
          onPressed: loading || busy ? null : onRefresh,
          tooltip: 'Refresh',
          color: const Color(0xFFFFAD83),
          disabledColor: Colors.white54,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 760),
          child: ListView(
            padding: const EdgeInsets.all(20),
            children: [
              ?notice,
              if (loading)
                const Padding(
                  padding: EdgeInsets.all(32),
                  child: Center(
                    child: CircularProgressIndicator(
                      color: Color(0xFFFFAD83),
                      semanticsLabel: 'Loading data',
                    ),
                  ),
                )
              else if (error != null)
                Card(
                  color: const Color(0xFFFFEDEA),
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Semantics(
                          liveRegion: true,
                          child: Text(
                            error!,
                            style: const TextStyle(color: Color(0xFFB3261E)),
                          ),
                        ),
                        TextButton.icon(
                          onPressed: busy ? null : onRefresh,
                          icon: const Icon(Icons.refresh),
                          label: const Text('Retry'),
                        ),
                      ],
                    ),
                  ),
                )
              else if (empty)
                Padding(
                  padding: const EdgeInsets.all(24),
                  child: Text(
                    emptyMessage,
                    style: const TextStyle(color: Colors.white),
                  ),
                )
              else
                ...children,
            ],
          ),
        ),
      ),
    ),
  );
}

class CoordinationStatus extends StatelessWidget {
  const CoordinationStatus(this.status, {super.key});
  final String status;

  @override
  Widget build(BuildContext context) {
    final positive = [
      'APPROVE',
      'Approved',
      'PASS',
      'Resolved',
    ].contains(status);
    final negative = [
      'REJECT',
      'Rejected',
      'FAIL',
      'Cancelled',
    ].contains(status);
    final color = positive
        ? const Color(0xFF1B6B3A)
        : negative
        ? const Color(0xFFB3261E)
        : const Color(0xFF9C3E16);
    return Align(
      alignment: Alignment.centerLeft,
      child: Container(
        margin: const EdgeInsets.symmetric(vertical: 6),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: positive
              ? const Color(0xFFE8F5EB)
              : negative
              ? const Color(0xFFFFEDEA)
              : const Color(0xFFFFEDE3),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Text(
          status,
          style: TextStyle(color: color, fontWeight: FontWeight.w700),
        ),
      ),
    );
  }
}

class CoordinationField extends StatelessWidget {
  const CoordinationField(this.label, this.value, {super.key});
  final String label;
  final Object? value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 5),
    child: Text(
      '$label: ${value ?? 'Not provided'}',
      style: const TextStyle(color: Color(0xFF14283F)),
    ),
  );
}

class AssignmentCard extends StatelessWidget {
  const AssignmentCard({super.key, required this.assignment, this.onRevise});
  final Map<String, dynamic> assignment;
  final VoidCallback? onRevise;

  @override
  Widget build(BuildContext context) => Card(
    margin: const EdgeInsets.only(bottom: 16),
    color: Colors.white,
    clipBehavior: Clip.antiAlias,
    child: ExpansionTile(
      title: Text(
        assignment['rescueTeamName'] as String? ?? 'Assignment',
        style: const TextStyle(
          color: Color(0xFF14283F),
          fontWeight: FontWeight.w700,
        ),
      ),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CoordinationField('Assignment ID', assignment['id']),
          CoordinationStatus(assignment['status'] as String? ?? 'Unknown'),
        ],
      ),
      iconColor: const Color(0xFFC4481C),
      childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
      expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        CoordinationField('Team ID', assignment['rescueTeamId']),
        if (assignment['status'] == 'Proposed')
          TextButton.icon(
            onPressed: onRevise,
            icon: const Icon(Icons.edit_outlined),
            label: Text('Revise Assignment v${assignment['planVersion']}'),
          ),
        CoordinationField('Vehicle', assignment['vehiclePlateNumber']),
        if (assignment['vehicleId'] != null)
          CoordinationField('Vehicle ID', assignment['vehicleId']),
        CoordinationField('Required skill', assignment['requiredSkill']),
        CoordinationField('Required capacity', assignment['requiredCapacity']),
        CoordinationField('Plan version', assignment['planVersion']),
        CoordinationField('Assigned at', assignment['assignedAt']),
        if (assignment['incidentId'] != null)
          CoordinationField('Incident', assignment['incidentId']),
        if (assignment['helpRequestId'] != null)
          CoordinationField('Help request', assignment['helpRequestId']),
        if (assignment['dispatchId'] != null)
          CoordinationField('Dispatch', assignment['dispatchId']),
        if (assignment['notes'] != null)
          CoordinationField('Notes', assignment['notes']),
      ],
    ),
  );
}
