import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/models/auth.dart';
import '../../../shared/widgets/app_ui.dart';
import '../services/rescue_coordination_service.dart';

/// The Rescue tab for signed-in users who don't run rescue operations. It
/// answers two questions in plain language: are rescue teams ready, and what
/// can each team do. Read-only, and backed by an
/// endpoint that never returns member or dispatch details.
class RescueOverviewScreen extends StatefulWidget {
  const RescueOverviewScreen({super.key, required this.session, this.service});

  final AuthSession session;

  /// Overridable so tests can supply canned data.
  final RescueCoordinationService? service;

  @override
  State<RescueOverviewScreen> createState() => _RescueOverviewScreenState();
}

enum _Filter { all, available, onMission }

class _RescueOverviewScreenState extends State<RescueOverviewScreen> {
  late final RescueCoordinationService _service =
      widget.service ?? RescueCoordinationService();
  Map<String, dynamic>? _data;
  String? _error;
  bool _loading = true;
  _Filter _filter = _Filter.all;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final data = await _service.getRescueOverview(widget.session);
      if (!mounted) return;
      setState(() => _data = data);
    } on RescueCoordinationException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  List<Map<String, dynamic>> _teams() {
    final all = (_data?['teams'] as List<dynamic>? ?? const [])
        .whereType<Map<String, dynamic>>()
        .toList();
    final shown = all.where((t) {
      final status = t['status'] as String? ?? '';
      return switch (_filter) {
        _Filter.all => true,
        _Filter.available => status == 'Available',
        _Filter.onMission => status == 'OnMission',
      };
    }).toList();
    // Teams that can help right now come first.
    int rank(Map<String, dynamic> t) => switch (t['status']) {
      'Available' => 0,
      'OnMission' => 1,
      _ => 2,
    };
    shown.sort((a, b) => rank(a).compareTo(rank(b)));
    return shown;
  }

  @override
  Widget build(BuildContext context) {
    final data = _data;

    return Scaffold(
      appBar: AppHeader(title: 'Rescue', loading: _loading),
      body: RefreshIndicator(
        onRefresh: _load,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.gutter,
            AppSpacing.gap,
            AppSpacing.gutter,
            AppSpacing.gutter * 2,
          ),
          children: [
            if (_error != null) ...[
              AppErrorBanner(message: _error!, onRetry: _load),
              const SizedBox(height: AppSpacing.gap),
            ],
            if (data == null && _loading)
              const Padding(
                padding: EdgeInsets.only(top: 80),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (data != null) ...[
              _ReadinessCard(data: data),
              const SizedBox(height: AppSpacing.gap * 2),
              AppSectionTitle(
                'Rescue teams',
                trailing: Text(
                  'Pull down to refresh',
                  style: const TextStyle(fontSize: 11.5, color: AppColors.body),
                ),
              ),
              _FilterChips(
                selected: _filter,
                onChanged: (f) => setState(() => _filter = f),
              ),
              const SizedBox(height: AppSpacing.gap),
              ..._teamList(),
            ] else if (_error == null)
              const AppEmptyState(
                icon: Icons.groups_outlined,
                title: 'Nothing to show',
              ),
          ],
        ),
      ),
    );
  }

  List<Widget> _teamList() {
    final teams = _teams();
    if (teams.isEmpty) {
      final none = (_data?['teams'] as List<dynamic>? ?? const []).isEmpty;
      return [
        AppEmptyState(
          icon: Icons.groups_outlined,
          title: none ? 'No rescue teams yet' : 'No teams match this filter',
          message: none
              ? 'Teams will appear here once they are set up.'
              : 'Try choosing "All".',
        ),
      ];
    }
    return [
      for (final team in teams)
        Padding(
          padding: const EdgeInsets.only(bottom: AppSpacing.gap),
          child: _TeamCard(team: team),
        ),
    ];
  }
}

/// The headline: one sentence a stressed reader can take in at a glance.
class _ReadinessCard extends StatelessWidget {
  const _ReadinessCard({required this.data});

  final Map<String, dynamic> data;

  @override
  Widget build(BuildContext context) {
    final available = (data['teamsAvailable'] as num? ?? 0).toInt();
    final onMission = (data['teamsOnMission'] as num? ?? 0).toInt();
    final dispatches = (data['activeDispatches'] as num? ?? 0).toInt();
    final total = (data['teams'] as List<dynamic>? ?? const []).length;

    final (Color tone, IconData icon, String headline, String detail) =
        available > 0
        ? (
            AppColors.safe,
            Icons.verified_outlined,
            available == 1
                ? '1 rescue team is ready'
                : '$available rescue teams are ready',
            'Teams are standing by and can be sent out.',
          )
        : total > 0
        ? (
            AppColors.high,
            Icons.hourglass_top_outlined,
            'All teams are busy right now',
            'Every team is on a mission or off duty.',
          )
        : (
            AppColors.body,
            Icons.groups_outlined,
            'No rescue teams set up yet',
            'Check back soon.',
          );

    return Container(
      decoration: BoxDecoration(
        color: tone.withValues(alpha: 0.08),
        border: Border.all(color: tone.withValues(alpha: 0.35)),
        borderRadius: AppSpacing.radius,
      ),
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(AppSpacing.card + 2),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(icon, color: tone, size: 30),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        headline,
                        style: const TextStyle(
                          fontSize: 17,
                          fontWeight: FontWeight.w700,
                          color: AppColors.ink,
                        ),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        detail,
                        style: const TextStyle(color: AppColors.body),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          AppStatBar(
            stats: [
              AppStat(
                value: '$available',
                label: 'Ready',
                tone: AppColors.safe,
              ),
              AppStat(
                value: '$onMission',
                label: 'On a mission',
                tone: AppColors.high,
              ),
              AppStat(value: '$dispatches', label: 'Active call-outs'),
            ],
          ),
        ],
      ),
    );
  }
}

