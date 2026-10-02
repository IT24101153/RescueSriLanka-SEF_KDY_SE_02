import 'package:flutter/material.dart';

import '../../../shared/screens/auth/login_screen.dart';
import '../../../shared/services/auth_service.dart';
import '../../../shared/widgets/app_ui.dart';
import 'rescue_coordinator_dashboard.dart';
import 'rescue_overview_screen.dart';

/// The Rescue tab in the app shell: Component D's coordination dashboard when
/// signed in, a sign-in prompt otherwise. It uses the app-wide session, the
/// same one the Report and Help tabs use.
class RescueCoordinatorTab extends StatelessWidget {
  const RescueCoordinatorTab({super.key, required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: auth,
      builder: (context, _) {
        final session = auth.session;
        if (session == null) return _SignInPrompt(auth: auth);
        // Only the Rescue Team account manages teams; everyone else gets the
        // read-only overview.
        if (session.user.role == 'RescueTeam') {
          return RescueCoordinatorDashboard(session: session);
        }
        return RescueOverviewScreen(session: session);
      },
    );
  }
}

class _SignInPrompt extends StatelessWidget {
  const _SignInPrompt({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: const AppHeader(title: 'Rescue'),
      body: Center(
        child: AppEmptyState(
          icon: Icons.groups_outlined,
          title: 'Sign in to see rescue teams',
          message: 'See which rescue teams are available and what they can do.',
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
