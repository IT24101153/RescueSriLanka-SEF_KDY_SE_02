import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/widgets/app_ui.dart';
import '../models/coordination_requests.dart';
import '../rescue_style.dart';
import '../services/rescue_coordination_service.dart';
import 'assignments_screen.dart';
import 'coordination_forms.dart';

class AiSafetyReviewScreen extends StatefulWidget {
  const AiSafetyReviewScreen({super.key, required this.session});
  final AuthSession session;

  @override
  State<AiSafetyReviewScreen> createState() => _AiSafetyReviewScreenState();
}

class _AiSafetyReviewScreenState extends State<AiSafetyReviewScreen> {
  final _service = RescueCoordinationService();
  List<Map<String, dynamic>> _assignments = [];
  final Map<String, Map<String, dynamic>> _results = {};
  final Map<String, String> _validationErrors = {};
  bool _loading = false;
  bool _deciding = false;
  String? _validatingId;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (_loading || _validatingId != null || _deciding) return;
    setState(() {
      _loading = true;
      _error = null;
      _results.clear();
      _validationErrors.clear();
    });
    try {
      final data = await _service.getAssignments(widget.session);
      if (!mounted) return;
      final assignments = data
          .cast<Map<String, dynamic>>()
          .where((item) => item['status'] == 'Proposed')
          .toList();
      setState(() => _assignments = assignments);
    } catch (error) {
      if (mounted) setState(() => _error = coordinationError(error));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _validate(String id) async {
    if (_loading || _validatingId != null || _deciding) return;
    setState(() {
      _validatingId = id;
      _results.remove(id);
      _validationErrors.remove(id);
    });
    try {
      // Invoked only by the coordinator's explicit button tap.
      final result = await _service.validateAssignment(widget.session, id);
      if (!mounted) return;
      if (result['assignmentId'] != id ||
          result['decision'] is! String ||
          result['isStale'] is! bool ||
          result['checks'] is! List ||
          result['failedChecks'] is! List ||
          result['suggestedActions'] is! List) {
        throw const FormatException('Invalid validation result.');
      }
      for (final check in result['checks'] as List) {
        if (check is! Map<String, dynamic> || check['passed'] is! bool) {
          throw const FormatException('Invalid validation check.');
        }
      }
      setState(() => _results[id] = result);
    } catch (error) {
      if (mounted) {
        setState(
          () => _validationErrors[id] = error is RescueCoordinationException
              ? error.message
              : 'Unable to read the AI safety validation result.',
        );
      }
    } finally {
      if (mounted) setState(() => _validatingId = null);
    }
  }

  bool _canDecide(Map<String, dynamic> assignment) {
    final result = _results[assignment['id']];
    // The backend requires an approved, current workflow for ALL decisions.
    return !_loading &&
        !_deciding &&
        _validatingId == null &&
        result != null &&
        result['assignmentId'] == assignment['id'] &&
        assignment['planVersion'] is int &&
        result['planVersion'] == assignment['planVersion'] &&
        result['isStale'] == false &&
        result['decision'] == 'APPROVE' &&
        result['workflowId'] is String &&
        (result['workflowId'] as String).isNotEmpty &&
        result['workflowStatus'] == 'AwaitingApproval' &&
        (result['checks'] as List).length == 10 &&
        (result['checks'] as List).every((check) => check['passed'] == true);
  }

  Future<void> _decide(
    Map<String, dynamic> assignment,
    HumanDecision decision,
  ) async {
    if (!_canDecide(assignment)) return;
    final id = assignment['id'] as String;
    final result = _results[id]!;
    setState(() => _deciding = true);
    var completed = false;
    try {
      final message = decision == HumanDecision.approve
          ? 'Approve this AI-recommended plan and dispatch resources?'
          : decision == HumanDecision.reject
          ? 'Reject this plan?'
          : 'Request revision of this plan?';
      if (!await confirmCoordinationAction(context, message)) return;
      if (!mounted) return;
      final response = await _service.decideAssignment(
        widget.session,
        id,
        workflowId: result['workflowId'],
        planVersion: result['planVersion'],
        decision: decision,
      );
      if (!mounted) return;
      if (response['success'] != true) {
        throw const RescueCoordinationException(
          'The decision was not accepted. Refresh and revalidate the plan.',
        );
      }
      completed = true;
      setState(() {
        _results.remove(id);
        _validationErrors.remove(id);
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            decision == HumanDecision.approve
                ? 'Plan approved and resources dispatched.'
                : 'Decision recorded.',
          ),
        ),
      );
      if (decision == HumanDecision.revise) {
        await Navigator.of(context).push<bool>(
          MaterialPageRoute(
            builder: (_) =>
                AssignmentEditor(session: widget.session, existing: assignment),
          ),
        );
      }
    } catch (error) {
      if (mounted) {
        setState(() {
          _results.remove(id);
          _validationErrors[id] = mutationError(error);
        });
      }
    } finally {
      if (mounted) setState(() => _deciding = false);
    }
    if (mounted && completed) await _load();
  }

