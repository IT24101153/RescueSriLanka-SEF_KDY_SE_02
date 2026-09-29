import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:liquid_glass_easy/liquid_glass_easy.dart';

import '../core/theme.dart';
import '../services/auth_service.dart';
import '../../features/component_b/screens/home_screen.dart';
import '../../features/component_c/screens/resource_home_screen.dart';
import '../../features/component_d/screens/rescue_coordinator_tab.dart';
import '../../features/component_a/screens/map/disaster_map_screen.dart';
import 'profile/profile_screen.dart';
import '../../features/component_a/screens/report/report_screen.dart';

/// Whether this platform gets the floating "liquid glass" nav bar instead
/// of the flat Material one.
bool get _isIOS => defaultTargetPlatform == TargetPlatform.iOS;

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

/// A tab: the screen it shows and its icon/label, kept as raw data so both
/// the Material bar and the glass bar can build their own widget from it.
class _Tab {
  const _Tab({
    required this.screen,
    required this.icon,
    required this.selectedIcon,
    required this.label,
  });

  final Widget screen;
  final IconData icon;
  final IconData selectedIcon;
  final String label;

  NavigationDestination toDestination() => NavigationDestination(
    icon: Icon(icon),
    selectedIcon: Icon(selectedIcon),
    label: label,
  );

  LiquidGlassTabBarItem toGlassItem() => LiquidGlassTabBarItem(
    icon: icon,
    selectedIcon: selectedIcon,
    label: label,
  );
}

class _HomeShellState extends State<HomeShell> {
  int _index = 0;
  late bool _wasSignedIn = widget.auth.isSignedIn;

  List<_Tab> _tabsFor(bool signedIn) {
    return [
      const _Tab(
        screen: DisasterMapScreen(),
        icon: Icons.map_outlined,
        selectedIcon: Icons.map,
        label: 'Map',
      ),
      if (signedIn) ...[
        _Tab(
          screen: ReportScreen(auth: widget.auth),
          icon: Icons.add_alert_outlined,
          selectedIcon: Icons.add_alert,
          label: 'Report',
        ),
        _Tab(
          screen: HelpRequestsTab(auth: widget.auth),
          icon: Icons.health_and_safety_outlined,
          selectedIcon: Icons.health_and_safety,
          label: 'Help',
        ),
        _Tab(
          screen: ResourceHomePage(auth: widget.auth),
          icon: Icons.inventory_2_outlined,
          selectedIcon: Icons.inventory_2,
          label: 'Resources',
        ),
        _Tab(
          screen: RescueCoordinatorTab(auth: widget.auth),
          icon: Icons.groups_outlined,
          selectedIcon: Icons.groups,
          label: 'Rescue',
        ),
      ],
      _Tab(
        screen: ProfileScreen(auth: widget.auth),
        icon: Icons.person_outline,
        selectedIcon: Icons.person,
        label: 'Profile',
      ),
    ];
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.auth,
      builder: (context, _) {
        final signedIn = widget.auth.isSignedIn;
        // Signing in adds tabs before the current one, so the same index
        // would land on a different tab (e.g. Report instead of Map/Profile).
        // Jump back to Map on that transition; sign-out keeps the clamp
        // fallback below since the bar only shrinks there.
        if (signedIn && !_wasSignedIn) {
          _index = 0;
        }
        _wasSignedIn = signedIn;
        final tabs = _tabsFor(signedIn);
        // Signing out shortens the bar, so a tab that no longer exists falls
        // back to the last one rather than crashing.
        final index = _index.clamp(0, tabs.length - 1);
        void select(int value) => setState(() => _index = value);

        final content = IndexedStack(
          index: index,
          // IndexedStack keeps the map alive, so switching tabs never
          // reloads it — and a half-filled report survives a glance at it.
          children: [for (final tab in tabs) tab.screen],
        );

        if (!_isIOS) {
          return Scaffold(
            body: content,
            bottomNavigationBar: NavigationBar(
              selectedIndex: index,
              onDestinationSelected: select,
              backgroundColor: AppColors.surface,
              indicatorColor: AppColors.brand.withValues(alpha: 0.18),
              // Six tabs share the width, so labels run a size below the
              // Material default; the selected one stays bolder to stand out.
              labelTextStyle: WidgetStateProperty.resolveWith(
                (states) => TextStyle(
                  fontSize: 11,
                  fontWeight: states.contains(WidgetState.selected)
                      ? FontWeight.w600
                      : FontWeight.w500,
                  color: AppColors.ink,
                ),
              ),
              destinations: [for (final tab in tabs) tab.toDestination()],
            ),
          );
        }

        // iOS: a bodyless floating glass bar over the full-bleed body,
        // matching the system tab bar introduced in iOS 26 — it reads the
        // live content behind it instead of a captured page, so the
        // Scaffold underneath needs no bottomNavigationBar slot at all.
        return Scaffold(
          body: Stack(
            children: [
              content,
              LiquidGlassTabBar.withImpeller(
                items: [for (final tab in tabs) tab.toGlassItem()],
                selectedIndex: index,
                onChanged: select,
                width: MediaQuery.sizeOf(context).width - 32,
                // The default white/white70 icon palette assumes a dark or
                // colourful backdrop; this app's surfaces are light, so the
                // bar needs dark ink instead to stay legible over them.
                itemStyle: const LiquidGlassTabItemStyle(
                  selectedColor: AppColors.brand,
                  unselectedColor: AppColors.body,
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}
