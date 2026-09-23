import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

/// A place returned by a search: somewhere on the island with a name.
class Place {
  const Place({
    required this.shortName,
    required this.name,
    required this.latitude,
    required this.longitude,
  });

  /// The place itself, e.g. "Kandy" — enough for a list row.
  final String shortName;

  /// Where it is, e.g. "Kandy, Kandy District, Central Province".
  final String name;

  final double latitude;
  final double longitude;

  /// Photon returns GeoJSON features: coordinates are [longitude, latitude],
  /// and the address parts are separate fields rather than one label.
  static Place? fromPhoton(Map<String, dynamic> feature) {
    final properties = feature['properties'] as Map<String, dynamic>? ?? {};
    final coordinates =
        (feature['geometry'] as Map<String, dynamic>?)?['coordinates'] as List?;
    final title =
        properties['name'] as String? ??
        properties['street'] as String? ??
        properties['city'] as String?;

    if (coordinates == null || coordinates.length < 2 || title == null) {
      return null;
    }

    // Broadest last, skipping repeats: "Kandy, Kandy" says nothing twice.
    final parts = <String>[title];
    for (final key in ['street', 'locality', 'city', 'county', 'state']) {
      final part = properties[key] as String?;
      if (part != null && part.isNotEmpty && !parts.contains(part)) {
        parts.add(part);
      }
    }

    return Place(
      shortName: title,
      name: parts.join(', '),
      latitude: (coordinates[1] as num).toDouble(),
      longitude: (coordinates[0] as num).toDouble(),
    );
  }
}

/// Thrown when a search cannot be completed.
class PlaceSearchException implements Exception {
  PlaceSearchException(this.message);
  final String message;

  @override
  String toString() => message;
}

/// Place search through Photon, an OpenStreetMap geocoder built for
/// search-as-you-type: it matches partial words, so "kand" already finds
/// Kandy. (Nominatim, used before, forbids autocomplete in its usage policy
/// and only matches whole words, which is why typing showed nothing.)
///
/// Photon is free and keyless on a fair-use basis, so this client:
///   * is called by the map only after typing pauses, never per keystroke;
///   * asks for at most six results, inside Sri Lanka's bounding box;
///   * keeps one request current, dropping replies to superseded ones.
///
/// https://photon.komoot.io
class PlaceSearchService {
  PlaceSearchService({http.Client? client}) : _client = client ?? http.Client();

  static const String _userAgent =
      'RescueSriLanka/1.0 (lk.rescuesrilanka.mobile)';
  static const Duration _timeout = Duration(seconds: 12);

  /// Sri Lanka, as west,south,east,north.
  static const String _sriLanka = '79.5,5.8,82.0,9.9';

  final http.Client _client;

  /// Rises with each search so a slow earlier response cannot overwrite a
  /// later one.
  int _generation = 0;

  /// Matching places, best first. Null when a newer search has started since
  /// — the caller should ignore it rather than show "no results".
  Future<List<Place>?> search(String query) async {
    final trimmed = query.trim();
    if (trimmed.isEmpty) return const [];

    final generation = ++_generation;

    final uri = Uri.https('photon.komoot.io', '/api/', {
      'q': trimmed,
      'limit': '6',
      'lang': 'en',
      'bbox': _sriLanka,
    });

    http.Response response;
    try {
      response = await _client
          .get(uri, headers: const {'User-Agent': _userAgent})
          .timeout(_timeout);
    } on TimeoutException {
      if (generation != _generation) return null;
      throw PlaceSearchException('Place search timed out. Try again.');
    } catch (_) {
      if (generation != _generation) return null;
      throw PlaceSearchException('Cannot reach the place search service.');
    }

    if (generation != _generation) return null;

    if (response.statusCode != 200) {
      throw PlaceSearchException(
        'Place search failed (HTTP ${response.statusCode}).',
      );
    }

    try {
      final decoded = jsonDecode(response.body) as Map<String, dynamic>;
      final features = decoded['features'] as List<dynamic>? ?? const [];
      return [
        for (final feature in features.cast<Map<String, dynamic>>())
          // The bounding box grazes India's southern tip; keep the island.
          if ((feature['properties'] as Map?)?['countrycode'] == 'LK')
            ?Place.fromPhoton(feature),
      ];
    } catch (_) {
      throw PlaceSearchException(
        'Place search sent back something unreadable.',
      );
    }
  }

  void dispose() => _client.close();
}