  Widget _humanActions(Map<String, dynamic> assignment) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const SizedBox(height: 16),
      AppPrimaryButton(
        label: 'Approve & dispatch',
        icon: Icons.check,
        onPressed: _canDecide(assignment)
            ? () => _decide(assignment, HumanDecision.approve)
            : null,
      ),
      const SizedBox(height: 8),
      OutlinedButton(
        onPressed: _canDecide(assignment)
            ? () => _decide(assignment, HumanDecision.revise)
            : null,
        child: const Text('Revise plan'),
      ),
      const SizedBox(height: 8),
      OutlinedButton(
        onPressed: _canDecide(assignment)
            ? () => _decide(assignment, HumanDecision.reject)
            : null,
        style: OutlinedButton.styleFrom(foregroundColor: AppColors.critical),
        child: const Text('Reject plan'),
      ),
      if (!_canDecide(assignment) && !_deciding) ...[
        const SizedBox(height: 10),
        const Text(
          'Human decisions require a current approved validation workflow. '
          'Edit a proposed plan from Assignments, then validate again.',
          style: TextStyle(fontSize: 12, height: 1.4, color: AppColors.body),
        ),
      ],
    ],
  );

  Widget _result(Map<String, dynamic> result) => Semantics(
    liveRegion: true,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Divider(color: AppColors.border, height: 24),
        const AppSectionTitle('AI recommendation'),
        CoordinationStatus(result['decision'] as String),
        if (result['isStale'] == true)
          const Text(
            'Stale result — the plan has changed. Refresh before requesting a '
            'new review.',
            style: TextStyle(
              color: AppColors.critical,
              fontWeight: FontWeight.w700,
              fontSize: 12.5,
              height: 1.4,
            ),
          ),
        CoordinationField('Workflow ID', result['workflowId']),
        CoordinationField('Assignment ID', result['assignmentId']),
        CoordinationField('Plan version', result['planVersion']),
        CoordinationField('Stale', result['isStale']),
        CoordinationField('Workflow status', result['workflowStatus']),
        CoordinationField('Summary', result['summary']),
        const SizedBox(height: 12),
        const AppSectionTitle('Failed checks'),
        if ((result['failedChecks'] as List).isEmpty)
          const Text(
            'None reported.',
            style: TextStyle(color: AppColors.body, fontSize: 13),
          ),
        for (final item in result['failedChecks'] as List)
          CoordinationField('•', item),
        const SizedBox(height: 12),
        const AppSectionTitle('Suggested actions'),
        if ((result['suggestedActions'] as List).isEmpty)
          const Text(
            'None reported.',
            style: TextStyle(color: AppColors.body, fontSize: 13),
          ),
        for (final item in result['suggestedActions'] as List)
          CoordinationField('•', item),
        const SizedBox(height: 12),
        const AppSectionTitle('Validation checks'),
        if ((result['checks'] as List).isEmpty)
          const Text(
            'No checks returned.',
            style: TextStyle(color: AppColors.body, fontSize: 13),
          ),
        for (final check in result['checks'] as List) ...[
          CoordinationField('Check', check['name']),
          CoordinationStatus(check['passed'] == true ? 'PASS' : 'FAIL'),
          CoordinationField('Reason', check['reason']),
        ],
      ],
    ),
  );

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_deciding,
    child: CoordinationPage(
      title: 'AI safety review',
      loading: _loading,
      busy: _validatingId != null || _deciding,
      error: _error,
      onRefresh: _load,
      empty: _assignments.isEmpty,
      emptyMessage:
          'Proposed assignments waiting on a safety review appear here.',
      notice: const Padding(
        padding: EdgeInsets.only(bottom: AppSpacing.gap),
        child: AppCard(
          accent: AppColors.caution,
          child: Row(
            children: [
              Icon(Icons.gavel_outlined, size: 18, color: AppColors.caution),
              SizedBox(width: 10),
              Expanded(
                child: Text(
                  'AI recommendation only — human approval required.',
                  style: TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 13,
                    height: 1.35,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
      children: [
        for (final assignment in _assignments)
          Padding(
            padding: const EdgeInsets.only(bottom: AppSpacing.gap),
            child: AppCard(
              accent: coordinationTone(
                assignment['status'] as String? ?? 'Unknown',
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  CoordinationField('Assignment ID', assignment['id']),
                  CoordinationStatus(
                    assignment['status'] as String? ?? 'Unknown',
                  ),
                  CoordinationField('Team', assignment['rescueTeamName']),
                  CoordinationField(
                    'Vehicle',
                    assignment['vehiclePlateNumber'],
                  ),
                  CoordinationField(
                    'Required skill',
                    assignment['requiredSkill'],
                  ),
                  CoordinationField(
                    'Required capacity',
                    assignment['requiredCapacity'],
                  ),
                  CoordinationField('Plan version', assignment['planVersion']),
                  const SizedBox(height: 12),
                  AppPrimaryButton(
                    label: 'Run AI safety validation',
                    icon: Icons.verified_user_outlined,
                    busy: _validatingId == assignment['id'],
                    onPressed:
                        _loading ||
                            _deciding ||
                            _validatingId != null ||
                            assignment['id'] is! String
                        ? null
                        : () => _validate(assignment['id'] as String),
                  ),
                  if (_validationErrors[assignment['id']] != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 12),
                      child: Semantics(
                        liveRegion: true,
                        child: Text(
                          _validationErrors[assignment['id']]!,
                          style: const TextStyle(
                            color: AppColors.critical,
                            fontSize: 12.5,
                            height: 1.4,
                          ),
                        ),
                      ),
                    ),
                  if (_results[assignment['id']] != null)
                    _result(_results[assignment['id']]!),
                  if (_results[assignment['id']] != null)
                    _humanActions(assignment),
                  if (_deciding) const LinearProgressIndicator(),
                ],
              ),
            ),
          ),
      ],
    ),
  );
}
