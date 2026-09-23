import 'package:flutter/material.dart';

import '../../../shared/models/auth.dart';
import '../models/coordination_requests.dart';
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
      FilledButton(
        onPressed: _canDecide(assignment)
            ? () => _decide(assignment, HumanDecision.approve)
            : null,
        child: const Text('Approve & Dispatch'),
      ),
      OutlinedButton(
        onPressed: _canDecide(assignment)
            ? () => _decide(assignment, HumanDecision.revise)
            : null,
        child: const Text('Revise Plan'),
      ),
      OutlinedButton(
        onPressed: _canDecide(assignment)
            ? () => _decide(assignment, HumanDecision.reject)
            : null,
        child: const Text('Reject Plan'),
      ),
      if (!_canDecide(assignment) && !_deciding)
        const Text(
          'Human decisions require a current approved validation workflow. Edit a proposed plan from Assignments, then validate again.',
        ),
    ],
  );

  Widget _result(Map<String, dynamic> result) => Semantics(
    liveRegion: true,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Divider(),
        const Text(
          'AI recommendation',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
        ),
        CoordinationStatus(result['decision'] as String),
        if (result['isStale'] == true)
          const Text(
            'Stale result — the plan has changed. Refresh before requesting a new review.',
            style: TextStyle(
              color: Color(0xFFB3261E),
              fontWeight: FontWeight.w700,
            ),
          ),
        CoordinationField('Workflow ID', result['workflowId']),
        CoordinationField('Assignment ID', result['assignmentId']),
        CoordinationField('Plan version', result['planVersion']),
        CoordinationField('Stale', result['isStale']),
        CoordinationField('Workflow status', result['workflowStatus']),
        CoordinationField('Summary', result['summary']),
        const SizedBox(height: 12),
        const Text(
          'Failed checks',
          style: TextStyle(fontWeight: FontWeight.w700),
        ),
        if ((result['failedChecks'] as List).isEmpty)
          const Text('None reported.'),
        for (final item in result['failedChecks'] as List)
          CoordinationField('•', item),
        const SizedBox(height: 12),
        const Text(
          'Suggested actions',
          style: TextStyle(fontWeight: FontWeight.w700),
        ),
        if ((result['suggestedActions'] as List).isEmpty)
          const Text('None reported.'),
        for (final item in result['suggestedActions'] as List)
          CoordinationField('•', item),
        const SizedBox(height: 12),
        const Text(
          'Validation checks',
          style: TextStyle(fontWeight: FontWeight.w700),
        ),
        if ((result['checks'] as List).isEmpty)
          const Text('No checks returned.'),
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
      title: 'AI Safety Review',
      loading: _loading,
      busy: _validatingId != null || _deciding,
      error: _error,
      onRefresh: _load,
      empty: _assignments.isEmpty,
      emptyMessage: 'No proposed assignments available for safety review.',
      notice: const Card(
        color: Color(0xFFFFEDE3),
        child: Padding(
          padding: EdgeInsets.all(16),
          child: Text(
            'AI recommendation only — human approval required.',
            style: TextStyle(
              color: Color(0xFF9C3E16),
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
      ),
      children: [
        for (final assignment in _assignments)
          Card(
            color: Colors.white,
            margin: const EdgeInsets.only(bottom: 16),
            child: Padding(
              padding: const EdgeInsets.all(20),
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
                  FilledButton(
                    onPressed:
                        _loading ||
                            _deciding ||
                            _validatingId != null ||
                            assignment['id'] is! String
                        ? null
                        : () => _validate(assignment['id'] as String),
                    style: FilledButton.styleFrom(
                      backgroundColor: const Color(0xFFC4481C),
                      foregroundColor: Colors.white,
                      minimumSize: const Size.fromHeight(52),
                    ),
                    child: const Text('Run AI Safety Validation'),
                  ),
                  if (_validatingId == assignment['id'])
                    const Padding(
                      padding: EdgeInsets.all(16),
                      child: Center(
                        child: CircularProgressIndicator(
                          semanticsLabel: 'Running AI safety validation',
                          color: Color(0xFFC4481C),
                        ),
                      ),
                    ),
                  if (_validationErrors[assignment['id']] != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 12),
                      child: Semantics(
                        liveRegion: true,
                        child: Text(
                          _validationErrors[assignment['id']]!,
                          style: const TextStyle(color: Color(0xFFB3261E)),
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
