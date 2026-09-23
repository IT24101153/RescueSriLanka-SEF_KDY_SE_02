import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/widgets/app_ui.dart';
import '../rescue_style.dart';
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
    notice: Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.gap),
      child: AppPrimaryButton(
        label: 'Create assignment',
        icon: Icons.add,
        onPressed: _loading || _editing ? null : () => _edit(),
      ),
    ),
    error: _error,
    onRefresh: _load,
    empty: _assignments.isEmpty,
    emptyMessage: 'Assignments you create appear here.',
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

/// The frame every coordination screen uses: the section's app bar, a refresh,
/// and one of loading / error / empty / content.
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
    appBar: AppBar(
      titleSpacing: 16,
      title: Text(
        title,
        style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
      ),
      actions: [
        IconButton(
          onPressed: loading || busy ? null : onRefresh,
          tooltip: 'Refresh',
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: SafeArea(
      child: Column(
        children: [
          if (error != null)
            Semantics(
              liveRegion: true,
              child: AppErrorBanner(
                message: error!,
                onRetry: busy ? null : onRefresh,
              ),
            ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.gutter,
                16,
                AppSpacing.gutter,
                28,
              ),
              children: [
                ?notice,
                if (loading)
                  const Padding(
                    padding: EdgeInsets.all(32),
                    child: Center(
                      child: CircularProgressIndicator(
                        semanticsLabel: 'Loading data',
                      ),
                    ),
                  )
                else if (empty)
                  AppEmptyState(
                    icon: Icons.inbox_outlined,
                    title: 'Nothing here yet',
                    message: emptyMessage,
                  )
                else
                  ...children,
              ],
            ),
          ),
        ],
      ),
    ),
  );
}

/// A state, as a pill: the tone carries it and the words say it.
class CoordinationStatus extends StatelessWidget {
  const CoordinationStatus(this.status, {super.key});
  final String status;

  @override
  Widget build(BuildContext context) {
    final tone = coordinationTone(status);

    return Align(
      alignment: Alignment.centerLeft,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 6),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(
            color: tone.withValues(alpha: 0.12),
            borderRadius: BorderRadius.circular(999),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(coordinationIcon(status), size: 13, color: tone),
              const SizedBox(width: 5),
              Text(
                coordinationLabel(status),
                style: TextStyle(
                  color: tone,
                  fontWeight: FontWeight.w700,
                  fontSize: 11.5,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// One labelled value inside a coordination card.
class CoordinationField extends StatelessWidget {
  const CoordinationField(this.label, this.value, {super.key});
  final String label;
  final Object? value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: RichText(
      text: TextSpan(
        style: const TextStyle(
          fontSize: 13,
          height: 1.35,
          color: AppColors.ink,
        ),
        children: [
          TextSpan(
            text: '$label: ',
            style: const TextStyle(color: AppColors.body),
          ),
          TextSpan(text: '${value ?? 'Not provided'}'),
        ],
      ),
    ),
  );
}

class AssignmentCard extends StatelessWidget {
  const AssignmentCard({super.key, required this.assignment, this.onRevise});
  final Map<String, dynamic> assignment;
  final VoidCallback? onRevise;

  @override
  Widget build(BuildContext context) {
    final status = assignment['status'] as String? ?? 'Unknown';

    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.gap),
      child: AppCard(
        accent: coordinationTone(status),
        padding: EdgeInsets.zero,
        // The card draws the border, so the tile's own dividers go.
        child: Theme(
          data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
          child: ExpansionTile(
            title: Text(
              assignment['rescueTeamName'] as String? ?? 'Assignment',
              style: const TextStyle(
                color: AppColors.ink,
                fontWeight: FontWeight.w700,
                fontSize: 15,
              ),
            ),
            subtitle: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                CoordinationField('Assignment ID', assignment['id']),
                CoordinationStatus(status),
              ],
            ),
            iconColor: AppColors.body,
            collapsedIconColor: AppColors.body,
            tilePadding: const EdgeInsets.symmetric(horizontal: 14),
            childrenPadding: const EdgeInsets.fromLTRB(14, 0, 14, 14),
            expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              CoordinationField('Team ID', assignment['rescueTeamId']),
              if (assignment['status'] == 'Proposed')
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    onPressed: onRevise,
                    icon: const Icon(Icons.edit_outlined, size: 18),
                    label: Text(
                      'Revise assignment v${assignment['planVersion']}',
                    ),
                  ),
                ),
              CoordinationField('Vehicle', assignment['vehiclePlateNumber']),
              if (assignment['vehicleId'] != null)
                CoordinationField('Vehicle ID', assignment['vehicleId']),
              CoordinationField('Required skill', assignment['requiredSkill']),
              CoordinationField(
                'Required capacity',
                assignment['requiredCapacity'],
              ),
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
        ),
      ),
    );
  }
}
