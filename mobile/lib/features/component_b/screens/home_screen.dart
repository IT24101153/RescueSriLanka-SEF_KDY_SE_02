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
import 'safety_check_screen.dart';
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
  bool _loadingSummary = true;
  String? _summaryError;

  @override
  void initState() {
    super.initState();
    HelpRequestApi.auth = widget.auth;
    _loadSummary();
  }

  Future<void> _loadSummary() async {
    final result = await HelpRequestService.getMineWithStatus();
    if (!mounted) return;
    setState(() {
      _requests = result.requests;
      _summaryError = result.error;
      _loadingSummary = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    final active = _requests.where((r) => helpStatusIsOpen(r.status)).length;
    final resolved = _requests.where((r) => r.status == 3).length;

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Help',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: _loadingSummary ? null : _loadSummary,
          ),
          IconButton(
            icon: const Icon(Icons.logout),
            tooltip: 'Log out',
            onPressed: () => widget.auth.signOut(),
          ),
        ],
      ),
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
                    const SizedBox(height: 26),
                    const AppSectionTitle('How can we help?'),
                    _HomeCard(
                      icon: Icons.sos_outlined,
                      title: 'Request help',
                      subtitle: 'Water, food, medical aid, rescue, or shelter',
                      onTap: () async {
                        await Navigator.of(context).push(
                          MaterialPageRoute(
                            builder: (_) => const SubmitRequestScreen(),
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
                          builder: (_) => const MyRequestsMapScreen(),
                        ),
                      ),
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
