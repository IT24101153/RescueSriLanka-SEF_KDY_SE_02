import 'package:flutter/material.dart';

import '../../../shared/models/auth.dart';
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
  static const _navy = Color(0xFF14283F);
  static const _accent = Color(0xFFC4481C);

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

  Widget _summaryCard(String label, IconData icon, int? count) {
    return Card(
      margin: EdgeInsets.zero,
      elevation: 0,
      color: const Color(0xFFF5F7FA),
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: _accent, size: 28),
            const SizedBox(height: 16),
            Text(
              count?.toString() ?? '--',
              style: const TextStyle(
                color: _navy,
                fontSize: 30,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 4),
            Text(label, style: const TextStyle(color: _navy, fontSize: 15)),
          ],
        ),
      ),
    );
  }

  Widget _actionCard(String label, IconData icon, {VoidCallback? onTap}) {
    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      elevation: 0,
      color: Colors.white,
      clipBehavior: Clip.antiAlias,
      child: Semantics(
        button: true,
        child: InkWell(
          onTap: onTap ?? _showComingNext,
          child: Padding(
            padding: const EdgeInsets.all(20),
            child: Row(
              children: [
                Icon(icon, color: _accent, size: 30),
                const SizedBox(width: 16),
                Expanded(
                  child: Text(
                    label,
                    style: const TextStyle(
                      color: _navy,
                      fontSize: 17,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                const Icon(Icons.chevron_right, color: _navy),
              ],
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: _navy,
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 760),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Expanded(
                        child: Text(
                          'RescueSriLanka',
                          style: TextStyle(
                            color: Colors.white,
                            fontSize: 28,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                      IconButton(
                        onPressed: _isLoading ? null : _loadDashboard,
                        tooltip: 'Refresh overview',
                        icon: const Icon(Icons.refresh),
                        color: const Color(0xFFFFAD83),
                        disabledColor: const Color(0xFFD0DBE7),
                      ),
                      IconButton(
                        onPressed: widget.onLogout,
                        tooltip: 'Logout',
                        icon: const Icon(Icons.logout),
                        color: const Color(0xFFFFAD83),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    'Rescue Coordination Center',
                    style: TextStyle(color: Color(0xFFD0DBE7), fontSize: 18),
                  ),
                  const SizedBox(height: 24),
                  Card(
                    margin: EdgeInsets.zero,
                    elevation: 0,
                    color: Colors.white,
                    child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const Icon(Icons.badge_outlined, color: _accent),
                          const SizedBox(height: 12),
                          Text(
                            widget.session.user.fullName,
                            style: const TextStyle(
                              color: _navy,
                              fontSize: 20,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          const SizedBox(height: 6),
                          Text(
                            widget.session.user.email,
                            style: const TextStyle(
                              color: Color(0xFF46576B),
                              fontSize: 15,
                            ),
                          ),
                          const SizedBox(height: 12),
                          const Text(
                            'Emergency Coordinator',
                            style: TextStyle(
                              color: _accent,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 28),
                  const Text(
                    'Overview',
                    style: TextStyle(
                      color: Colors.white,
                      fontSize: 22,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 16),
                  if (_isLoading) ...[
                    const LinearProgressIndicator(
                      color: Color(0xFFFFAD83),
                      semanticsLabel: 'Loading overview',
                    ),
                    const SizedBox(height: 16),
                  ],
                  if (_errorMessage != null) ...[
                    Card(
                      margin: EdgeInsets.zero,
                      color: const Color(0xFFFFEDEA),
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Semantics(
                              liveRegion: true,
                              child: Text(
                                _errorMessage!,
                                style: const TextStyle(
                                  color: Color(0xFFB3261E),
                                ),
                              ),
                            ),
                            const SizedBox(height: 8),
                            TextButton.icon(
                              onPressed: _isLoading ? null : _loadDashboard,
                              icon: const Icon(Icons.refresh),
                              label: const Text('Retry'),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),
                  ],
                  LayoutBuilder(
                    builder: (context, constraints) {
                      final useTwoColumns =
                          constraints.maxWidth >= 320 &&
                          MediaQuery.textScalerOf(context).scale(15) <= 22;
                      final width = useTwoColumns
                          ? (constraints.maxWidth - 12) / 2
                          : constraints.maxWidth;
                      return Wrap(
                        spacing: 12,
                        runSpacing: 12,
                        children: [
                          for (final summary in [
                            (
                              'Available Teams',
                              Icons.groups_outlined,
                              _availableTeams,
                            ),
                            (
                              'Available Vehicles',
                              Icons.local_shipping_outlined,
                              _availableVehicles,
                            ),
                            (
                              'Proposed Assignments',
                              Icons.assignment_outlined,
                              _proposedAssignments,
                            ),
                            (
                              'Active Dispatches',
                              Icons.emergency_outlined,
                              _activeDispatches,
                            ),
                          ])
                            SizedBox(
                              width: width,
                              child: _summaryCard(
                                summary.$1,
                                summary.$2,
                                summary.$3,
                              ),
                            ),
                        ],
                      );
                    },
                  ),
                  const SizedBox(height: 28),
                  const Text(
                    'Coordination',
                    style: TextStyle(
                      color: Colors.white,
                      fontSize: 22,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 16),
                  _actionCard(
                    'Rescue Teams',
                    Icons.groups_outlined,
                    onTap: () =>
                        _openScreen(RescueTeamsScreen(session: widget.session)),
                  ),
                  _actionCard(
                    'Assignments',
                    Icons.assignment_outlined,
                    onTap: () =>
                        _openScreen(AssignmentsScreen(session: widget.session)),
                  ),
                  _actionCard(
                    'AI Safety Review',
                    Icons.verified_user_outlined,
                    onTap: () => _openScreen(
                      AiSafetyReviewScreen(session: widget.session),
                    ),
                  ),
                  _actionCard(
                    'Active Dispatches',
                    Icons.local_shipping_outlined,
                    onTap: () => _openScreen(
                      ActiveDispatchesScreen(session: widget.session),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
