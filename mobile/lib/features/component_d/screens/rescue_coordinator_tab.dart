import 'package:flutter/material.dart';

import '../../../shared/screens/auth/login_screen.dart';
import '../../../shared/services/auth_service.dart';
import '../../../shared/widgets/app_ui.dart';
import 'rescue_coordinator_dashboard.dart';

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
        return RescueCoordinatorDashboard(
          session: session,
          onLogout: auth.signOut,
        );
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
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Rescue',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
      ),
      body: Center(
        child: AppEmptyState(
          icon: Icons.groups_outlined,
          title: 'Sign in to coordinate rescues',
          message:
              'Rescue teams, assignments and dispatches are only visible to '
              'signed-in coordinators.',
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
