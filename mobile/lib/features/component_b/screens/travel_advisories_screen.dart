import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
import '../services/help_request_service.dart';

/// The travel advisories in force, worst first, so someone asking for help can
/// see which areas are unsafe to move through.
class TravelAdvisoriesScreen extends StatefulWidget {
  const TravelAdvisoriesScreen({super.key});

  @override
  State<TravelAdvisoriesScreen> createState() => _TravelAdvisoriesScreenState();
}

class _TravelAdvisoriesScreenState extends State<TravelAdvisoriesScreen> {
  List<TravelAdvisory>? _advisories;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final result = await HelpRequestService.getActiveAdvisories();
    if (!mounted) return;
    setState(() {
      _advisories = result;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    final advisories = _advisories;

    return Scaffold(
      appBar: AppHeader(title: 'Travel advisories', loading: _loading),
      body: SafeArea(
        child: Column(
          children: [
            if (!_loading && advisories == null)
              AppErrorBanner(
                message: 'Could not load travel advisories.',
                onRetry: _load,
              ),
            Expanded(
              child: RefreshIndicator(
                onRefresh: _load,
                child: ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(
                    AppSpacing.gutter,
                    18,
                    AppSpacing.gutter,
                    28,
                  ),
                  children: [
                    const Text(
                      'Areas where moving around is risky right now. Avoid '
                      'travel through them unless you are told to evacuate.',
                      style: TextStyle(
                        fontSize: 13.5,
                        height: 1.45,
                        color: AppColors.body,
                      ),
                    ),
                    const SizedBox(height: 16),
                    if (!_loading && advisories != null && advisories.isEmpty)
                      const AppEmptyState(
                        icon: Icons.check_circle_outline,
                        title: 'No active advisories',
                        message:
                            'Nothing is currently flagged. That is not a '
                            'guarantee of safety; follow official warnings.',
                      ),
                    for (final advisory in advisories ?? const []) ...[
                      _AdvisoryCard(advisory: advisory),
                      const SizedBox(height: AppSpacing.gap),
                    ],
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

class _AdvisoryCard extends StatelessWidget {
  const _AdvisoryCard({required this.advisory});

  final TravelAdvisory advisory;

  @override
  Widget build(BuildContext context) {
    final tone = safetyLevelTone(advisory.safetyLevel);
    final radiusKm = advisory.radiusMeters / 1000;

    return AppCard(
      accent: tone,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(safetyLevelIcon(advisory.safetyLevel), size: 20, color: tone),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  advisory.areaName,
                  style: const TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 15,
                    color: AppColors.ink,
                  ),
                ),
              ),
              AppPill(safetyLevelLabel(advisory.safetyLevel), tone: tone),
            ],
          ),
          const SizedBox(height: 8),
          Text(advisory.reason, style: const TextStyle(fontSize: 14, height: 1.4)),
          const SizedBox(height: 8),
          Text(
            [
              'Within ${radiusKm.toStringAsFixed(radiusKm < 10 ? 1 : 0)} km',
              if (advisory.expiresAt != null)
                'until ${DateFormat('d MMM, HH:mm').format(advisory.expiresAt!)}',
            ].join(' · '),
            style: const TextStyle(fontSize: 12, color: AppColors.body),
          ),
        ],
      ),
    );
  }
}
