import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../../shared/core/config.dart';
import '../../../../shared/core/theme.dart';
import '../../../../shared/services/place_search_service.dart';
import '../../../../shared/widgets/app_ui.dart';

/// Full-screen map for marking where a report is. Pops the chosen point.
///
/// The pin stays fixed in the middle and the map moves under it — easier to
/// place precisely with a thumb than dragging a small marker.
class LocationPickerScreen extends StatefulWidget {
  const LocationPickerScreen({
    super.key,
    required this.initial,
    this.initialZoom = 13,
  });

  final LatLng initial;
  final double initialZoom;

  @override
  State<LocationPickerScreen> createState() => _LocationPickerScreenState();
}

class _LocationPickerScreenState extends State<LocationPickerScreen> {
  static final CameraConstraint _constraint = CameraConstraint.containCenter(
    bounds: LatLngBounds(
      const LatLng(AppConfig.minLatitude, AppConfig.minLongitude),
      const LatLng(AppConfig.maxLatitude, AppConfig.maxLongitude),
    ),
  );

  static const double _minZoom = 6.5;
  static const double _maxZoom = 18;
  static const Duration _searchPause = Duration(milliseconds: 350);

  final MapController _map = MapController();
  final PlaceSearchService _places = PlaceSearchService();
  final TextEditingController _search = TextEditingController();

  late LatLng _centre = widget.initial;
  List<Place> _results = const [];
  bool _searching = false;
  String? _searchError;
  Timer? _debounce;

  @override
  void dispose() {
    _debounce?.cancel();
    _places.dispose();
    _search.dispose();
    super.dispose();
  }

  void _onSearchChanged(String text) {
    _debounce?.cancel();
    if (text.trim().length < 2) {
      setState(() {
        _results = const [];
        _searchError = null;
        _searching = false;
      });
      return;
    }
    _debounce = Timer(_searchPause, () => _runSearch(text));
  }

  Future<void> _runSearch(String text) async {
    setState(() {
      _searching = true;
      _searchError = null;
    });
    try {
      final results = await _places.search(text);
      if (!mounted || results == null) return;
      setState(() {
        _results = results;
        _searchError = results.isEmpty ? 'No places found.' : null;
      });
    } on PlaceSearchException catch (error) {
      if (mounted) setState(() => _searchError = error.message);
    } finally {
      if (mounted) setState(() => _searching = false);
    }
  }

  void _goTo(Place place) {
    FocusScope.of(context).unfocus();
    _search.text = place.shortName;
    setState(() {
      _results = const [];
      _searchError = null;
    });
    _map.move(LatLng(place.latitude, place.longitude), 15);
  }

  void _zoomBy(double delta) {
    final camera = _map.camera;
    _map.move(
      camera.center,
      (camera.zoom + delta).clamp(_minZoom, _maxZoom).toDouble(),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: const AppHeader(title: 'Mark the spot'),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
            child: TextField(
              controller: _search,
              textInputAction: TextInputAction.search,
              onChanged: _onSearchChanged,
              onSubmitted: _runSearch,
              decoration: InputDecoration(
                isDense: true,
                hintText: 'Search a town, road or landmark',
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
                    : null,
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
          ),
          if (_searchError != null)
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              child: Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  _searchError!,
                  style: const TextStyle(fontSize: 12.5),
                ),
              ),
            ),
          if (_results.isNotEmpty)
            ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 240),
              child: ListView(
                shrinkWrap: true,
                padding: EdgeInsets.zero,
                children: [
                  for (final place in _results)
                    ListTile(
                      dense: true,
                      leading: const Icon(Icons.place_outlined, size: 20),
                      title: Text(
                        place.shortName,
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                      subtitle: Text(
                        place.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                      onTap: () => _goTo(place),
                    ),
                ],
              ),
            ),
          Expanded(
            child: Stack(
              children: [
                FlutterMap(
                  mapController: _map,
                  options: MapOptions(
                    initialCenter: widget.initial,
                    initialZoom: widget.initialZoom,
                    minZoom: _minZoom,
                    maxZoom: _maxZoom,
                    cameraConstraint: _constraint,
                    onPositionChanged: (camera, _) =>
                        setState(() => _centre = camera.center),
                    // A tap is a quicker way to say "here" than a drag.
                    onTap: (_, point) => _map.move(point, _map.camera.zoom),
                  ),
                  children: [
                    TileLayer(
                      urlTemplate:
                          'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                      userAgentPackageName: 'lk.rescuesrilanka.mobile',
                    ),
                    const SimpleAttributionWidget(
                      source: Text('OpenStreetMap contributors'),
                    ),
                  ],
                ),
                // The pin's tip marks the map centre: lift it by half its
                // height so the point, not the middle of the icon, is there.
                const IgnorePointer(
                  child: Center(
                    child: Padding(
                      padding: EdgeInsets.only(bottom: 46),
                      child: Icon(
                        Icons.location_on,
                        size: 48,
                        color: AppColors.ink,
                        shadows: [
                          Shadow(color: Color(0x55000000), blurRadius: 6),
                        ],
                      ),
                    ),
                  ),
                ),
                Positioned(
                  top: 12,
                  left: 12,
                  right: 72,
                  child: IgnorePointer(
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 12,
                          vertical: 7,
                        ),
                        decoration: BoxDecoration(
                          color: AppColors.surface,
                          borderRadius: BorderRadius.circular(99),
                          border: Border.all(color: AppColors.border),
                        ),
                        child: const Text(
                          'Move the map so the pin sits on the spot',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                            color: AppColors.ink,
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                Positioned(
                  right: 12,
                  top: 12,
                  child: Column(
                    children: [
                      FloatingActionButton.small(
                        heroTag: 'picker-zoom-in',
                        onPressed: () => _zoomBy(1),
                        backgroundColor: AppColors.surface,
                        foregroundColor: AppColors.ink,
                        tooltip: 'Zoom in',
                        child: const Icon(Icons.add, size: 20),
                      ),
                      const SizedBox(height: 8),
                      FloatingActionButton.small(
                        heroTag: 'picker-zoom-out',
                        onPressed: () => _zoomBy(-1),
                        backgroundColor: AppColors.surface,
                        foregroundColor: AppColors.ink,
                        tooltip: 'Zoom out',
                        child: const Icon(Icons.remove, size: 20),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          Container(
            decoration: const BoxDecoration(
              color: AppColors.surface,
              border: Border(top: BorderSide(color: AppColors.border)),
            ),
            child: SafeArea(
              top: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      '${_centre.latitude.toStringAsFixed(5)}, '
                      '${_centre.longitude.toStringAsFixed(5)}',
                      textAlign: TextAlign.center,
                      style: const TextStyle(fontSize: 12.5),
                    ),
                    const SizedBox(height: 10),
                    AppPrimaryButton(
                      label: 'Use this spot',
                      icon: Icons.check,
                      onPressed: () => Navigator.of(context).pop(_centre),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
