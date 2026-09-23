import 'package:flutter/material.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/screens/auth/login_screen.dart';
import '../../../shared/services/auth_service.dart';
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
      appBar: AppBar(title: const Text('Rescue coordination')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.groups_outlined, size: 54, color: AppColors.brand),
              const SizedBox(height: 18),
              Text(
                'Sign in to coordinate rescues',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: AppColors.ink,
                    ),
              ),
              const SizedBox(height: 10),
              const Text(
                'Rescue teams, assignments and dispatches are only visible to '
                'signed-in coordinators.',
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
