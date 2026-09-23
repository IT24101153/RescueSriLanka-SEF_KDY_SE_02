import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

/// A place returned by a search: somewhere on the island with a name.
class Place {
  const Place({
    required this.name,
    required this.latitude,
    required this.longitude,
  });

  /// The full label Nominatim gives, e.g. "Galle, Southern Province".
  final String name;
  final double latitude;
  final double longitude;

  /// The part before the first comma — enough for a list row.
  String get shortName => name.split(',').first.trim();

  factory Place.fromJson(Map<String, dynamic> json) => Place(
        name: (json['display_name'] as String?) ?? 'Unknown place',
        latitude: double.parse(json['lat'] as String),
        longitude: double.parse(json['lon'] as String),
      );
}

/// Thrown when a search cannot be completed.
class PlaceSearchException implements Exception {
  PlaceSearchException(this.message);
  final String message;

  @override
  String toString() => message;
}

/// Place search through Nominatim, OpenStreetMap's own geocoder.
///
/// Free and keyless, like the map tiles, but its usage policy asks for a few
/// things in return, so this client:
///   * identifies the app in User-Agent — requests without one are refused;
///   * searches only on submit, never per keystroke;
///   * asks for at most five results, inside Sri Lanka;
///   * keeps one request in flight, cancelling the previous one.
///
/// https://operations.osmfoundation.org/policies/nominatim/
class PlaceSearchService {
  PlaceSearchService({http.Client? client}) : _client = client ?? http.Client();

  static const String _userAgent = 'RescueSriLanka/1.0 (lk.rescuesrilanka.mobile)';
  static const Duration _timeout = Duration(seconds: 12);

  final http.Client _client;

  /// Rises with each search so a slow earlier response cannot overwrite a
  /// later one.
  int _generation = 0;

  Future<List<Place>> search(String query) async {
    final trimmed = query.trim();
    if (trimmed.isEmpty) return const [];

    final generation = ++_generation;

    final uri = Uri.https('nominatim.openstreetmap.org', '/search', {
      'q': trimmed,
      'format': 'jsonv2',
      'limit': '5',
      // Sri Lanka only: the app's map is locked to the island, so results
      // elsewhere could not be shown anyway.
      'countrycodes': 'lk',
    });

    http.Response response;
    try {
      response = await _client
          .get(uri, headers: const {'User-Agent': _userAgent})
          .timeout(_timeout);
    } on TimeoutException {
      throw PlaceSearchException('Place search timed out. Try again.');
    } catch (_) {
      throw PlaceSearchException('Cannot reach the place search service.');
    }

    // A newer search started while this one was in flight.
    if (generation != _generation) return const [];

    if (response.statusCode != 200) {
      throw PlaceSearchException(
        'Place search failed (HTTP ${response.statusCode}).',
      );
    }

    try {
      final decoded = jsonDecode(response.body) as List<dynamic>;
      return decoded
          .map((item) => Place.fromJson(item as Map<String, dynamic>))
          .toList();
    } catch (_) {
      throw PlaceSearchException('Place search sent back something unreadable.');
    }
  }

  void dispose() => _client.close();
}
