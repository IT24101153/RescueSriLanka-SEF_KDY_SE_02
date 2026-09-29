import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
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
import '../report/report_visuals.dart';
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
  final DraggableScrollableController _sheet = DraggableScrollableController();
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

  /// The incident last tapped, marked on the map so it can be found again.
  String? _selectedId;

  /// The legend opens on demand rather than covering the map all the time.
  bool _showLegend = false;

  /// Place search. Suggestions show as you type, until one is picked or
  /// the search is cleared; the picked place stays pinned on the map.
  bool _searching = false;
  List<Place> _results = [];
  String? _searchError;
  Place? _pickedPlace;
  Timer? _searchDebounce;

  /// How long typing must pause before a search goes out — short enough to
  /// feel live, long enough not to send one request per keystroke.
  static const Duration _searchPause = Duration(milliseconds: 350);
  static const int _minSearchLength = 2;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _api.dispose();
    _sheet.dispose();
    _searchDebounce?.cancel();
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
  /// Called on every keystroke; the search itself waits for a pause.
  void _onSearchChanged(String query) {
    _searchDebounce?.cancel();

    if (query.trim().length < _minSearchLength) {
      setState(() {
        _results = [];
        _searchError = null;
        _searching = false;
      });
      return;
    }

    setState(() {});
    _searchDebounce = Timer(_searchPause, () => _searchPlaces(query));
  }

  Future<void> _searchPlaces(String query) async {
    _searchDebounce?.cancel();

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
      // Null: a newer search has started, and its answer is the one to show.
      if (!mounted || results == null) return;
      setState(() {
        _results = results;
        _searchError = results.isEmpty ? 'No place matched "$query".' : null;
        _searching = false;
      });
    } on PlaceSearchException catch (error) {
      if (!mounted) return;
      setState(() {
        _results = [];
        _searchError = error.message;
        _searching = false;
      });
    }
  }

  void _goToPlace(Place place) {
    FocusScope.of(context).unfocus();
    _searchDebounce?.cancel();
    setState(() {
      _results = [];
      _searchError = null;
      _searching = false;
      _pickedPlace = place;
      _searchField.text = place.shortName;
    });
    _map.move(LatLng(place.latitude, place.longitude), 13);
  }

  void _clearSearch() {
    FocusScope.of(context).unfocus();
    _searchDebounce?.cancel();
    _searchField.clear();
    setState(() {
      _results = [];
      _searchError = null;
      _searching = false;
      _pickedPlace = null;
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


  int _countFor(String severity) => severity == 'All'
      ? _incidents.length
      : _incidents.where((i) => i.severity == severity).length;

  /// Centres the map on an incident, marks it, and opens its details. The
  /// list sheet drops back down so the marker is not hidden behind it.
  void _focus(Incident incident) {
    FocusScope.of(context).unfocus();
    setState(() => _selectedId = incident.id);
    _map.move(LatLng(incident.latitude, incident.longitude), 13);
    if (_sheet.isAttached) {
      _sheet.animateTo(
        _sheetMin,
        duration: const Duration(milliseconds: 250),
        curve: Curves.easeOut,
      );
    }
    IncidentSheet.show(context, incident);
  }

  void _resetView() => _map.move(_islandCentre, 7.2);

  /// How tall the list sheet is when pulled down: its handle, heading and
  /// counts, plus whatever sits over the bottom of the screen.
  static const double _sheetPeek = 128;

  /// Fraction of the screen the collapsed sheet takes, set each layout.
  double _sheetMin = 0.2;

  @override
  Widget build(BuildContext context) {
    // On iOS the glass tab bar floats over the bottom of the screen, so the
    // sheet and the map controls sit higher to stay clear of it.
    final bottomInset = MediaQuery.paddingOf(context).bottom +
        (defaultTargetPlatform == TargetPlatform.iOS ? 84 : 0);

    return AnnotatedRegion<SystemUiOverlayStyle>(
      // The map runs under the status bar, so its icons must stay dark.
      value: SystemUiOverlayStyle.dark,
      child: Scaffold(
        body: LayoutBuilder(
          builder: (context, constraints) {
            final height = constraints.maxHeight;
            final peek = _sheetPeek + bottomInset;
            _sheetMin = (peek / height).clamp(0.12, 0.4).toDouble();
            final mid = math.max(_sheetMin + 0.05, 0.46);

            return Stack(
              children: [
                Positioned.fill(child: _mapView()),
                if (_loading && _incidents.isEmpty)
                  const Positioned.fill(
                    child: IgnorePointer(
                      child: ColoredBox(color: Color(0x33FFFFFF)),
                    ),
                  ),
                _topOverlay(),
                Positioned(
                  right: 12,
                  bottom: peek + 12,
                  child: _mapControls(),
                ),
                if (_showLegend)
                  Positioned(
                    left: 12,
                    bottom: peek + 12,
                    child: GestureDetector(
                      onTap: () => setState(() => _showLegend = false),
                      child: MapLegend(showZones: _showZones),
                    ),
                  ),
                DraggableScrollableSheet(
                  controller: _sheet,
                  initialChildSize: _sheetMin,
                  minChildSize: _sheetMin,
                  maxChildSize: 0.9,
                  snap: true,
                  snapSizes: [mid],
                  builder: (context, scroll) => _incidentSheet(scroll, bottomInset),
                ),
              ],
            );
          },
        ),
      ),
    );
  }

  // ------------------------------------------------------------ top overlay

  /// Search, filters and any alert, floating over the top of the map. Only
  /// the cards themselves take touches; the gaps between them are map.
  Widget _topOverlay() {
    return SafeArea(
      bottom: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _searchBar(),
            if (_results.isNotEmpty || _searchError != null) ...[
              const SizedBox(height: 6),
              _searchResults(),
            ],
            const SizedBox(height: 8),
            _filterBar(),
            if (_error != null) ...[
              const SizedBox(height: 8),
              _errorBanner(),
            ],
            if (_zoneCheck != null) ...[
              const SizedBox(height: 8),
              ZoneBanner(
                check: _zoneCheck!,
                onDismiss: () => setState(() => _zoneCheck = null),
              ),
            ],
          ],
        ),
      ),
    );
  }

  static const List<BoxShadow> _floatShadow = [
    BoxShadow(color: Color(0x22000000), blurRadius: 14, offset: Offset(0, 4)),
  ];

  /// Place search, with the zone toggle and refresh folded into the same
  /// card so the top of the map carries one bar, not three.
  Widget _searchBar() {
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(14),
        boxShadow: _floatShadow,
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              const Padding(
                padding: EdgeInsets.only(left: 14, right: 6),
                child: Icon(Icons.search, size: 21, color: AppColors.body),
              ),
              Expanded(
                child: TextField(
                  controller: _searchField,
                  textInputAction: TextInputAction.search,
                  onSubmitted: _searchPlaces,
                  onChanged: _onSearchChanged,
                  style: const TextStyle(fontSize: 15, color: AppColors.ink),
                  decoration: const InputDecoration(
                    hintText: 'Search a place in Sri Lanka',
                    border: InputBorder.none,
                    isDense: true,
                    contentPadding: EdgeInsets.symmetric(vertical: 15),
                  ),
                ),
              ),
              if (_searching)
                const Padding(
                  padding: EdgeInsets.all(12),
                  child: SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                )
              else if (_searchField.text.isNotEmpty)
                IconButton(
                  icon: const Icon(Icons.close, size: 19),
                  onPressed: _clearSearch,
                  tooltip: 'Clear search',
                ),
              Container(width: 1, height: 26, color: AppColors.border),
              IconButton(
                onPressed: () => setState(() => _showZones = !_showZones),
                icon: Icon(
                  _showZones ? Icons.layers : Icons.layers_outlined,
                  color: _showZones ? AppColors.brandInk : AppColors.body,
                ),
                style: IconButton.styleFrom(
                  backgroundColor: _showZones
                      ? AppColors.brand.withValues(alpha: 0.2)
                      : null,
                ),
                tooltip: _showZones ? 'Hide safety zones' : 'Show safety zones',
              ),
              IconButton(
                onPressed: _loading ? null : _load,
                icon: const Icon(Icons.refresh, color: AppColors.body),
                tooltip: 'Refresh',
              ),
              const SizedBox(width: 4),
            ],
          ),
          // Loading shows as a thin line along the bar, never a screen that
          // hides the map.
          SizedBox(
            height: 2,
            child: _loading
                ? const LinearProgressIndicator(minHeight: 2)
                : null,
          ),
        ],
      ),
    );
  }

  Widget _searchResults() => Container(
    decoration: BoxDecoration(
      color: AppColors.surface,
      borderRadius: BorderRadius.circular(14),
      boxShadow: _floatShadow,
    ),
    clipBehavior: Clip.antiAlias,
    child: _searchError != null
        ? Padding(
            padding: const EdgeInsets.all(14),
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
                  leading: Container(
                    width: 32,
                    height: 32,
                    decoration: const BoxDecoration(
                      color: AppColors.surfaceAlt,
                      shape: BoxShape.circle,
                    ),
                    child: const Icon(
                      Icons.place_outlined,
                      size: 18,
                      color: AppColors.body,
                    ),
                  ),
                  title: Text(
                    place.shortName,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w600,
                      color: AppColors.ink,
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
              // Photon's results are OpenStreetMap data, which asks for credit.
              const Padding(
                padding: EdgeInsets.fromLTRB(12, 0, 12, 8),
                child: Align(
                  alignment: Alignment.centerRight,
                  child: Text(
                    'Search by Photon · OpenStreetMap',
                    style: TextStyle(fontSize: 10.5, color: AppColors.body),
                  ),
                ),
              ),
            ],
          ),
  );

  /// Severity filter, each chip carrying how many incidents it would show.
  Widget _filterBar() {
    const options = ['All', 'Critical', 'High', 'Moderate', 'Low'];

    return SizedBox(
      height: 38,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        clipBehavior: Clip.none,
        itemCount: options.length,
        separatorBuilder: (context, index) => const SizedBox(width: 7),
        itemBuilder: (context, index) {
          final option = options[index];
          final selected = option == _severityFilter;
          final tone = option == 'All'
              ? AppColors.ink
              : AppColors.forSeverity(option);

          return Semantics(
            button: true,
            selected: selected,
            child: GestureDetector(
              onTap: () => setState(() => _severityFilter = option),
              child: AnimatedContainer(
                duration: const Duration(milliseconds: 160),
                padding: const EdgeInsets.symmetric(horizontal: 12),
                decoration: BoxDecoration(
                  color: selected ? AppColors.brand : AppColors.surface,
                  borderRadius: BorderRadius.circular(999),
                  boxShadow: _floatShadow,
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (option != 'All') ...[
                      Icon(
                        AppColors.iconForSeverity(option),
                        size: 15,
                        color: selected ? AppColors.brandInk : tone,
                      ),
                      const SizedBox(width: 5),
                    ],
                    Text(
                      option,
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: selected ? FontWeight.w700 : FontWeight.w600,
                        color: selected ? AppColors.brandInk : AppColors.ink,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      '${_countFor(option)}',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                        color: selected ? AppColors.brandInk : AppColors.body,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    );
  }

  /// The map itself needs no API, so a failed load is a card over it rather
  /// than a screen that hides the map.
  Widget _errorBanner() => Container(
    padding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
    decoration: BoxDecoration(
      color: AppColors.surface,
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: AppColors.high.withValues(alpha: 0.4)),
      boxShadow: _floatShadow,
    ),
    child: Row(
      children: [
        const Icon(Icons.cloud_off, size: 19, color: AppColors.high),
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
  );

  // ------------------------------------------------------------ map controls

  /// Pinch-to-zoom works on a real device but is awkward on an emulator and
  /// impossible one-handed, so the map carries explicit controls too.
  Widget _mapControls() {
    Widget group(List<Widget> children) => Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        boxShadow: _floatShadow,
      ),
      child: Column(mainAxisSize: MainAxisSize.min, children: children),
    );

    Widget button(IconData icon, String tip, VoidCallback? onTap,
            {bool active = false}) =>
        IconButton(
          onPressed: onTap,
          icon: Icon(icon, size: 21),
          color: active ? AppColors.brandInk : AppColors.ink,
          style: IconButton.styleFrom(
            backgroundColor:
                active ? AppColors.brand.withValues(alpha: 0.2) : null,
          ),
          tooltip: tip,
        );

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        group([
          button(Icons.add, 'Zoom in', () => _zoomBy(1)),
          Container(width: 26, height: 1, color: AppColors.border),
          button(Icons.remove, 'Zoom out', () => _zoomBy(-1)),
        ]),
        const SizedBox(height: 10),
        group([
          button(
            Icons.info_outline,
            _showLegend ? 'Hide legend' : 'Show legend',
            () => setState(() => _showLegend = !_showLegend),
            active: _showLegend,
          ),
          Container(width: 26, height: 1, color: AppColors.border),
          button(Icons.zoom_out_map, 'Show all of Sri Lanka', _resetView),
        ]),
        const SizedBox(height: 10),
        // The one-tap answer to "am I safe here?", so it stands out.
        Material(
          color: AppColors.brand,
          shape: const CircleBorder(),
          elevation: 3,
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: _locating ? null : _locateMe,
            child: SizedBox(
              width: 52,
              height: 52,
              child: Center(
                child: _locating
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(
                          strokeWidth: 2.2,
                          valueColor: AlwaysStoppedAnimation(AppColors.brandInk),
                        ),
                      )
                    : const Icon(
                        Icons.my_location,
                        size: 23,
                        color: AppColors.brandInk,
                        semanticLabel: 'Show my location and check if it is safe',
                      ),
              ),
            ),
          ),
        ),
      ],
    );
  }

  // --------------------------------------------------------------------- map

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
        onTap: (_, _) {
          FocusScope.of(context).unfocus();
          if (_selectedId != null) setState(() => _selectedId = null);
        },
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
              // A soft halo first, so the dot reads as "you" at any zoom.
              CircleMarker(
                point: _myLocation!,
                radius: 22,
                color: Colors.blue.withValues(alpha: 0.15),
                borderStrokeWidth: 0,
              ),
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
                width: 44,
                height: 44,
                child: GestureDetector(
                  onTap: () {
                    setState(() => _selectedId = incident.id);
                    IncidentSheet.show(context, incident);
                  },
                  child: _marker(incident, selected: incident.id == _selectedId),
                ),
              ),
            // The searched-for place, pinned so it can be found again after
            // the map is panned. Its tip sits on the spot.
            if (_pickedPlace case final place?)
              Marker(
                point: LatLng(place.latitude, place.longitude),
                width: 40,
                height: 40,
                alignment: Alignment.topCenter,
                child: Tooltip(
                  message: place.name,
                  child: const Icon(
                    Icons.location_on,
                    size: 40,
                    color: AppColors.brand,
                    shadows: [Shadow(color: Colors.black38, blurRadius: 4)],
                  ),
                ),
              ),
          ],
        ),
        // The OpenStreetMap credit its tile policy requires is in the list
        // sheet's header — the sheet would cover it down here.
      ],
    );
  }

  /// Colour, size and icon all encode severity — three cues, not one. The
  /// marker last tapped grows and gains a ring so it can be found again.
  Widget _marker(Incident incident, {bool selected = false}) {
    final color = AppColors.forSeverity(incident.severity);
    final size = AppColors.radiusForSeverity(incident.severity) * 2 +
        (selected ? 10 : 0);

    return Center(
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 180),
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: color,
          shape: BoxShape.circle,
          border: Border.all(
            color: selected ? AppColors.ink : Colors.white,
            width: selected ? 3 : 2,
          ),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: 0.25),
              blurRadius: selected ? 8 : 4,
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

  // ------------------------------------------------------------ list sheet

  /// Pulled down it is a summary; pulled up, the full list of what is active.
  Widget _incidentSheet(ScrollController scroll, double bottomInset) {
    final visible = _visible;
    final critical = _incidents.where((i) => i.severity == 'Critical').length;
    final danger = _zones.where((z) => z.status == 'Danger').length;

    return Container(
      decoration: const BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.vertical(top: Radius.circular(22)),
        boxShadow: [
          BoxShadow(color: Color(0x26000000), blurRadius: 20, offset: Offset(0, -2)),
        ],
      ),
      child: ListView.builder(
        controller: scroll,
        padding: EdgeInsets.only(bottom: bottomInset + 16),
        // Header, then a row per incident (or the empty message).
        itemCount: 1 + (visible.isEmpty ? 1 : visible.length),
        itemBuilder: (context, index) {
          if (index == 0) {
            return _sheetHeader(
              shown: visible.length,
              critical: critical,
              danger: danger,
            );
          }
          if (visible.isEmpty) {
            return Padding(
              padding: const EdgeInsets.fromLTRB(24, 28, 24, 12),
              child: Text(
                _loading
                    ? 'Loading incidents…'
                    : _severityFilter == 'All'
                    ? 'No active incidents right now.'
                    : 'No $_severityFilter incidents right now.',
                textAlign: TextAlign.center,
                style: const TextStyle(fontSize: 13.5),
              ),
            );
          }
          return _incidentTile(visible[index - 1], last: index == visible.length);
        },
      ),
    );
  }

  Widget _sheetHeader({
    required int shown,
    required int critical,
    required int danger,
  }) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Center(
            child: Container(
              width: 40,
              height: 4,
              decoration: BoxDecoration(
                color: AppColors.border,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: Text(
                  _severityFilter == 'All'
                      ? 'Active incidents'
                      : '$_severityFilter incidents',
                  style: const TextStyle(
                    fontSize: 17,
                    fontWeight: FontWeight.w700,
                    color: AppColors.ink,
                  ),
                ),
              ),
              const Text(
                'Swipe up for list',
                style: TextStyle(fontSize: 11.5, color: AppColors.body),
              ),
            ],
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              _statTile('$shown', 'Showing', AppColors.ink),
              const SizedBox(width: 8),
              _statTile('$critical', 'Critical', AppColors.critical),
              const SizedBox(width: 8),
              _statTile('$danger', 'Danger zones', AppColors.danger),
            ],
          ),
          const SizedBox(height: 6),
          const Text(
            'Map data © OpenStreetMap contributors',
            style: TextStyle(fontSize: 10, color: AppColors.body),
          ),
        ],
      ),
    );
  }

  Widget _statTile(String value, String label, Color tone) => Expanded(
    child: Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      decoration: BoxDecoration(
        color: AppColors.surfaceAlt,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        children: [
          Text(
            value,
            style: TextStyle(
              fontSize: 17,
              fontWeight: FontWeight.w700,
              color: tone,
            ),
          ),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 11.5),
            ),
          ),
        ],
      ),
    ),
  );

  Widget _incidentTile(Incident incident, {required bool last}) {
    final tone = AppColors.forSeverity(incident.severity);
    final selected = incident.id == _selectedId;

    return Material(
      color: selected ? AppColors.brand.withValues(alpha: 0.08) : Colors.transparent,
      child: InkWell(
        onTap: () => _focus(incident),
        child: Container(
          padding: const EdgeInsets.fromLTRB(16, 12, 10, 12),
          decoration: BoxDecoration(
            border: last
                ? null
                : const Border(bottom: BorderSide(color: AppColors.border)),
          ),
          child: Row(
            children: [
              Container(
                width: 44,
                height: 44,
                decoration: BoxDecoration(
                  color: tone.withValues(alpha: 0.13),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Icon(iconForType(incident.type), size: 23, color: tone),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      incident.title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 14.5,
                        height: 1.3,
                        fontWeight: FontWeight.w600,
                        color: AppColors.ink,
                      ),
                    ),
                    const SizedBox(height: 5),
                    Row(
                      children: [
                        SeverityChip(severity: incident.severity, compact: true),
                        const SizedBox(width: 7),
                        Flexible(
                          child: Text(
                            '${incident.district ?? 'Unknown'} · '
                            '${timeAgo(incident.reportedAt)}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(fontSize: 12),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right, color: AppColors.body),
            ],
          ),
        ),
      ),
    );
  }
}
