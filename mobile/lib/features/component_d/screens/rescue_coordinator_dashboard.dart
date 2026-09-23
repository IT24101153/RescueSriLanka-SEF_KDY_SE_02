import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/widgets/app_ui.dart';
import '../services/rescue_coordination_service.dart';
import 'rescue_teams_screen.dart';
import 'assignments_screen.dart';
import 'ai_safety_review_screen.dart';
import 'active_dispatches_screen.dart';

class RescueCoordinatorDashboard extends StatefulWidget {
  const RescueCoordinatorDashboard({
    super.key,
    required this.session,
    required this.onLogout,
  });

  final AuthSession session;

  /// Signing out is the app shell's business; the tab shows the sign-in
  /// prompt again once the session is gone.
  final VoidCallback onLogout;

  @override
  State<RescueCoordinatorDashboard> createState() =>
      _RescueCoordinatorDashboardState();
}

class _RescueCoordinatorDashboardState
    extends State<RescueCoordinatorDashboard> {
  Future<void> _openScreen(Widget screen) async {
    await Navigator.of(context)
        .push(MaterialPageRoute<void>(builder: (_) => screen));
    if (!mounted) return;
    // If the initial load is still finishing, ensure a fresh load follows it.
    if (_isLoading) {
      _refreshAfterLoad = true;
    } else {
      await _loadDashboard();
    }
  }

  bool _refreshAfterLoad = false;

  final _service = RescueCoordinationService();
  bool _isLoading = false;
  String? _errorMessage;
  int? _availableTeams;
  int? _availableVehicles;
  int? _proposedAssignments;
  int? _activeDispatches;

  @override
  void initState() {
    super.initState();
    _loadDashboard();
  }

  String _status(dynamic item) {
    if (item is! Map<String, dynamic> || item['status'] is! String) {
      throw const FormatException('Missing resource status.');
    }
    return item['status'] as String;
  }

  Future<void> _loadDashboard() async {
    if (_isLoading) return;
    setState(() {
      _isLoading = true;
      _errorMessage = null;
      _availableTeams = null;
      _availableVehicles = null;
      _proposedAssignments = null;
      _activeDispatches = null;
    });

    try {
      final resources = await Future.wait([
        _service.getRescueTeams(widget.session),
        _service.getAssignments(widget.session),
        _service.getDispatches(widget.session),
      ]);
      if (!mounted) return;

      final teams = resources[0];
      final availableTeams = teams
          .where((team) => _status(team) == 'Available')
          .length;
      var availableVehicles = 0;
      for (final team in teams) {
        final vehicles = team['vehicles'];
        if (vehicles is! List) {
          throw const FormatException('Missing team vehicles.');
        }
        availableVehicles += vehicles
            .where((vehicle) => _status(vehicle) == 'Available')
            .length;
      }
      final proposedAssignments = resources[1]
          .where((assignment) => _status(assignment) == 'Proposed')
          .length;
      final activeDispatches = resources[2].where((dispatch) {
        final status = _status(dispatch);
        return status != 'Resolved' && status != 'Cancelled';
      }).length;

      setState(() {
        _availableTeams = availableTeams;
        _availableVehicles = availableVehicles;
        _proposedAssignments = proposedAssignments;
        _activeDispatches = activeDispatches;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _errorMessage = error is RescueCoordinationException
            ? error.message
            : 'Unable to load rescue coordination data.';
      });
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
    if (mounted && _refreshAfterLoad) {
      _refreshAfterLoad = false;
      await _loadDashboard();
    }
  }

  void _showComingNext() {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(const SnackBar(content: Text('Coming next')));
  }

  /// One count from the overview. A dash stands for "not loaded yet".
  Widget _summaryCard(String label, IconData icon, int? count, Color tone) {
    return AppCard(
      accent: count == null || count == 0 ? null : tone,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: AppColors.body, size: 20),
          const SizedBox(height: 12),
          Text(
            count?.toString() ?? '–',
            style: const TextStyle(
              color: AppColors.ink,
              fontSize: 26,
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            label,
            style: const TextStyle(color: AppColors.body, fontSize: 12.5),
          ),
        ],
      ),
    );
  }

  Widget _actionCard(String label, IconData icon, {VoidCallback? onTap}) {
    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.gap),
      child: AppCard(
        onTap: onTap ?? _showComingNext,
        child: Row(
          children: [
            Container(
              width: 42,
              height: 42,
              decoration: BoxDecoration(
                color: AppColors.brand.withValues(alpha: 0.14),
                borderRadius: BorderRadius.circular(11),
              ),
              child: Icon(icon, color: AppColors.brandInk, size: 21),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                label,
                style: const TextStyle(
                  color: AppColors.ink,
                  fontSize: 15,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
            const SizedBox(width: 8),
            const Icon(Icons.chevron_right, color: AppColors.body),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Rescue',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        actions: [
          IconButton(
            onPressed: _isLoading ? null : _loadDashboard,
            tooltip: 'Refresh overview',
            icon: const Icon(Icons.refresh),
          ),
          IconButton(
            onPressed: widget.onLogout,
            tooltip: 'Log out',
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (_errorMessage != null)
              Semantics(
                liveRegion: true,
                child: AppErrorBanner(
                  message: _errorMessage!,
                  onRetry: _isLoading ? null : _loadDashboard,
                ),
              ),
            if (_isLoading)
              const LinearProgressIndicator(
                semanticsLabel: 'Loading overview',
                minHeight: 2,
              ),
            Expanded(
              child: RefreshIndicator(
                onRefresh: _loadDashboard,
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(
                    AppSpacing.gutter,
                    18,
                    AppSpacing.gutter,
                    28,
                  ),
                  children: [
                    const Text(
                      'Rescue coordination centre',
                      style: TextStyle(
                        fontSize: 23,
                        fontWeight: FontWeight.w700,
                        color: AppColors.ink,
                        letterSpacing: -0.4,
                      ),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      '${widget.session.user.fullName} · '
                      '${widget.session.user.email}',
                      style: const TextStyle(
                        fontSize: 13,
                        height: 1.4,
                        color: AppColors.body,
                      ),
                    ),
                    const SizedBox(height: 20),
                    const AppSectionTitle('Overview'),
                    LayoutBuilder(
                      builder: (context, constraints) {
                        final useTwoColumns =
                            constraints.maxWidth >= 320 &&
                            MediaQuery.textScalerOf(context).scale(15) <= 22;
                        final width = useTwoColumns
                            ? (constraints.maxWidth - AppSpacing.gap) / 2
                            : constraints.maxWidth;
                        return Wrap(
                          spacing: AppSpacing.gap,
                          runSpacing: AppSpacing.gap,
                          children: [
                            for (final summary in [
                              (
                                'Available teams',
                                Icons.groups_outlined,
                                _availableTeams,
                                AppColors.safe,
                              ),
                              (
                                'Available vehicles',
                                Icons.local_shipping_outlined,
                                _availableVehicles,
                                AppColors.safe,
                              ),
                              (
                                'Proposed assignments',
                                Icons.assignment_outlined,
                                _proposedAssignments,
                                AppColors.caution,
                              ),
                              (
                                'Active dispatches',
                                Icons.emergency_outlined,
                                _activeDispatches,
                                AppColors.ink,
                              ),
                            ])
                              SizedBox(
                                width: width,
                                child: _summaryCard(
                                  summary.$1,
                                  summary.$2,
                                  summary.$3,
                                  summary.$4,
                                ),
                              ),
                          ],
                        );
                      },
                    ),
                    const SizedBox(height: 26),
                    const AppSectionTitle('Coordination'),
                    _actionCard(
                      'Rescue teams',
                      Icons.groups_outlined,
                      onTap: () => _openScreen(
                        RescueTeamsScreen(session: widget.session),
                      ),
                    ),
                    _actionCard(
                      'Assignments',
                      Icons.assignment_outlined,
                      onTap: () => _openScreen(
                        AssignmentsScreen(session: widget.session),
                      ),
                    ),
                    _actionCard(
                      'AI safety review',
                      Icons.verified_user_outlined,
                      onTap: () => _openScreen(
                        AiSafetyReviewScreen(session: widget.session),
                      ),
                    ),
                    _actionCard(
                      'Active dispatches',
                      Icons.local_shipping_outlined,
                      onTap: () => _openScreen(
                        ActiveDispatchesScreen(session: widget.session),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
