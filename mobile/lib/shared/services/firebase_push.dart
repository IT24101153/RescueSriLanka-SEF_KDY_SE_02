import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';

import 'push_notifications.dart';

/// Android notification channel for area warnings and updates. The manifest
/// names it as the default, so pushes that arrive in the background use it too.
const String _alertChannelId = 'rescuesl_alerts';

final FlutterLocalNotificationsPlugin _local = FlutterLocalNotificationsPlugin();

bool _ready = false;

/// Starts Firebase and foreground display. Never throws. Without a Firebase
/// configuration the app still runs, and push is simply unavailable.
Future<void> initializePush() async {
  try {
    await Firebase.initializeApp();
    FirebaseMessaging.onBackgroundMessage(_onBackgroundMessage);

    await _local.initialize(
      settings: const InitializationSettings(
        android: AndroidInitializationSettings('@mipmap/ic_launcher'),
        iOS: DarwinInitializationSettings(
          requestAlertPermission: false,
          requestBadgePermission: false,
          requestSoundPermission: false,
        ),
      ),
    );

    await _local
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.createNotificationChannel(
          const AndroidNotificationChannel(
            _alertChannelId,
            'Alerts and updates',
            description: 'Area warnings and updates on your reports and requests.',
            importance: Importance.high,
          ),
        );

    await FirebaseMessaging.instance.setForegroundNotificationPresentationOptions(
      alert: true,
      badge: true,
      sound: true,
    );

    FirebaseMessaging.onMessage.listen(_showForeground);
    _ready = true;
  } catch (error) {
    debugPrint('Push notifications are unavailable: $error');
  }
}

/// The phone's push system, as the push rules see it.
final PushTokenSource pushTokens = FirebasePushTokenSource();

class FirebasePushTokenSource implements PushTokenSource {
  @override
  String get platform =>
      defaultTargetPlatform == TargetPlatform.iOS ? 'ios' : 'android';

  @override
  Future<bool> requestPermission() async {
    if (!_ready) return false;

    // Android 13 and later need the runtime permission for notifications, which
    // the messaging plugin does not ask for on its own.
    final androidGranted = await _local
        .resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>()
        ?.requestNotificationsPermission();
    if (androidGranted == false) return false;

    final settings = await FirebaseMessaging.instance.requestPermission();
    return settings.authorizationStatus == AuthorizationStatus.authorized ||
        settings.authorizationStatus == AuthorizationStatus.provisional;
  }

  @override
  Future<String?> token() async {
    if (!_ready) return null;
    try {
      return await FirebaseMessaging.instance.getToken();
    } catch (_) {
      return null;
    }
  }

  @override
  Stream<String> get tokenRefreshes => _ready
      ? FirebaseMessaging.instance.onTokenRefresh
      : const Stream<String>.empty();

  @override
  Future<void> deleteToken() async {
    if (!_ready) return;
    try {
      await FirebaseMessaging.instance.deleteToken();
    } catch (_) {
      // Nothing to undo if Firebase never issued one.
    }
  }
}

/// Messages that arrive while the app is in the background are shown by the
/// system itself, so there is nothing to do here. Top-level so the messaging
/// plugin can call it in its own isolate.
@pragma('vm:entry-point')
Future<void> _onBackgroundMessage(RemoteMessage message) async {}

/// While the app is open, the system does not show a push, so it is shown here.
Future<void> _showForeground(RemoteMessage message) async {
  final notification = message.notification;
  if (notification == null) return;

  await _local.show(
    id: message.hashCode,
    title: notification.title,
    body: notification.body,
    notificationDetails: const NotificationDetails(
      android: AndroidNotificationDetails(
        _alertChannelId,
        'Alerts and updates',
        importance: Importance.high,
        priority: Priority.high,
      ),
      iOS: DarwinNotificationDetails(),
    ),
  );
}
