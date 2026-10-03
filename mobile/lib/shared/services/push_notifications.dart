import 'dart:async';

/// What the phone's push system provides. It sits behind this interface so the
/// rules below can be tested without a Firebase project.
abstract interface class PushTokenSource {
  /// "android" or "ios", as the API expects.
  String get platform;

  /// Asks the person for permission to show notifications. True when allowed.
  Future<bool> requestPermission();

  /// This install's push token, or null when the phone cannot provide one.
  Future<String?> token();

  /// Fires when the phone's token changes and the new one needs registering.
  Stream<String> get tokenRefreshes;

  /// Stops this install receiving pushes and forgets its token.
  Future<void> deleteToken();
}

/// The part of the signed-in account that push settings are written to.
/// [AuthService] is the app's implementation.
abstract interface class PushDeviceRegistry {
  bool get isSignedIn;

  /// Whether this account has push turned on.
  bool get pushEnabled;

  /// Records this phone's token against the signed-in account.
  Future<bool> registerPushDevice(String token, String platform);

  /// Removes this phone's token from the account.
  Future<bool> unregisterPushDevice(String token);

  /// Saves the account's push preference.
  Future<bool> setPushPreference(bool enabled);
}

enum PushChange {
  turnedOn,
  turnedOff,
  permissionDenied,
  tokenUnavailable,
  serverRefused,
}

extension PushChangeMessage on PushChange {
  String get message => switch (this) {
        PushChange.turnedOn => 'Push notifications are on for this phone.',
        PushChange.turnedOff => 'Push notifications are off for this phone.',
        PushChange.permissionDenied =>
          'Allow notifications for RescueSL in your phone settings to turn this on.',
        PushChange.tokenUnavailable =>
          'This phone could not register for notifications. Check your connection and try again.',
        PushChange.serverRefused =>
          'Could not save your notification settings. Try again.',
      };
}

/// Turns push on or off for this account on this phone.
///
/// Turning it on registers the phone first and only then saves the preference,
/// so the account never claims push is on while no phone can receive it. Turning
/// it off stops the phone first, so a failure part-way leaves no phone still
/// receiving the account's pushes.
Future<PushChange> setPushEnabled(
  PushDeviceRegistry registry,
  PushTokenSource source,
  bool enabled,
) async {
  if (enabled) {
    if (!await source.requestPermission()) return PushChange.permissionDenied;

    final token = await source.token();
    if (token == null) return PushChange.tokenUnavailable;

    if (!await registry.registerPushDevice(token, source.platform)) {
      return PushChange.serverRefused;
    }

    if (!await registry.setPushPreference(true)) {
      // Undo the registration, so the phone does not receive pushes the account
      // does not have switched on.
      await registry.unregisterPushDevice(token);
      return PushChange.serverRefused;
    }

    return PushChange.turnedOn;
  }

  final token = await source.token();
  if (token != null) {
    await registry.unregisterPushDevice(token);
  }
  await source.deleteToken();

  if (!await registry.setPushPreference(false)) return PushChange.serverRefused;
  return PushChange.turnedOff;
}

/// Before sign-out: removes this phone from the account, while the session can
/// still authorise the request. Without this, the next person to sign in on the
/// phone would receive the previous account's pushes.
Future<void> releasePushDevice(
  PushDeviceRegistry registry,
  PushTokenSource source,
) async {
  final token = await source.token();
  if (token != null) {
    await registry.unregisterPushDevice(token);
  }
}

/// At start-up: records this phone's current token for an account that has push
/// on. Firebase can change the token while the app is closed, and the server
/// should not be left holding the old one.
Future<void> resumePush(PushDeviceRegistry registry, PushTokenSource source) async {
  if (!registry.isSignedIn || !registry.pushEnabled) return;

  final token = await source.token();
  if (token != null) {
    await registry.registerPushDevice(token, source.platform);
  }
}

/// While the app runs: when Firebase rotates this phone's token, the new one is
/// registered for an account that has push on.
StreamSubscription<String> keepTokenCurrent(
  PushDeviceRegistry registry,
  PushTokenSource source,
) {
  return source.tokenRefreshes.listen((token) async {
    if (!registry.isSignedIn || !registry.pushEnabled) return;
    await registry.registerPushDevice(token, source.platform);
  });
}
