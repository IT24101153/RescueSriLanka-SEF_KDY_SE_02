import 'package:flutter/material.dart';

import 'shared/core/theme.dart';
import 'shared/services/auth_service.dart';
import 'shared/screens/splash_screen.dart';

void main() {
  runApp(const RescueSriLankaApp());
}

class RescueSriLankaApp extends StatefulWidget {
  const RescueSriLankaApp({super.key});

  @override
  State<RescueSriLankaApp> createState() => _RescueSriLankaAppState();
}

class _RescueSriLankaAppState extends State<RescueSriLankaApp> {
  /// One session for the whole app. Created here so it outlives every screen
  /// and the token survives tab switches and navigation.
  final AuthService _auth = AuthService();

  @override
  void initState() {
    super.initState();
    // Reads any stored token back while the splash screen is showing, so a
    // returning user is already signed in by the time the map appears.
    _auth.restore();
  }

  @override
  void dispose() {
    _auth.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'RescueSL',
      debugShowCheckedModeBanner: false,
      theme: buildAppTheme(),
      home: SplashScreen(auth: _auth),
    );
  }
}
