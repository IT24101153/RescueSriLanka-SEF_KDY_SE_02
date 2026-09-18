import 'package:flutter/material.dart';

import '../core/theme.dart';
import '../services/auth_service.dart';
import 'map/disaster_map_screen.dart';
import 'profile/profile_screen.dart';
import 'report/report_screen.dart';

/// Three-tab shell: the disaster map, filing a report, and the profile area.
///
/// Only the middle tab needs an account — it shows a sign-in prompt instead of
/// the form when there is no session, so a tourist can open the app and read
/// the map without ever meeting a login screen.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.auth});

  final AuthService auth;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(
        index: _index,
        // IndexedStack keeps the map alive, so switching tabs never reloads it
        // — and a half-filled report survives a glance at the map.
        children: [
          const DisasterMapScreen(),
          ReportScreen(auth: widget.auth),
          ProfileScreen(auth: widget.auth),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _index,
        onDestinationSelected: (value) => setState(() => _index = value),
        backgroundColor: AppColors.surface,
        indicatorColor: AppColors.brand.withValues(alpha: 0.18),
        destinations: const [
          NavigationDestination(
            icon: Icon(Icons.map_outlined),
            selectedIcon: Icon(Icons.map),
            label: 'Disaster map',
          ),
          NavigationDestination(
            icon: Icon(Icons.add_alert_outlined),
            selectedIcon: Icon(Icons.add_alert),
            label: 'Report',
          ),
          NavigationDestination(
            icon: Icon(Icons.person_outline),
            selectedIcon: Icon(Icons.person),
            label: 'Profile',
          ),
        ],
      ),
    );
  }
}
