import 'package:flutter/material.dart';

import '../../core/config.dart';
import '../../core/theme.dart';
import '../../services/auth_service.dart';
import '../auth/login_screen.dart';
import '../auth/register_screen.dart';
import 'notification_settings.dart';
import '../../../shared/widgets/app_ui.dart';

/// Profile tab. Shows who is signed in, or offers the two ways to get there.
class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: auth,
      builder: (context, _) {
        return Scaffold(
          appBar: const AppHeader(title: 'Profile'),
          body: SafeArea(
            child: auth.isSignedIn
                ? _SignedIn(auth: auth)
                : _SignedOut(auth: auth),
          ),
        );
      },
    );
  }
}

class _SignedIn extends StatelessWidget {
  const _SignedIn({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    final user = auth.user!;

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 28),
      children: [
        Row(
          children: [
            CircleAvatar(
              radius: 27,
              backgroundColor: AppColors.brand.withValues(alpha: 0.2),
              child: Text(
                user.shortName.characters.first.toUpperCase(),
                style: const TextStyle(
                  fontSize: 21,
                  fontWeight: FontWeight.w600,
                  color: AppColors.brandInk,
                ),
              ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    user.fullName,
                    style: const TextStyle(
                      fontSize: 17,
                      fontWeight: FontWeight.w600,
                      color: AppColors.ink,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(user.email, style: const TextStyle(fontSize: 13)),
                ],
              ),
            ),
          ],
        ),
        const SizedBox(height: 24),

        _InfoRow(label: 'Role', value: user.role),
        if (user.phoneNumber != null && user.phoneNumber!.isNotEmpty)
          _InfoRow(label: 'Phone', value: user.phoneNumber!),
        _InfoRow(label: 'District', value: user.district ?? 'Not set'),
        _InfoRow(label: 'Server', value: AppConfig.apiBaseUrl),

        const SizedBox(height: 22),
        NotificationSettings(auth: auth),

        const SizedBox(height: 28),
        OutlinedButton.icon(
          onPressed: () async {
            final messenger = ScaffoldMessenger.of(context);
            await auth.signOut();
            messenger.showSnackBar(
              const SnackBar(content: Text('Signed out.')),
            );
          },
          style: OutlinedButton.styleFrom(
            minimumSize: const Size.fromHeight(46),
            foregroundColor: AppColors.critical,
            side: BorderSide(color: AppColors.critical.withValues(alpha: 0.4)),
          ),
          icon: const Icon(Icons.logout, size: 19),
          label: const Text('Sign out'),
        ),
      ],
    );
  }
}

class _SignedOut extends StatelessWidget {
  const _SignedOut({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 28, 20, 28),
      children: [
        const Icon(Icons.person_outline, size: 52, color: AppColors.brand),
        const SizedBox(height: 18),
        Text(
          'You are browsing as a guest',
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.titleLarge?.copyWith(
                fontWeight: FontWeight.w600,
                color: AppColors.ink,
              ),
        ),
        const SizedBox(height: 10),
        const Text(
          'The live map and safety zones are open to everyone. An account is '
          'needed only to file a report, so a coordinator can follow it up.',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 13.5, height: 1.5),
        ),
        const SizedBox(height: 28),
        FilledButton(
          onPressed: () => Navigator.of(context).push<bool>(
            MaterialPageRoute(builder: (_) => LoginScreen(auth: auth)),
          ),
          style: FilledButton.styleFrom(
            minimumSize: const Size.fromHeight(48),
            backgroundColor: AppColors.brand,
            foregroundColor: AppColors.brandInk,
          ),
          child: const Text('Sign in'),
        ),
        const SizedBox(height: 12),
        OutlinedButton(
          onPressed: () => Navigator.of(context).push<bool>(
            MaterialPageRoute(builder: (_) => RegisterScreen(auth: auth)),
          ),
          style: OutlinedButton.styleFrom(
            minimumSize: const Size.fromHeight(48),
          ),
          child: const Text('Create an account'),
        ),
      ],
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 9),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 82,
            child: Text(
              label,
              style: const TextStyle(fontSize: 12.5, color: AppColors.body),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: const TextStyle(fontSize: 13.5, color: AppColors.ink),
            ),
          ),
        ],
      ),
    );
  }
}
