import 'package:flutter/foundation.dart' show kDebugMode;
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:latlong2/latlong.dart';

import '../../../../shared/core/config.dart';
import '../../../../shared/core/theme.dart';
import '../../models/incident.dart';
import '../../models/safety_zone.dart';
import '../../../../shared/services/place_search_service.dart';
import '../../services/api_client.dart';
import '../../widgets/map_legend.dart';
import '../../widgets/severity_chip.dart';
import '../../widgets/zone_banner.dart';
import 'incident_sheet.dart';

/// Component A on mobile: the live disaster map, its safety zones, and the
/// list of what is active. Open to everyone — no sign-in.
class DisasterMapScreen extends StatefulWidget {
  const DisasterMapScreen({super.key});

  @override
  State<DisasterMapScreen> createState() => _DisasterMapScreenState();
}

class _DisasterMapScreenState extends State<DisasterMapScreen> {
  static final LatLngBounds _sriLanka = LatLngBounds(
    const LatLng(AppConfig.minLatitude, AppConfig.minLongitude),
    const LatLng(AppConfig.maxLatitude, AppConfig.maxLongitude),
  );

  /// Keeps the *centre* on the island rather than demanding the whole viewport
  /// fit inside it. CameraConstraint.contain has no valid solution on a wide
  /// window — the visible area is simply bigger than Sri Lanka — and asserts.
  /// Built once: a fresh instance each rebuild re-triggers camera validation.
  static final CameraConstraint _constraint = CameraConstraint.containCenter(
    bounds: _sriLanka,
  );

  static const LatLng _islandCentre = LatLng(7.85, 80.75);

  /// One source of truth for the zoom limits — MapOptions and the zoom buttons
  /// both read these, so they cannot drift apart.
  static const double _minZoom = 6.5;
  static const double _maxZoom = 16;

  final ApiClient _api = ApiClient();
  final MapController _map = MapController();
  final PlaceSearchService _places = PlaceSearchService();
  final TextEditingController _searchField = TextEditingController();

  List<Incident> _incidents = [];
  List<SafetyZone> _zones = [];
  ZoneCheck? _zoneCheck;
  LatLng? _myLocation;

  bool _loading = true;
  bool _showZones = true;
  bool _locating = false;
  String? _error;
  String _severityFilter = 'All';

