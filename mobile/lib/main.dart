import 'package:flutter/material.dart';

import 'dart:async';

import 'shared/core/theme.dart';
import 'shared/services/auth_service.dart';
import 'shared/services/firebase_push.dart';
import 'shared/services/push_notifications.dart';
import 'shared/screens/splash_screen.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializePush();
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

  StreamSubscription<String>? _tokenRefreshes;

  @override
  void initState() {
    super.initState();
    // Before sign-out, remove this phone from the account, while the token still works.
    _auth.beforeSignOut = () => releasePushDevice(_auth, pushTokens);

    // Reads any stored token back while the splash screen is showing, so a
    // returning user is already signed in by the time the map appears.
    _auth.restore().then((_) => resumePush(_auth, pushTokens));
    _tokenRefreshes = keepTokenCurrent(_auth, pushTokens);
  }

  @override
  void dispose() {
    _tokenRefreshes?.cancel();
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
