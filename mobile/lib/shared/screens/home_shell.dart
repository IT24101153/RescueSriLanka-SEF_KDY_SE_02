import 'package:flutter/material.dart';

import '../core/theme.dart';
import '../services/auth_service.dart';
import '../../features/component_b/screens/home_screen.dart';
import '../../features/component_c/screens/resource_home_screen.dart';
import '../../features/component_d/screens/rescue_coordinator_tab.dart';
import '../../features/component_a/screens/map/disaster_map_screen.dart';
import 'profile/profile_screen.dart';
import '../../features/component_a/screens/report/report_screen.dart';

/// Six-tab shell: the disaster map, filing a report, asking for help, the
/// resource area, rescue coordination, and the profile area.
///
/// Only the Report and Help tabs need an account — they show a sign-in prompt
/// instead of the form when there is no session, so a tourist can open the app
/// and read the map without ever meeting a login screen.
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
          HelpRequestsTab(auth: widget.auth),
          const ResourceHomePage(),
          RescueCoordinatorTab(auth: widget.auth),
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
            icon: Icon(Icons.health_and_safety_outlined),
            selectedIcon: Icon(Icons.health_and_safety),
            label: 'Help',
          ),
          NavigationDestination(
            icon: Icon(Icons.inventory_2_outlined),
            selectedIcon: Icon(Icons.inventory_2),
            label: 'Resources',
          ),
          NavigationDestination(
            icon: Icon(Icons.groups_outlined),
            selectedIcon: Icon(Icons.groups),
            label: 'Rescue',
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