  /// Place search (Nominatim). Results show until one is picked or dismissed.
  bool _searching = false;
  List<Place> _results = [];
  String? _searchError;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _api.dispose();
    _places.dispose();
    _searchField.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final results = await Future.wait([
        _api.fetchIncidents(),
        _api.fetchZones(),
      ]);
      if (!mounted) return;
      setState(() {
        _incidents = results[0] as List<Incident>;
        _zones = results[1] as List<SafetyZone>;
        _loading = false;
      });
    } on ApiException catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.message;
        _loading = false;
      });
    }
  }

  /// GPS is opt-in: nothing is requested until the user taps the button.
  Future<void> _locateMe() async {
    setState(() => _locating = true);
    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        _toast('Location services are turned off on this device.');
        return;
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        _toast('Location permission denied.');
        return;
      }

      final position = await Geolocator.getCurrentPosition();
      final here = LatLng(position.latitude, position.longitude);

      final check = await _api.checkZone(
        latitude: position.latitude,
        longitude: position.longitude,
      );

      if (!mounted) return;
      setState(() {
        _myLocation = here;
        _zoneCheck = check;
      });
      _map.move(here, 11);
    } on ApiException catch (error) {
      _toast(error.message);
    } catch (_) {
      _toast('Could not get your location.');
    } finally {
      if (mounted) setState(() => _locating = false);
    }
  }

  /// Steps the zoom, holding the current centre.
  ///
  Future<void> _searchPlaces(String query) async {
    if (query.trim().isEmpty) {
      setState(() {
        _results = [];
        _searchError = null;
      });
      return;
    }

    setState(() {
      _searching = true;
      _searchError = null;
    });

    try {
      final results = await _places.search(query);
      if (!mounted) return;
      setState(() {
        _results = results;
        _searchError = results.isEmpty ? 'No place matched "$query".' : null;
      });
    } on PlaceSearchException catch (error) {
      if (!mounted) return;
      setState(() {
        _results = [];
        _searchError = error.message;
      });
    } finally {
      if (mounted) setState(() => _searching = false);
    }
  }

  void _goToPlace(Place place) {
    FocusScope.of(context).unfocus();
    setState(() {
      _results = [];
      _searchError = null;
      _searchField.text = place.shortName;
    });
    _map.move(LatLng(place.latitude, place.longitude), 12);
  }

  void _clearSearch() {
    FocusScope.of(context).unfocus();
    _searchField.clear();
    setState(() {
      _results = [];
      _searchError = null;
    });
  }

  /// Clamped to the same limits MapOptions declares — moving outside them is
  /// silently ignored by flutter_map, which reads as a dead button.
  void _zoomBy(double delta) {
    final camera = _map.camera;
    final target = (camera.zoom + delta).clamp(_minZoom, _maxZoom);

    if (target == camera.zoom) {
      if (kDebugMode) {
        debugPrint('Zoom: already at the limit (${camera.zoom}).');
      }
      return;
    }

    final accepted = _map.move(camera.center, target);

    if (kDebugMode) {
      debugPrint(
        'Zoom: ${camera.zoom} -> $target '
        '(move accepted: $accepted, now ${_map.camera.zoom})',
      );
    }
  }

  void _toast(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  List<Incident> get _visible => _severityFilter == 'All'
      ? _incidents
      : _incidents.where((i) => i.severity == _severityFilter).toList();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Rescue SriLanka',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        actions: [
          IconButton(
            onPressed: () => setState(() => _showZones = !_showZones),
            icon: Icon(_showZones ? Icons.layers : Icons.layers_outlined),
            tooltip: _showZones ? 'Hide safety zones' : 'Show safety zones',
          ),
          IconButton(
            onPressed: _loading ? null : _load,
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
          ),
        ],
      ),
      body: _content(),
    );
  }

  /// The map itself needs no API, so a failed load is a banner over it rather
  /// than a screen that hides the map.
  Widget _errorBanner() => Material(
    color: AppColors.surfaceAlt,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(16, 6, 8, 6),
      child: Row(
        children: [
          const Icon(Icons.cloud_off, size: 18, color: AppColors.body),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              'Incidents and zones are unavailable. $_error',
              style: const TextStyle(fontSize: 12, height: 1.35),
            ),
          ),
          TextButton(onPressed: _load, child: const Text('Retry')),
        ],
      ),
    ),
  );

  Widget _content() {
    return Column(
      children: [
        if (_error != null) _errorBanner(),
        _searchBar(),
        if (_results.isNotEmpty || _searchError != null) _searchResults(),
        _summaryBar(),
        _filterBar(),
        Expanded(
          child: Stack(
            children: [
              _mapView(),
              if (_loading)
                const Positioned.fill(
                  child: ColoredBox(
                    color: Color(0x66FFFFFF),
                    child: Center(child: CircularProgressIndicator()),
                  ),
                ),
              Positioned(
                left: 12,
                bottom: 12,
                child: MapLegend(showZones: _showZones),
              ),
              Positioned(
                right: 12,
                bottom: 12,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    // Pinch-to-zoom works on a real device but is awkward on an
                    // emulator and impossible one-handed, so the map carries
                    // explicit controls like the web console does.
                    FloatingActionButton.small(
                      heroTag: 'zoom-in',
                      onPressed: () => _zoomBy(1),
                      backgroundColor: AppColors.surface,
                      foregroundColor: AppColors.ink,
                      tooltip: 'Zoom in',
                      child: const Icon(Icons.add, size: 20),
                    ),
                    const SizedBox(height: 8),
                    FloatingActionButton.small(
                      heroTag: 'zoom-out',
                      onPressed: () => _zoomBy(-1),
                      backgroundColor: AppColors.surface,
                      foregroundColor: AppColors.ink,
                      tooltip: 'Zoom out',
                      child: const Icon(Icons.remove, size: 20),
                    ),
                    const SizedBox(height: 8),
                    FloatingActionButton.small(
                      heroTag: 'locate',
                      onPressed: _locating ? null : _locateMe,
                      backgroundColor: AppColors.surface,
                      foregroundColor: AppColors.ink,
                      tooltip: 'Show my location',
                      child: _locating
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.my_location, size: 20),
                    ),
                  ],
                ),
              ),
              if (_zoneCheck != null)
                Positioned(
                  left: 12,
                  right: 12,
                  top: 12,
                  child: ZoneBanner(
                    check: _zoneCheck!,
                    onDismiss: () => setState(() => _zoneCheck = null),
                  ),
                ),
            ],
          ),
        ),
        if (_visible.isNotEmpty || _loading) _incidentList(),
      ],
    );
  }

  /// Place search over Nominatim. It runs on submit rather than per
  /// keystroke, which their usage policy asks for.
  Widget _searchBar() => Padding(
    padding: const EdgeInsets.fromLTRB(12, 10, 12, 4),
    child: TextField(
      controller: _searchField,
      textInputAction: TextInputAction.search,
      onSubmitted: _searchPlaces,
      // Rebuild so the clear button appears as soon as there is text. The
      // search itself still waits for submit.
      onChanged: (_) => setState(() {}),
      decoration: InputDecoration(
        isDense: true,
        hintText: 'Search a place in Sri Lanka',
        prefixIcon: const Icon(Icons.search, size: 20),
        suffixIcon: _searching
            ? const Padding(
                padding: EdgeInsets.all(12),
                child: SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              )
            : (_searchField.text.isEmpty
                ? null
                : IconButton(
                    icon: const Icon(Icons.close, size: 18),
                    onPressed: _clearSearch,
                    tooltip: 'Clear search',
                  )),
        filled: true,
        fillColor: AppColors.surfaceAlt,
        contentPadding: const EdgeInsets.symmetric(vertical: 10),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: AppColors.border),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: AppColors.border),
        ),
      ),
    ),
  );

  Widget _searchResults() => Container(
    margin: const EdgeInsets.fromLTRB(12, 0, 12, 6),
    decoration: BoxDecoration(
      color: AppColors.surface,
      border: Border.all(color: AppColors.border),
      borderRadius: BorderRadius.circular(10),
    ),
    child: _searchError != null
        ? Padding(
            padding: const EdgeInsets.all(12),
            child: Text(
              _searchError!,
              style: const TextStyle(fontSize: 13, color: AppColors.body),
            ),
          )
        : Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final place in _results)
                ListTile(
                  dense: true,
                  visualDensity: VisualDensity.compact,
                  leading: const Icon(
                    Icons.place_outlined,
                    size: 20,
                    color: AppColors.body,
                  ),
                  title: Text(
                    place.shortName,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  subtitle: Text(
                    place.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 11.5),
                  ),
                  onTap: () => _goToPlace(place),
                ),
              // Nominatim's policy asks for this credit alongside results.
              const Padding(
                padding: EdgeInsets.fromLTRB(12, 0, 12, 8),
                child: Align(
                  alignment: Alignment.centerRight,
                  child: Text(
                    'Search by OpenStreetMap Nominatim',
                    style: TextStyle(fontSize: 10.5, color: AppColors.body),
                  ),
                ),
              ),
            ],
          ),
  );

  Widget _summaryBar() {
    final active = _incidents.length;
    final critical = _incidents.where((i) => i.severity == 'Critical').length;
    final danger = _zones.where((z) => z.status == 'Danger').length;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 7),
      decoration: const BoxDecoration(
        color: AppColors.surfaceAlt,
        border: Border(bottom: BorderSide(color: AppColors.border)),
      ),
      child: Row(
        children: [
          _stat('$active', 'Active', AppColors.ink),
          _stat('$critical', 'Critical', AppColors.critical),
          _stat('$danger', 'Danger zones', AppColors.danger),
        ],
      ),
    );
  }

  Widget _stat(String value, String label, Color color) => Expanded(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          value,
          style: TextStyle(
            fontSize: 19,
            fontWeight: FontWeight.w700,
            color: color,
          ),
        ),
        Text(label, style: const TextStyle(fontSize: 11.5)),
      ],
    ),
  );

  Widget _filterBar() {
    const options = ['All', 'Critical', 'High', 'Moderate', 'Low'];

    return SizedBox(
      height: 46,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
        itemCount: options.length,
        separatorBuilder: (context, index) => const SizedBox(width: 7),
        itemBuilder: (context, index) {
          final option = options[index];
          final selected = option == _severityFilter;
          return ChoiceChip(
            label: Text(option),
            selected: selected,
            onSelected: (_) => setState(() => _severityFilter = option),
            labelStyle: TextStyle(
              fontSize: 12.5,
              color: selected ? AppColors.brandInk : AppColors.body,
              fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
            ),
            selectedColor: AppColors.brand,
            backgroundColor: AppColors.surface,
            side: const BorderSide(color: AppColors.border),
            showCheckmark: false,
          );
        },
      ),
    );
  }

  Widget _mapView() {
    return FlutterMap(
      mapController: _map,
      options: MapOptions(
        initialCenter: _islandCentre,
        initialZoom: 7.2,
        // Panning away from Sri Lanka is never useful here.
        cameraConstraint: _constraint,
        minZoom: _minZoom,
        maxZoom: _maxZoom,
      ),
      children: [
        // OpenStreetMap's own tiles — keyless, so nothing sensitive ships in
        // the APK and the map draws whether or not our API is reachable.
        // Their usage policy requires a real User-Agent, which
        // userAgentPackageName supplies.
        TileLayer(
          urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
          userAgentPackageName: 'lk.rescuesrilanka.mobile',
          // A tile that fails to load is otherwise just a grey square, which
          // hides the reason.
          errorTileCallback: (tile, error, stackTrace) {
            debugPrint('Map tile failed: $error');
          },
        ),
        if (_showZones)
          CircleLayer(
            circles: [
              for (final zone in _zones)
                CircleMarker(
                  point: LatLng(zone.centerLatitude, zone.centerLongitude),
                  radius: zone.radiusMeters.toDouble(),
                  useRadiusInMeter: true,
                  color: AppColors.forZone(zone.status).withValues(alpha: 0.13),
                  borderColor: AppColors.forZone(zone.status),
                  borderStrokeWidth: 1.4,
                ),
            ],
          ),
        if (_myLocation != null)
          CircleLayer(
            circles: [
              CircleMarker(
                point: _myLocation!,
                radius: 9,
                color: Colors.blue.withValues(alpha: 0.9),
                borderColor: Colors.white,
                borderStrokeWidth: 2.5,
              ),
            ],
          ),
        MarkerLayer(
          markers: [
            for (final incident in _visible)
              Marker(
                point: LatLng(incident.latitude, incident.longitude),
                width: 34,
                height: 34,
                child: GestureDetector(
                  onTap: () => IncidentSheet.show(context, incident),
                  child: _marker(incident),
                ),
              ),
          ],
        ),
        // OpenStreetMap's tile policy requires crediting contributors.
        const RichAttributionWidget(
          attributions: [
            TextSourceAttribution('OpenStreetMap contributors'),
          ],
        ),
      ],
    );
  }

  /// Colour, size and icon all encode severity — three cues, not one.
  Widget _marker(Incident incident) {
    final color = AppColors.forSeverity(incident.severity);
    final size = AppColors.radiusForSeverity(incident.severity) * 2;

    return Center(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: color,
          shape: BoxShape.circle,
          border: Border.all(color: Colors.white, width: 2),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: 0.22),
              blurRadius: 4,
              offset: const Offset(0, 1),
            ),
          ],
        ),
        child: Icon(
          AppColors.iconForSeverity(incident.severity),
          size: size * 0.55,
          color: Colors.white,
        ),
      ),
    );
  }

  Widget _incidentList() {
    final visible = _visible;

    return Container(
      height: 148,
      decoration: const BoxDecoration(
        color: AppColors.surface,
        border: Border(top: BorderSide(color: AppColors.border)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 11, 16, 7),
            child: Text(
              _loading
                  ? 'Loading incidents…'
                  : '${visible.length} active incident${visible.length == 1 ? '' : 's'}',
              style: const TextStyle(
                fontSize: 11.5,
                fontWeight: FontWeight.w700,
                letterSpacing: 0.5,
                color: AppColors.body,
              ),
            ),
          ),
          Expanded(
            child: visible.isEmpty && !_loading
                ? const Center(
                    child: Text(
                      'No incidents match this filter.',
                      style: TextStyle(fontSize: 13),
                    ),
                  )
                : ListView.separated(
                    scrollDirection: Axis.horizontal,
                    padding: const EdgeInsets.fromLTRB(14, 0, 14, 14),
                    itemCount: visible.length,
                    separatorBuilder: (context, index) =>
                        const SizedBox(width: 10),
                    itemBuilder: (context, index) => _card(visible[index]),
                  ),
          ),
        ],
      ),
    );
  }

  Widget _card(Incident incident) {
    return GestureDetector(
      onTap: () {
        _map.move(LatLng(incident.latitude, incident.longitude), 12);
        IncidentSheet.show(context, incident);
      },
      child: Container(
        width: 232,
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: AppColors.surface,
          border: Border.all(color: AppColors.border),
          borderRadius: BorderRadius.circular(13),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SeverityChip(severity: incident.severity, compact: true),
            const SizedBox(height: 8),
            Text(
              incident.title,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
                color: AppColors.ink,
                height: 1.3,
              ),
            ),
            const Spacer(),
            Text(
              '${incident.type} · ${incident.district ?? 'Unknown'}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 11.5),
            ),
          ],
        ),
      ),
    );
  }
}
