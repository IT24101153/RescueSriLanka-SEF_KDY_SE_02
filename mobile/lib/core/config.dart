import 'dart:io' show Platform;
import 'package:flutter/foundation.dart' show kIsWeb;

/// Where the ASP.NET Core API lives.
///
/// `localhost` does not resolve to your machine from a device or emulator, so
/// each platform needs its own default:
///   Android emulator -> 10.0.2.2 is the host machine
///   iOS simulator    -> localhost works
///   physical phone   -> your LAN IP, passed with --dart-define
///
/// Override at build time:
///   flutter run --dart-define=API_BASE_URL=http://192.168.1.20:5093
class AppConfig {
  const AppConfig._();

  static const String _override = String.fromEnvironment('API_BASE_URL');

  static String get apiBaseUrl {
    if (_override.isNotEmpty) return _override;
    if (kIsWeb) return 'http://localhost:5093';
    if (Platform.isAndroid) return 'http://10.0.2.2:5093';
    return 'http://localhost:5093';
  }

  /// Island bounding box — the map is locked to this.
  static const double minLatitude = 5.70;
  static const double maxLatitude = 10.00;
  static const double minLongitude = 79.40;
  static const double maxLongitude = 82.10;

  static const double defaultRadiusKm = 100;
}
