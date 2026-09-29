import 'dart:math' as math;

import 'package:latlong2/latlong.dart';

/// The main town of each of Sri Lanka's 25 districts.
///
/// Bundled rather than looked up so "My area" and "Another area" still work
/// with no signal — the likely state of the network during a disaster. A
/// town is only a starting point for the pin, never an exact location.
const Map<String, LatLng> districtTowns = {
  'Ampara': LatLng(7.2912, 81.6724),
  'Anuradhapura': LatLng(8.3114, 80.4037),
  'Badulla': LatLng(6.9934, 81.0550),
  'Batticaloa': LatLng(7.7310, 81.6747),
  'Colombo': LatLng(6.9271, 79.8612),
  'Galle': LatLng(6.0535, 80.2210),
  'Gampaha': LatLng(7.0873, 80.0144),
  'Hambantota': LatLng(6.1241, 81.1185),
  'Jaffna': LatLng(9.6615, 80.0255),
  'Kalutara': LatLng(6.5854, 79.9607),
  'Kandy': LatLng(7.2906, 80.6337),
  'Kegalle': LatLng(7.2513, 80.3464),
  'Kilinochchi': LatLng(9.3803, 80.3770),
  'Kurunegala': LatLng(7.4863, 80.3647),
  'Mannar': LatLng(8.9810, 79.9044),
  'Matale': LatLng(7.4675, 80.6234),
  'Matara': LatLng(5.9549, 80.5550),
  'Monaragala': LatLng(6.8728, 81.3507),
  'Mullaitivu': LatLng(9.2671, 80.8142),
  'Nuwara Eliya': LatLng(6.9497, 80.7891),
  'Polonnaruwa': LatLng(7.9403, 81.0188),
  'Puttalam': LatLng(8.0362, 79.8283),
  'Ratnapura': LatLng(6.6828, 80.3992),
  'Trincomalee': LatLng(8.5874, 81.2152),
  'Vavuniya': LatLng(8.7514, 80.4971),
};

/// Spelling-insensitive key, so "Nuwara-Eliya", "NuwaraEliya" and
/// "Moneragala" all find their town.
String _normalise(String name) => name
    .toLowerCase()
    .replaceAll(RegExp(r'[^a-z]'), '')
    .replaceAll('moneragala', 'monaragala');

/// The main town of [district], or null when the name is not one of the 25.
LatLng? townFor(String? district) {
  if (district == null || district.trim().isEmpty) return null;
  final key = _normalise(district);
  for (final entry in districtTowns.entries) {
    if (_normalise(entry.key) == key) return entry.value;
  }
  return null;
}

/// The district whose main town is closest to [point] — a sensible guess to
/// pre-fill, which the reporter can still change.
String nearestDistrict(LatLng point) {
  // Degrees of longitude shrink away from the equator; scale them so the
  // comparison is in roughly equal units.
  final scale = math.cos(point.latitude * math.pi / 180);
  String best = districtTowns.keys.first;
  var bestDistance = double.infinity;

  for (final entry in districtTowns.entries) {
    final dLat = entry.value.latitude - point.latitude;
    final dLng = (entry.value.longitude - point.longitude) * scale;
    final distance = dLat * dLat + dLng * dLng;
    if (distance < bestDistance) {
      bestDistance = distance;
      best = entry.key;
    }
  }
  return best;
}

/// [name] in the spelling [options] uses (the API's district list), falling
/// back to [name] itself when the list does not have it.
String matchDistrict(String name, List<String> options) {
  final key = _normalise(name);
  for (final option in options) {
    if (_normalise(option) == key) return option;
  }
  return name;
}
