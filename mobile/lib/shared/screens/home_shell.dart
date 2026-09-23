import 'package:flutter/material.dart';

import '../core/theme.dart';
import '../services/auth_service.dart';
import '../../features/component_b/screens/home_screen.dart';
import '../../features/component_c/screens/resource_home_screen.dart';
import '../../features/component_d/screens/rescue_coordinator_tab.dart';
import '../../features/component_a/screens/map/disaster_map_screen.dart';
import 'profile/profile_screen.dart';
import '../../features/component_a/screens/report/report_screen.dart';

/// One tab per thing you can do, and the tab bar follows the session.
///
/// Signed out there are two tabs — the disaster map, which is open to
/// everyone, and Profile, where you sign in or create an account. Signing in
/// adds Report, Help, Resources and Rescue; signing out takes them away again.
class HomeShell extends StatefulWidget {
  const HomeShell({super.key, required this.auth});

  final AuthService auth;

  @override
  State<HomeShell> createState() => _HomeShellState();
}

/// A tab: the screen it shows and how it appears in the bar.
class _Tab {
  const _Tab({required this.screen, required this.destination});

  final Widget screen;
  final NavigationDestination destination;
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;

  List<_Tab> _tabsFor(bool signedIn) {
    return [
      _Tab(
        screen: const DisasterMapScreen(),
        destination: const NavigationDestination(
          icon: Icon(Icons.map_outlined),
          selectedIcon: Icon(Icons.map),
          label: 'Disaster map',
        ),
      ),
      if (signedIn) ...[
        _Tab(
          screen: ReportScreen(auth: widget.auth),
          destination: const NavigationDestination(
            icon: Icon(Icons.add_alert_outlined),
            selectedIcon: Icon(Icons.add_alert),
            label: 'Report',
          ),
        ),
        _Tab(
          screen: HelpRequestsTab(auth: widget.auth),
          destination: const NavigationDestination(
            icon: Icon(Icons.health_and_safety_outlined),
            selectedIcon: Icon(Icons.health_and_safety),
            label: 'Help',
          ),
        ),
        _Tab(
          screen: const ResourceHomePage(),
          destination: const NavigationDestination(
            icon: Icon(Icons.inventory_2_outlined),
            selectedIcon: Icon(Icons.inventory_2),
            label: 'Resources',
          ),
        ),
        _Tab(
          screen: RescueCoordinatorTab(auth: widget.auth),
          destination: const NavigationDestination(
            icon: Icon(Icons.groups_outlined),
            selectedIcon: Icon(Icons.groups),
            label: 'Rescue',
          ),
        ),
      ],
      _Tab(
        screen: ProfileScreen(auth: widget.auth),
        destination: const NavigationDestination(
          icon: Icon(Icons.person_outline),
          selectedIcon: Icon(Icons.person),
          label: 'Profile',
        ),
      ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.auth,
      builder: (context, _) {
        final tabs = _tabsFor(widget.auth.isSignedIn);
        // Signing out shortens the bar, so a tab that no longer exists falls
        // back to the last one rather than crashing.
        final index = _index.clamp(0, tabs.length - 1);

        return Scaffold(
          body: IndexedStack(
            index: index,
            // IndexedStack keeps the map alive, so switching tabs never
            // reloads it — and a half-filled report survives a glance at it.
            children: [for (final tab in tabs) tab.screen],
          ),
          bottomNavigationBar: NavigationBar(
            selectedIndex: index,
            onDestinationSelected: (value) => setState(() => _index = value),
            backgroundColor: AppColors.surface,
            indicatorColor: AppColors.brand.withValues(alpha: 0.18),
            destinations: [for (final tab in tabs) tab.destination],
          ),
        );
      },
    );
  }
}