class _FilterChips extends StatelessWidget {
  const _FilterChips({required this.selected, required this.onChanged});

  final _Filter selected;
  final ValueChanged<_Filter> onChanged;

  static const _labels = {
    _Filter.all: 'All',
    _Filter.available: 'Ready',
    _Filter.onMission: 'On a mission',
  };

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 8,
      children: [
        for (final f in _Filter.values)
          ChoiceChip(
            label: Text(_labels[f]!),
            selected: selected == f,
            onSelected: (_) => onChanged(f),
            selectedColor: AppColors.brand.withValues(alpha: 0.25),
            showCheckmark: false,
          ),
      ],
    );
  }
}

class _TeamCard extends StatelessWidget {
  const _TeamCard({required this.team});

  final Map<String, dynamic> team;

  static Color _tone(String status) => switch (status) {
    'Available' => AppColors.safe,
    'OnMission' => AppColors.high,
    _ => AppColors.body,
  };

  static String _statusLabel(String status) => switch (status) {
    'Available' => 'Ready',
    'OnMission' => 'On a mission',
    'OffDuty' => 'Off duty',
    _ => status,
  };

  /// "WaterRescue" -> "Water rescue".
  static String _words(String camel) {
    final spaced = camel.replaceAllMapped(
      RegExp(r'(?<=[a-z])(?=[A-Z])'),
      (_) => ' ',
    );
    if (spaced.isEmpty) return spaced;
    return spaced[0].toUpperCase() + spaced.substring(1).toLowerCase();
  }

  static IconData _vehicleIcon(String type) => switch (type) {
    'Ambulance' => Icons.local_hospital_outlined,
    'Boat' => Icons.directions_boat_outlined,
    'FireTruck' => Icons.fire_truck_outlined,
    'FourByFour' => Icons.directions_car_outlined,
    'Truck' => Icons.local_shipping_outlined,
    _ => Icons.directions_car_outlined,
  };

  @override
  Widget build(BuildContext context) {
    final status = team['status'] as String? ?? '';
    final members = (team['memberCount'] as num? ?? 0).toInt();
    final freeMembers = (team['availableMemberCount'] as num? ?? 0).toInt();
    final skills = (team['skills'] as List<dynamic>? ?? const [])
        .map((s) => _words(s.toString()))
        .toList();
    final vehicles = (team['vehicles'] as List<dynamic>? ?? const [])
        .whereType<Map<String, dynamic>>()
        .toList();
    final tone = _tone(status);

    return AppCard(
      accent: tone,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  team['name'] as String? ?? 'Rescue team',
                  style: const TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w700,
                    color: AppColors.ink,
                  ),
                ),
              ),
              AppPill(_statusLabel(status), tone: tone),
            ],
          ),
          const SizedBox(height: 10),
          if (members > 0) ...[
            ClipRRect(
              borderRadius: BorderRadius.circular(999),
              child: LinearProgressIndicator(
                value: freeMembers / members,
                minHeight: 6,
                color: tone,
                backgroundColor: AppColors.border,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              '$freeMembers of $members members free',
              style: const TextStyle(fontSize: 12.5, color: AppColors.body),
            ),
          ] else
            const Text(
              'No members listed yet',
              style: TextStyle(fontSize: 12.5, color: AppColors.body),
            ),
          if (vehicles.isNotEmpty) ...[
            const SizedBox(height: 10),
            Wrap(
              spacing: 14,
              runSpacing: 6,
              children: [
                for (final v in vehicles)
                  Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        _vehicleIcon(v['type'] as String? ?? ''),
                        size: 18,
                        color: AppColors.body,
                      ),
                      const SizedBox(width: 4),
                      Text(
                        '${v['count']} ${_words(v['type'] as String? ?? '')}',
                        style: const TextStyle(
                          fontSize: 13,
                          color: AppColors.ink,
                        ),
                      ),
                    ],
                  ),
              ],
            ),
          ],
          if (skills.isNotEmpty) ...[
            const SizedBox(height: 10),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [for (final s in skills) AppPill(s)],
            ),
          ],
        ],
      ),
    );
  }
}
