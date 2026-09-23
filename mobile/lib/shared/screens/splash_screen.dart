import 'dart:async';

import 'package:flutter/material.dart';
import '../core/theme.dart';
import '../services/auth_service.dart';
import 'home_shell.dart';

/// Brand splash. Goes straight to the map — no login gate.
///
/// It waits for the stored session to be read back rather than for a fixed
/// delay, so a returning user lands on a tab bar that already knows they are
/// signed in, and a first-time user is not kept waiting.
class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 700),
  )..forward();

  @override
  void initState() {
    super.initState();
    _goToApp();
  }

  Future<void> _goToApp() async {
    await Future.wait([
      // Long enough for the logo to finish fading in, short enough not to
      // stall the app when there is no session to restore.
      Future<void>.delayed(const Duration(milliseconds: 900)),
      _sessionRestored(),
    ]);
    if (!mounted) return;
    Navigator.of(context).pushReplacement(
      PageRouteBuilder(
        transitionDuration: const Duration(milliseconds: 350),
        pageBuilder: (context, animation, secondaryAnimation) =>
            HomeShell(auth: widget.auth),
        transitionsBuilder: (context, animation, secondaryAnimation, child) =>
            FadeTransition(opacity: animation, child: child),
      ),
    );
  }

  /// Completes once AuthService has finished reading any stored session.
  Future<void> _sessionRestored() {
    if (!widget.auth.isRestoring) return Future<void>.value();

    final completer = Completer<void>();
    void listener() {
      if (widget.auth.isRestoring) return;
      widget.auth.removeListener(listener);
      if (!completer.isCompleted) completer.complete();
    }

    widget.auth.addListener(listener);
    return completer.future;
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final fade = CurvedAnimation(parent: _controller, curve: Curves.easeOut);

    return Scaffold(
      backgroundColor: AppColors.surface,
      body: Center(
        child: FadeTransition(
          opacity: fade,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ScaleTransition(
                scale: Tween<double>(begin: 0.86, end: 1).animate(fade),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(28),
                  child: Image.asset(
                    'assets/icon-512.png',
                    width: 116,
                    height: 116,
                    fit: BoxFit.cover,
                  ),
                ),
              ),
              const SizedBox(height: 26),
              const Text(
                'RescueSriLanka',
                style: TextStyle(
                  color: AppColors.ink,
                  fontSize: 23,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -0.3,
                ),
              ),
              const SizedBox(height: 8),
              const Text(
                'Live disaster map & safety zones',
                style: TextStyle(
                  color: AppColors.body,
                  fontSize: 13.5,
                ),
              ),
              const SizedBox(height: 44),
              SizedBox(
                width: 26,
                height: 26,
                child: CircularProgressIndicator(
                  strokeWidth: 2.2,
                  valueColor: const AlwaysStoppedAnimation(AppColors.brand),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
