import 'package:flutter/material.dart';

import '../../../shared/models/auth.dart';
import '../services/rescue_coordination_service.dart';
import 'coordination_forms.dart';

class RescueTeamsScreen extends StatefulWidget {
  const RescueTeamsScreen({super.key, required this.session});

  final AuthSession session;

  @override
  State<RescueTeamsScreen> createState() => _RescueTeamsScreenState();
}

class _RescueTeamsScreenState extends State<RescueTeamsScreen> {
  static const _navy = Color(0xFF14283F);
  static const _accent = Color(0xFFC4481C);
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
        icon: const Icon(Icons.delete_outline),
        label: Text('Delete ${kind.name}'),
      ),
    ],
  );

  String _label(dynamic value) {
    if (value is! String || value.isEmpty) return 'Not provided';
    return value.replaceAllMapped(
      RegExp(r'([a-z])([A-Z])'),
      (match) => '${match[1]} ${match[2]}',
    );
  }

  Widget _statusBadge(String status) {
    final positive = status == 'Available';
    final active = status == 'OnMission' || status == 'InUse';
    final foreground = positive
        ? const Color(0xFF1B6B3A)
        : active
        ? _accent
        : const Color(0xFF46576B);
    final background = positive
        ? const Color(0xFFE8F5EB)
        : active
        ? const Color(0xFFFFEDE3)
        : const Color(0xFFEDF0F4);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Text(
        _label(status),
        style: TextStyle(color: foreground, fontWeight: FontWeight.w600),
      ),
    );
  }

  Widget _detail(String title, String subtitle, String status) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: const TextStyle(color: _navy, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 4),
          Text(subtitle, style: const TextStyle(color: Color(0xFF46576B))),
          const SizedBox(height: 8),
          _statusBadge(status),
        ],
      ),
    );
  }

  Widget _teamCard(Map<String, dynamic> team) {
    final members = (team['members'] as List).cast<Map<String, dynamic>>();
    final vehicles = (team['vehicles'] as List).cast<Map<String, dynamic>>();
    final latitude = team['baseLatitude'];
    final longitude = team['baseLongitude'];
    return Card(
      color: Colors.white,
      margin: EdgeInsets.zero,
      clipBehavior: Clip.antiAlias,
      child: ExpansionTile(
        key: PageStorageKey(team['id']),
        tilePadding: const EdgeInsets.all(16),
        childrenPadding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
        iconColor: _accent,
        collapsedIconColor: _accent,
        title: Text(
          team['name'] is String ? team['name'] as String : 'Unnamed team',
          style: const TextStyle(
            color: _navy,
            fontSize: 18,
            fontWeight: FontWeight.w700,
          ),
        ),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _statusBadge(
                team['status'] is String ? team['status'] as String : 'Unknown',
              ),
              const SizedBox(height: 10),
              Text('${members.length} members · ${vehicles.length} vehicles'),
              if (latitude is num && longitude is num) ...[
                const SizedBox(height: 6),
                Text('Base coordinates: $latitude, $longitude'),
              ],
            ],
          ),
        ),
        children: [
          const Divider(),
          _resourceActions(ResourceKind.team, team),
          Wrap(
            spacing: 8,
            children: [
              TextButton.icon(
                onPressed: _busy
                    ? null
                    : () => _edit(ResourceKind.member, teamId: team['id']),
                icon: const Icon(Icons.person_add_alt),
                label: const Text('Add Member'),
              ),
              TextButton.icon(
                onPressed: _busy
                    ? null
                    : () => _edit(ResourceKind.vehicle, teamId: team['id']),
                icon: const Icon(Icons.add),
                label: const Text('Add Vehicle'),
              ),
            ],
          ),
          const Text(
            'Team Members',
            style: TextStyle(
              color: _navy,
              fontSize: 17,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (members.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 12),
              child: Text('No team members listed.'),
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
          const Divider(),
          const Text(
            'Vehicles',
            style: TextStyle(
              color: _navy,
              fontSize: 17,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (vehicles.isEmpty)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 12),
              child: Text('No vehicles listed.'),
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
            _resourceActions(ResourceKind.vehicle, vehicle, teamId: team['id']),
          ],
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: !_busy,
      child: Scaffold(
        backgroundColor: _navy,
        appBar: AppBar(
          title: const Text('Rescue Teams'),
          backgroundColor: _navy,
          foregroundColor: Colors.white,
          actions: [
            IconButton(
              onPressed: _busy || _isLoading
                  ? null
                  : () => _edit(ResourceKind.team),
              tooltip: 'Add Team',
              icon: const Icon(Icons.add),
            ),
            IconButton(
              onPressed: _isLoading || _busy ? null : _loadTeams,
              tooltip: 'Refresh teams',
              icon: const Icon(Icons.refresh),
              color: const Color(0xFFFFAD83),
              disabledColor: const Color(0xFFD0DBE7),
            ),
          ],
        ),
        bottomNavigationBar: _busy ? const LinearProgressIndicator() : null,
        body: SafeArea(
          child: _isLoading
              ? const Center(
                  child: CircularProgressIndicator(
                    color: Color(0xFFFFAD83),
                    semanticsLabel: 'Loading rescue teams',
                  ),
                )
              : _errorMessage != null
              ? SingleChildScrollView(
                  padding: const EdgeInsets.all(24),
                  child: Card(
                    color: const Color(0xFFFFEDEA),
                    child: Padding(
                      padding: const EdgeInsets.all(20),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Semantics(
                            liveRegion: true,
                            child: Text(
                              _errorMessage!,
                              style: const TextStyle(color: Color(0xFFB3261E)),
                            ),
                          ),
                          const SizedBox(height: 12),
                          TextButton.icon(
                            onPressed: _loadTeams,
                            icon: const Icon(Icons.refresh),
                            label: const Text('Retry'),
                          ),
                        ],
                      ),
                    ),
                  ),
                )
              : _teams.isEmpty
              ? const Center(
                  child: Padding(
                    padding: EdgeInsets.all(24),
                    child: Text(
                      'No rescue teams available.',
                      style: TextStyle(color: Colors.white),
                    ),
                  ),
                )
              : Center(
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 760),
                    child: ListView.separated(
                      padding: const EdgeInsets.all(20),
                      itemCount: _teams.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 16),
                      itemBuilder: (_, index) => _teamCard(_teams[index]),
                    ),
                  ),
                ),
        ),
      ),
    );
  }
}
