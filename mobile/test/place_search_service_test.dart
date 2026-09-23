import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:mobile/shared/services/place_search_service.dart';

/// One Photon feature, shaped as the live API returns it.
Map<String, dynamic> _feature(
  String name, {
  String countryCode = 'LK',
  double lon = 80.6350358,
  double lat = 7.2931208,
}) => {
  'type': 'Feature',
  'properties': {
    'name': name,
    'county': 'Kandy District',
    'state': 'Central Province',
    'countrycode': countryCode,
  },
  'geometry': {
    'type': 'Point',
    'coordinates': [lon, lat],
  },
};

PlaceSearchService _serviceReturning(List<Map<String, dynamic>> features) =>
    PlaceSearchService(
      client: MockClient(
        (_) async => http.Response(
          jsonEncode({'type': 'FeatureCollection', 'features': features}),
          200,
        ),
      ),
    );

void main() {
  test(
    'reads Photon results, latitude and longitude the right way round',
    () async {
      final places = await _serviceReturning([_feature('Kandy')])
          .search('kand');

      final place = places!.single;
      expect(place.shortName, 'Kandy');
      expect(place.name, 'Kandy, Kandy District, Central Province');
      expect(place.latitude, closeTo(7.29, 0.01));
      expect(place.longitude, closeTo(80.63, 0.01));
    },
  );

  test(
    'drops places outside Sri Lanka that the bounding box lets in',
    () async {
      final places = await _serviceReturning([
        _feature('Kandy'),
        _feature('Rameswaram', countryCode: 'IN'),
      ]).search('ram');

      expect(places!.map((place) => place.shortName), ['Kandy']);
    },
  );

  test('asks Photon for Sri Lanka only', () async {
    late Uri asked;
    final service = PlaceSearchService(
      client: MockClient((request) async {
        asked = request.url;
        return http.Response('{"features": []}', 200);
      }),
    );

    await service.search('  galle ');

    expect(asked.host, 'photon.komoot.io');
    expect(asked.queryParameters['q'], 'galle');
    expect(asked.queryParameters['bbox'], '79.5,5.8,82.0,9.9');
  });

  test('a superseded search returns null rather than "no results"', () async {
    final service = PlaceSearchService(
      client: MockClient((request) async {
        // The first query answers after the second has started.
        if (request.url.queryParameters['q'] == 'ka') {
          await Future<void>.delayed(const Duration(milliseconds: 30));
        }
        return http.Response(
          jsonEncode({
            'features': [_feature('Kandy')],
          }),
          200,
        );
      }),
    );

    final first = service.search('ka');
    final second = service.search('kan');

    expect(await first, isNull);
    expect(await second, hasLength(1));
  });

  test('a server error is reported, not swallowed', () async {
    final service = PlaceSearchService(
      client: MockClient((_) async => http.Response('busy', 503)),
    );

    expect(service.search('kandy'), throwsA(isA<PlaceSearchException>()));
  });
}
