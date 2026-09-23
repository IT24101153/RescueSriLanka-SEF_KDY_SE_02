import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/widgets/app_ui.dart';
import '../rescue_style.dart';
import '../services/rescue_coordination_service.dart';
import 'assignments_screen.dart' show CoordinationStatus;
import 'coordination_forms.dart';

class RescueTeamsScreen extends StatefulWidget {
  const RescueTeamsScreen({super.key, required this.session});

  final AuthSession session;

  @override
  State<RescueTeamsScreen> createState() => _RescueTeamsScreenState();
}

class _RescueTeamsScreenState extends State<RescueTeamsScreen> {
  final _service = RescueCoordinationService();
  List<Map<String, dynamic>> _teams = [];
  bool _isLoading = false;
  bool _busy = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _loadTeams();
  }

  Future<void> _loadTeams() async {
    if (_isLoading || _busy) return;
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });
    try {
      final data = await _service.getRescueTeams(widget.session);
      if (!mounted) return;
      final teams = data.cast<Map<String, dynamic>>().toList();
      // Validate nested collections before rendering so malformed data is retryable.
      for (final team in teams) {
        for (final key in ['members', 'vehicles']) {
          (team[key] as List).cast<Map<String, dynamic>>().toList();
        }
      }
      setState(() => _teams = teams);
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _errorMessage = error is RescueCoordinationException
            ? error.message
            : 'Unable to load rescue teams.';
      });
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _edit(
    ResourceKind kind, {
    String? teamId,
    Map<String, dynamic>? existing,
  }) async {
    if (_busy || _isLoading) return;
    setState(() => _busy = true);
    final changed = await showDialog<bool>(
      context: context,
      barrierDismissible: false,
      builder: (_) => ResourceEditor(
        session: widget.session,
        kind: kind,
        teamId: teamId,
        existing: existing,
      ),
    );
    if (!mounted) return;
    setState(() => _busy = false);
    if (changed == true) await _loadTeams();
  }

  Future<void> _delete(ResourceKind kind, String id, {String? teamId}) async {
    if (_busy || _isLoading) return;
    setState(() => _busy = true);
    try {
      if (!await confirmCoordinationAction(
        context,
        'Delete this ${kind.name}?',
      )) {
        return;
      }
      if (!mounted) return;
      switch (kind) {
        case ResourceKind.team:
          await _service.deleteTeam(widget.session, id);
        case ResourceKind.member:
          await _service.deleteMember(widget.session, teamId!, id);
        case ResourceKind.vehicle:
          await _service.deleteVehicle(widget.session, teamId!, id);
      }
      if (!mounted) return;
      setState(() => _busy = false);
      await _loadTeams();
    } catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(mutationError(error))));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _resourceActions(
    ResourceKind kind,
    Map<String, dynamic> item, {
    String? teamId,
  }) => Wrap(
    spacing: 8,
    children: [
      TextButton.icon(
        onPressed: _busy || _isLoading
            ? null
            : () => _edit(kind, teamId: teamId, existing: item),
        icon: const Icon(Icons.edit_outlined),
        label: Text('Edit ${kind.name}'),
      ),
      TextButton.icon(
        onPressed: _busy || _isLoading
            ? null
            : () => _delete(kind, item['id'], teamId: teamId),
        style: TextButton.styleFrom(foregroundColor: AppColors.critical),
        icon: const Icon(Icons.delete_outline),
        label: Text('Delete ${kind.name}'),
      ),
    ],
  );

  String _label(dynamic value) => coordinationLabel(value);

  /// One member or vehicle inside a team: what it is, and what state it is in.
  Widget _detail(String title, String subtitle, String status) {
    return Padding(
      padding: const EdgeInsets.only(top: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: const TextStyle(
              color: AppColors.ink,
              fontWeight: FontWeight.w600,
              fontSize: 14,
            ),
          ),
          const SizedBox(height: 3),
          Text(
            subtitle,
            style: const TextStyle(color: AppColors.body, fontSize: 12.5),
          ),
          CoordinationStatus(status),
        ],
      ),
    );
  }

  Widget _teamCard(Map<String, dynamic> team) {
    final members = (team['members'] as List).cast<Map<String, dynamic>>();
    final vehicles = (team['vehicles'] as List).cast<Map<String, dynamic>>();
    final latitude = team['baseLatitude'];
    final longitude = team['baseLongitude'];
    final status = team['status'] is String
        ? team['status'] as String
        : 'Unknown';

    return AppCard(
      accent: coordinationTone(status),
      padding: EdgeInsets.zero,
      // The card draws the border, so the tile's own dividers go.
      child: Theme(
        data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
        child: ExpansionTile(
          key: PageStorageKey(team['id']),
          tilePadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
          childrenPadding: const EdgeInsets.fromLTRB(14, 0, 14, 14),
          expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
          iconColor: AppColors.body,
          collapsedIconColor: AppColors.body,
          title: Text(
            team['name'] is String ? team['name'] as String : 'Unnamed team',
            style: const TextStyle(
              color: AppColors.ink,
              fontSize: 16,
              fontWeight: FontWeight.w700,
            ),
          ),
          subtitle: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              CoordinationStatus(status),
              Text(
                '${members.length} members · ${vehicles.length} vehicles',
                style: const TextStyle(color: AppColors.body, fontSize: 12.5),
              ),
              if (latitude is num && longitude is num)
                Text(
                  'Base: $latitude, $longitude',
                  style: const TextStyle(color: AppColors.body, fontSize: 12.5),
                ),
            ],
          ),
          children: [
            const Divider(color: AppColors.border, height: 20),
            _resourceActions(ResourceKind.team, team),
            Wrap(
              spacing: 8,
              children: [
                TextButton.icon(
                  onPressed: _busy
                      ? null
                      : () => _edit(ResourceKind.member, teamId: team['id']),
                  icon: const Icon(Icons.person_add_alt, size: 18),
                  label: const Text('Add member'),
                ),
                TextButton.icon(
                  onPressed: _busy
                      ? null
                      : () => _edit(ResourceKind.vehicle, teamId: team['id']),
                  icon: const Icon(Icons.add, size: 18),
                  label: const Text('Add vehicle'),
                ),
              ],
            ),
            const SizedBox(height: 8),
            const AppSectionTitle('Team members'),
            if (members.isEmpty)
              const Text(
                'No team members listed.',
                style: TextStyle(color: AppColors.body, fontSize: 13),
              ),
            for (final member in members) ...[
              _detail(
                member['fullName'] is String
                    ? member['fullName'] as String
                    : 'Unnamed member',
                'Skill: ${_label(member['skill'])}',
                member['isAvailable'] == true
                    ? 'Available'
                    : member['isAvailable'] == false
                    ? 'Unavailable'
                    : 'Unknown',
              ),
              _resourceActions(ResourceKind.member, member, teamId: team['id']),
            ],
            const Divider(color: AppColors.border, height: 24),
            const AppSectionTitle('Vehicles'),
            if (vehicles.isEmpty)
              const Text(
                'No vehicles listed.',
                style: TextStyle(color: AppColors.body, fontSize: 13),
              ),
            for (final vehicle in vehicles) ...[
              _detail(
                vehicle['plateNumber'] is String
                    ? vehicle['plateNumber'] as String
                    : 'Registration not provided',
                '${_label(vehicle['type'])} · Capacity: ${vehicle['capacity'] is num ? vehicle['capacity'] : 'Not provided'}',
                vehicle['status'] is String
                    ? vehicle['status'] as String
                    : 'Unknown',
              ),
              _resourceActions(
                ResourceKind.vehicle,
                vehicle,
                teamId: team['id'],
              ),
            ],
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: !_busy,
      child: Scaffold(
        appBar: AppBar(
          titleSpacing: 16,
          title: const Text(
            'Rescue teams',
            style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
          ),
          actions: [
            IconButton(
              onPressed: _busy || _isLoading
                  ? null
                  : () => _edit(ResourceKind.team),
              tooltip: 'Add team',
              icon: const Icon(Icons.add),
            ),
            IconButton(
              onPressed: _isLoading || _busy ? null : _loadTeams,
              tooltip: 'Refresh teams',
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        bottomNavigationBar: _busy ? const LinearProgressIndicator() : null,
        body: SafeArea(
          child: Column(
            children: [
              if (_errorMessage != null)
                Semantics(
                  liveRegion: true,
                  child: AppErrorBanner(
                    message: _errorMessage!,
                    onRetry: _busy ? null : _loadTeams,
                  ),
                ),
              Expanded(
                child: _isLoading
                    ? const Center(
                        child: CircularProgressIndicator(
                          semanticsLabel: 'Loading rescue teams',
                        ),
                      )
                    : _teams.isEmpty
                    ? const AppEmptyState(
                        icon: Icons.groups_outlined,
                        title: 'No rescue teams yet',
                        message:
                            'Add a team to give assignments somewhere to go.',
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.fromLTRB(
                          AppSpacing.gutter,
                          16,
                          AppSpacing.gutter,
                          28,
                        ),
                        itemCount: _teams.length,
                        separatorBuilder: (_, _) =>
                            const SizedBox(height: AppSpacing.gap),
                        itemBuilder: (_, index) => _teamCard(_teams[index]),
                      ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
