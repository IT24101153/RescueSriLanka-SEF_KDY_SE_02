import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/shared/services/push_notifications.dart';

/// A phone whose push system can be told to refuse, or to have no token.
class FakePhone implements PushTokenSource {
  FakePhone({this.permissionGranted = true, this.tokenValue = 'token-a'});

  bool permissionGranted;
  String? tokenValue;
  bool deleted = false;
  final StreamController<String> refreshes = StreamController<String>.broadcast();

  @override
  String get platform => 'android';

  @override
  Future<bool> requestPermission() async => permissionGranted;

  @override
  Future<String?> token() async => tokenValue;

  @override
  Stream<String> get tokenRefreshes => refreshes.stream;

  @override
  Future<void> deleteToken() async {
    deleted = true;
    tokenValue = null;
  }
}

/// An account that records every change it is asked to make, in order.
class FakeAccount implements PushDeviceRegistry {
  FakeAccount({this.pushEnabled = false, this.registerOk = true, this.preferenceOk = true});

  @override
  bool pushEnabled;

  bool registerOk;
  bool preferenceOk;
  final List<String> calls = [];

  @override
  bool get isSignedIn => true;

  @override
  Future<bool> registerPushDevice(String token, String platform) async {
    calls.add('register:$token:$platform');
    return registerOk;
  }

  @override
  Future<bool> unregisterPushDevice(String token) async {
    calls.add('unregister:$token');
    return true;
  }

  @override
  Future<bool> setPushPreference(bool enabled) async {
    calls.add('preference:$enabled');
    if (preferenceOk) pushEnabled = enabled;
    return preferenceOk;
  }
}

void main() {
  group('turning push on', () {
    test('registers the phone, then saves the preference', () async {
      final phone = FakePhone();
      final account = FakeAccount();

      final result = await setPushEnabled(account, phone, true);

      expect(result, PushChange.turnedOn);
      expect(account.calls, ['register:token-a:android', 'preference:true']);
      expect(account.pushEnabled, isTrue);
    });

    test('a refused permission changes nothing', () async {
      final phone = FakePhone(permissionGranted: false);
      final account = FakeAccount();

      final result = await setPushEnabled(account, phone, true);

      expect(result, PushChange.permissionDenied);
      expect(account.calls, isEmpty);
      expect(account.pushEnabled, isFalse);
    });

    test('a phone with no token is reported, not registered', () async {
      final phone = FakePhone(tokenValue: null);
      final account = FakeAccount();

      final result = await setPushEnabled(account, phone, true);

      expect(result, PushChange.tokenUnavailable);
      expect(account.calls, isEmpty);
    });

    test('when the server will not record the phone, the preference stays off', () async {
      final account = FakeAccount(registerOk: false);

      final result = await setPushEnabled(account, FakePhone(), true);

      expect(result, PushChange.serverRefused);
      expect(account.calls, ['register:token-a:android']);
      expect(account.pushEnabled, isFalse);
    });

    test('when the preference cannot be saved, the phone registration is undone', () async {
      final account = FakeAccount(preferenceOk: false);

      final result = await setPushEnabled(account, FakePhone(), true);

      expect(result, PushChange.serverRefused);
      expect(account.calls, [
        'register:token-a:android',
        'preference:true',
        'unregister:token-a',
      ]);
    });
  });

  group('turning push off', () {
    test('removes the phone, then clears the preference', () async {
      final phone = FakePhone();
      final account = FakeAccount(pushEnabled: true);

      final result = await setPushEnabled(account, phone, false);

      expect(result, PushChange.turnedOff);
      expect(account.calls, ['unregister:token-a', 'preference:false']);
      expect(phone.deleted, isTrue);
      expect(account.pushEnabled, isFalse);
    });
  });

  group('keeping the phone current', () {
    test('a rotated token is registered while push is on', () async {
      final phone = FakePhone();
      final account = FakeAccount(pushEnabled: true);
      final subscription = keepTokenCurrent(account, phone);

      phone.refreshes.add('token-b');
      await pumpEventQueue();

      expect(account.calls, ['register:token-b:android']);
      await subscription.cancel();
    });

    test('a rotated token is ignored while push is off', () async {
      final phone = FakePhone();
      final account = FakeAccount(pushEnabled: false);
      final subscription = keepTokenCurrent(account, phone);

      phone.refreshes.add('token-b');
      await pumpEventQueue();

      expect(account.calls, isEmpty);
      await subscription.cancel();
    });

    test('start-up re-registers the current token for an account with push on', () async {
      final account = FakeAccount(pushEnabled: true);

      await resumePush(account, FakePhone());

      expect(account.calls, ['register:token-a:android']);
    });
  });

  test('before sign-out, the phone is removed from the account', () async {
    final account = FakeAccount(pushEnabled: true);

    await releasePushDevice(account, FakePhone());

    expect(account.calls, ['unregister:token-a']);
  });
}
