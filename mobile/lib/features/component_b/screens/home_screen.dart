import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/services/auth_service.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
import '../services/help_request_api.dart';
import '../../../shared/screens/auth/login_screen.dart';
import 'submit_request_screen.dart';
import 'my_requests_screen.dart';
import 'my_requests_map_screen.dart';
import 'emergency_contacts_screen.dart';
import 'safety_check_screen.dart';
import 'travel_advisories_screen.dart';
import '../services/help_request_service.dart';

/// The Help tab in the app shell: the help-request hub when signed in, a
/// sign-in prompt otherwise. A fresh [HomeScreen] is built on every sign-in,
/// so its summary always loads with the new session.
class HelpRequestsTab extends StatelessWidget {
  const HelpRequestsTab({super.key, required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: auth,
      builder: (context, _) =>
          auth.isSignedIn ? HomeScreen(auth: auth) : _SignInPrompt(auth: auth),
    );
  }
}

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  List<HelpRequest> _requests = [];
  List<TravelAdvisory> _advisories = [];
  bool _loadingSummary = true;
  String? _summaryError;

  @override
  void initState() {
    super.initState();
    HelpRequestApi.auth = widget.auth;
    _loadSummary();
  }

  Future<void> _loadSummary() async {
    // Set on every load, not just the first: that one finishes while the tab
    // is still hidden, so a refresh is when the header's bar is actually seen.
    setState(() {
      _loadingSummary = true;
      _summaryError = null;
    });

    final results = await Future.wait([
      HelpRequestService.getMineWithStatus(),
      HelpRequestService.getActiveAdvisories(),
    ]);
    if (!mounted) return;
    final result = results[0] as HelpRequestLoadResult;
    setState(() {
      _advisories = (results[1] as List<TravelAdvisory>?) ?? [];
      _requests = result.requests;
      _summaryError = result.error;
      _loadingSummary = false;
    });
  }

  void _openAdvisories() => Navigator.of(context).push(
    MaterialPageRoute(builder: (_) => const TravelAdvisoriesScreen()),
  );

  @override
  Widget build(BuildContext context) {
    final active = _requests.where((r) => helpStatusIsOpen(r.status)).length;
    final resolved = _requests.where((r) => r.status == 3).length;

    return Scaffold(
      appBar: AppHeader(title: 'Help', loading: _loadingSummary),
      body: SafeArea(
        child: Column(
          children: [
            if (_summaryError != null)
              AppErrorBanner(message: _summaryError!, onRetry: _loadSummary),
            AppStatBar(
              stats: [
                AppStat(
                  value: _loadingSummary ? '–' : '${_requests.length}',
                  label: 'Requests',
                ),
                AppStat(
                  value: _loadingSummary ? '–' : '$active',
                  label: 'Active',
                  tone: AppColors.caution,
                ),
                AppStat(
                  value: _loadingSummary ? '–' : '$resolved',
                  label: 'Resolved',
                  tone: AppColors.safe,
                ),
              ],
            ),
            Expanded(
              child: RefreshIndicator(
                onRefresh: _loadSummary,
                child: ListView(
                  // Pull-to-refresh needs a scrollable, even when everything
                  // fits on screen.
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(
                    AppSpacing.gutter,
                    18,
                    AppSpacing.gutter,
                    28,
                  ),
                  children: [
                    const Text(
                      'Help is within reach.',
                      style: TextStyle(
                        fontSize: 23,
                        fontWeight: FontWeight.w700,
                        color: AppColors.ink,
                        letterSpacing: -0.4,
                      ),
                    ),
                    const SizedBox(height: 6),
                    const Text(
                      'Choose an option below. Your request is shared with '
                      'emergency coordinators.',
                      style: TextStyle(
                        fontSize: 13.5,
                        height: 1.45,
                        color: AppColors.body,
                      ),
                    ),
                    const SizedBox(height: 18),
                    const AppCard(
                      accent: AppColors.caution,
                      child: Row(
                        children: [
                          Icon(
                            Icons.warning_amber_rounded,
                            size: 20,
                            color: AppColors.caution,
                          ),
                          SizedBox(width: 12),
                          Expanded(
                            child: Text(
                              'For immediate life-threatening emergencies, '
                              'call local emergency services first.',
                              style: TextStyle(fontSize: 13, height: 1.35),
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (_advisories.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.gap),
                      _AdvisoryBanner(
                        advisories: _advisories,
                        onTap: _openAdvisories,
                      ),
                    ],
                    const SizedBox(height: 26),
                    const AppSectionTitle('How can we help?'),
                    _HomeCard(
                      icon: Icons.sos_outlined,
                      title: 'Request help',
                      subtitle: 'Water, food, medical aid, or rescue',
                      onTap: () async {
                        await Navigator.of(context).push(
                          MaterialPageRoute(
                            builder: (_) => SubmitRequestScreen(auth: widget.auth),
                          ),
                        );
                        _loadSummary();
                      },
                    ),
                    const SizedBox(height: AppSpacing.gap),
                    _HomeCard(
                      icon: Icons.checklist_outlined,
                      title: 'My requests',
                      subtitle: 'Track the status of what you have submitted',
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute(
                          builder: (_) => const MyRequestsScreen(),
                        ),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.gap),
                    _HomeCard(
                      icon: Icons.map_outlined,
                      title: 'My request map',
                      subtitle: 'See where your requests were submitted',
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute(
                          builder: (_) => MyRequestsMapScreen(auth: widget.auth),
                        ),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.gap),
                    _HomeCard(
                      icon: Icons.call_outlined,
                      title: 'Emergency contacts',
                      subtitle: 'Hotlines and your district disaster unit',
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute(
                          builder: (_) => const EmergencyContactsScreen(),
                        ),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.gap),
                    _HomeCard(
                      icon: Icons.crisis_alert_outlined,
                      title: 'Travel advisories',
                      subtitle: 'Areas that are unsafe to travel through',
                      onTap: _openAdvisories,
                    ),
                    const SizedBox(height: AppSpacing.gap),
                    _HomeCard(
                      icon: Icons.shield_outlined,
                      title: 'Safety check',
                      subtitle:
                          'Check if your location is safe before travelling',
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute(
                          builder: (_) => const SafetyCheckScreen(),
                        ),
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

/// Says how many advisories are in force, and how many are danger zones, so
/// the warning is seen without opening anything.
class _AdvisoryBanner extends StatelessWidget {
  const _AdvisoryBanner({required this.advisories, required this.onTap});

  final List<TravelAdvisory> advisories;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final danger = advisories.where((a) => a.safetyLevel == 2).length;
    final tone = danger > 0 ? AppColors.danger : AppColors.caution;
    final total = advisories.length;

    return AppCard(
      accent: tone,
      onTap: onTap,
      child: Row(
        children: [
          Icon(
            danger > 0 ? Icons.crisis_alert : Icons.warning_amber_rounded,
            size: 20,
            color: tone,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              '$total active travel advisor${total == 1 ? 'y' : 'ies'}'
              '${danger > 0 ? ', $danger danger zone${danger == 1 ? '' : 's'}' : ''}.'
              ' Tap to see where.',
              style: const TextStyle(fontSize: 13, height: 1.35),
            ),
          ),
          const Icon(Icons.chevron_right, color: AppColors.body),
        ],
      ),
    );
  }
}

/// One of the hub's entries: icon, what it does, and a chevron.
class _HomeCard extends StatelessWidget {
  const _HomeCard({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      onTap: onTap,
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
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(
                    fontWeight: FontWeight.w700,
                    fontSize: 15,
                    color: AppColors.ink,
                  ),
                ),
                const SizedBox(height: 3),
                Text(
                  subtitle,
                  style: const TextStyle(
                    color: AppColors.body,
                    fontSize: 12.5,
                    height: 1.3,
                  ),
                ),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: AppColors.body),
        ],
      ),
    );
  }
}

class _SignInPrompt extends StatelessWidget {
  const _SignInPrompt({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Help',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
      ),
      body: Center(
        child: AppEmptyState(
          icon: Icons.health_and_safety_outlined,
          title: 'Sign in to request help',
          message:
              'An account lets coordinators verify your request and keep you '
              'updated on its status.',
          action: SizedBox(
            width: 220,
            child: AppPrimaryButton(
              label: 'Sign in',
              onPressed: () => Navigator.of(context).push<bool>(
                MaterialPageRoute(builder: (_) => LoginScreen(auth: auth)),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
