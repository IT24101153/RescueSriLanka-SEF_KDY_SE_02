import 'package:flutter/material.dart';
import '../core/theme.dart';
import '../services/auth_service.dart';
import '../services/help_request_api.dart';
import 'auth/login_screen.dart';
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
      builder: (context, _) => auth.isSignedIn
          ? HomeScreen(auth: auth)
          : _SignInPrompt(auth: auth),
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
    return Scaffold(
      appBar: AppBar(
        title: const Row(
          children: [
            Icon(Icons.health_and_safety_outlined, size: 23),
            SizedBox(width: 9),
            Text('RescueSriLanka'),
          ],
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh dashboard',
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
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 20, 20, 28),
          children: [
              const Text('Help is within reach.', style: TextStyle(fontSize: 25, fontWeight: FontWeight.w800, color: Color(0xFF17323B))),
              const SizedBox(height: 6),
              const Text('Choose an option below. Your request is shared with emergency coordinators.', style: TextStyle(color: Color(0xFF617178), height: 1.45)),
              const SizedBox(height: 20),
              Container(
                padding: const EdgeInsets.all(18),
                decoration: BoxDecoration(
                  color: const Color(0xFFFFF4DE),
                  borderRadius: BorderRadius.circular(18),
                  border: Border.all(color: const Color(0xFFF4D59E)),
                ),
                child: const Row(children: [
                  Icon(Icons.info_outline, color: Color(0xFF9C6412)),
                  SizedBox(width: 12),
                  Expanded(child: Text('For immediate life-threatening emergencies, call local emergency services first.', style: TextStyle(color: Color(0xFF6F4A11), fontSize: 13, height: 1.35))),
                ]),
              ),
              const SizedBox(height: 24),
              const Text('Your request overview', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: Color(0xFF17323B))),
              const SizedBox(height: 12),
              _RequestSummary(requests: _requests, loading: _loadingSummary),
              if (_summaryError != null) ...[
                const SizedBox(height: 8),
                Text(_summaryError!, style: const TextStyle(fontSize: 12, color: Color(0xFFB33E39))),
              ],
              const SizedBox(height: 24),
              const Text('How can we help?', style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: Color(0xFF17323B))),
              const SizedBox(height: 12),
              _HomeCard(
                icon: Icons.sos_outlined,
                title: 'Request Help',
                subtitle: 'Water, food, medical aid, rescue, or shelter',
                color: const Color(0xFFC8453C),
                onTap: () async {
                  await Navigator.of(context).push(
                    MaterialPageRoute(builder: (_) => const SubmitRequestScreen()),
                  );
                  _loadSummary();
                },
              ),
              const SizedBox(height: 14),
              _HomeCard(
                icon: Icons.checklist_outlined,
                title: 'My Requests',
                subtitle: 'Track the status of what you have submitted',
                color: const Color(0xFF0B6E69),
                onTap: () {
                  Navigator.of(context).push(
                    MaterialPageRoute(builder: (_) => const MyRequestsScreen()),
                  );
                },
              ),
              const SizedBox(height: 14),
              _HomeCard(
                icon: Icons.shield_outlined,
                title: 'Safety Check',
                subtitle: 'Check if your location is safe before traveling',
                color: const Color(0xFFF0A12F),
                onTap: () {
                  Navigator.of(context).push(
                    MaterialPageRoute(builder: (_) => const SafetyCheckScreen()),
                  );
                },
              ),
              const SizedBox(height: 18),
              OutlinedButton.icon(
                icon: const Icon(Icons.map_outlined),
                label: const Text('View my request map'),
                onPressed: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const MyRequestsMapScreen())),
              ),
          ],
        ),
      ),
    );
  }
}

class _RequestSummary extends StatelessWidget {
  final List<HelpRequest> requests;
  final bool loading;

  const _RequestSummary({required this.requests, required this.loading});

  @override
  Widget build(BuildContext context) {
    final active = requests.where((request) => request.status == 0 || request.status == 1 || request.status == 2).length;
    final resolved = requests.where((request) => request.status == 3).length;
    return Row(
      children: [
        Expanded(child: _SummaryMetric(label: 'Total requests', value: loading ? '...' : '${requests.length}', color: const Color(0xFF2F6FB0), icon: Icons.description_outlined)),
        const SizedBox(width: 10),
        Expanded(child: _SummaryMetric(label: 'Active', value: loading ? '...' : '$active', color: const Color(0xFFB8720A), icon: Icons.pending_actions_outlined)),
        const SizedBox(width: 10),
        Expanded(child: _SummaryMetric(label: 'Resolved', value: loading ? '...' : '$resolved', color: const Color(0xFF0E8F56), icon: Icons.task_alt_outlined)),
      ],
    );
  }
}

class _SummaryMetric extends StatelessWidget {
  final String label;
  final String value;
  final Color color;
  final IconData icon;

  const _SummaryMetric({required this.label, required this.value, required this.color, required this.icon});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 118,
      child: Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
          border: Border(top: BorderSide(color: color, width: 3), left: const BorderSide(color: Color(0xFFE1E8E8)), right: const BorderSide(color: Color(0xFFE1E8E8)), bottom: const BorderSide(color: Color(0xFFE1E8E8))),
        ),
        child: Align(
          alignment: Alignment.topLeft,
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            Icon(icon, color: color, size: 20),
            const SizedBox(height: 8),
            Text(value, style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: Color(0xFF17323B))),
            const SizedBox(height: 2),
            Text(label, style: const TextStyle(fontSize: 11, color: Color(0xFF68797F)), maxLines: 1, overflow: TextOverflow.ellipsis),
          ]),
        ),
      ),
    );
  }
}

class _HomeCard extends StatelessWidget {
  final IconData icon;
  final String title;
  final String subtitle;
  final Color color;
  final VoidCallback onTap;

  const _HomeCard({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.color,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.white,
      borderRadius: BorderRadius.circular(18),
      child: InkWell(
        borderRadius: BorderRadius.circular(18),
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.all(18),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(18),
            border: Border.all(color: const Color(0xFFE1E8E8)),
          ),
          child: Row(
            children: [
              Container(
                width: 46,
                height: 46,
                decoration: BoxDecoration(
                  color: color.withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(14),
                ),
                child: Icon(icon, color: color),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16, color: Color(0xFF17323B))),
                    const SizedBox(height: 3),
                    Text(subtitle, style: const TextStyle(color: Color(0xFF68797F), fontSize: 13, height: 1.3)),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right, color: Color(0xFF7A7D89)),
            ],
          ),
        ),
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
      appBar: AppBar(title: const Text('Get help')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.health_and_safety_outlined, size: 54, color: AppColors.brand),
              const SizedBox(height: 18),
              Text(
                'Sign in to request help',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: AppColors.ink,
                    ),
              ),
              const SizedBox(height: 10),
              const Text(
                'An account lets coordinators verify your request and keep you '
                'updated on its status.',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 13.5, height: 1.5),
              ),
              const SizedBox(height: 24),
              FilledButton(
                onPressed: () => Navigator.of(context).push<bool>(
                  MaterialPageRoute(builder: (_) => LoginScreen(auth: auth)),
                ),
                style: FilledButton.styleFrom(
                  minimumSize: const Size(220, 46),
                  backgroundColor: AppColors.brand,
                  foregroundColor: AppColors.brandInk,
                ),
                child: const Text('Sign in'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
