import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
import '../services/help_request_service.dart';

/// Where your own requests were submitted, on the same OpenStreetMap tiles
/// the disaster map uses.
///
/// A pin's colour is its status and its icon is what was asked for — the same
/// two cues the request list shows.
class MyRequestsMapScreen extends StatefulWidget {
  const MyRequestsMapScreen({super.key});

  @override
  State<MyRequestsMapScreen> createState() => _MyRequestsMapScreenState();
}

class _MyRequestsMapScreenState extends State<MyRequestsMapScreen> {
  final MapController _mapController = MapController();
  List<HelpRequest> _requests = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final result = await HelpRequestService.getMineWithStatus();
    if (!mounted) return;
    setState(() {
      _requests = result.requests;
      _error = result.error;
      _loading = false;
    });
    if (result.requests.isNotEmpty) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _fitToRequests());
    }
  }

  void _fitToRequests() {
    if (_requests.isEmpty) return;
    final points = _requests
        .map((request) => LatLng(request.latitude, request.longitude))
        .toList();
    if (points.length == 1) {
      _mapController.move(points.first, 14);
      return;
    }
    _mapController.fitCamera(
      CameraFit.bounds(
        bounds: LatLngBounds.fromPoints(points),
        padding: const EdgeInsets.all(48),
      ),
    );
  }

  void _showRequestDetails(HelpRequest request) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => _RequestInfoSheet(request: request),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'My request map',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: _loading ? null : _load,
          ),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : Stack(
              children: [
                FlutterMap(
                  mapController: _mapController,
                  options: const MapOptions(
                    initialCenter: LatLng(7.8731, 80.7718),
                    initialZoom: 7,
                  ),
                  children: [
                    TileLayer(
                      urlTemplate:
                          'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                      userAgentPackageName: 'lk.rescuesrilanka.mobile',
                    ),
                    MarkerLayer(
                      markers: _requests
                          .map(
                            (request) => Marker(
                              point: LatLng(
                                request.latitude,
                                request.longitude,
                              ),
                              width: 46,
                              height: 46,
                              child: GestureDetector(
                                onTap: () => _showRequestDetails(request),
                                child: _RequestPin(request: request),
                              ),
                            ),
                          )
                          .toList(),
                    ),
                    RichAttributionWidget(
                      attributions: [
                        TextSourceAttribution(
                          'OpenStreetMap contributors',
                          onTap: () {},
                        ),
                      ],
                    ),
                  ],
                ),
                if (_error != null)
                  Center(
                    child: _MapMessage(
                      icon: Icons.cloud_off_outlined,
                      tone: AppColors.critical,
                      title: 'Unable to load request locations',
                      message: _error!,
                    ),
                  )
                else if (_requests.isEmpty)
                  const Center(
                    child: _MapMessage(
                      icon: Icons.location_off_outlined,
                      tone: AppColors.body,
                      title: 'No submitted requests yet',
                      message: 'Your request locations will appear here.',
                    ),
                  ),
                Positioned(
                  left: 14,
                  right: 14,
                  bottom: 20,
                  child: _MapLegend(requestCount: _requests.length),
                ),
                if (_requests.length > 1)
                  Positioned(
                    right: 16,
                    top: 16,
                    child: FloatingActionButton.small(
                      heroTag: 'fit-map',
                      onPressed: _fitToRequests,
                      backgroundColor: AppColors.surface,
                      foregroundColor: AppColors.ink,
                      child: const Icon(Icons.center_focus_strong),
                    ),
                  ),
              ],
            ),
    );
  }
}

/// Colour is the status, the icon is the kind, and an urgent request gets a
/// halo — three cues, like the disaster map's markers.
class _RequestPin extends StatelessWidget {
  const _RequestPin({required this.request});

  final HelpRequest request;

  @override
  Widget build(BuildContext context) {
    final tone = helpStatusTone(request.status);
    final urgent = request.urgencyScore >= 70;

    return Center(
      child: Stack(
        alignment: Alignment.center,
        children: [
          if (urgent)
            Container(
              width: 44,
              height: 44,
              decoration: BoxDecoration(
                color: tone.withValues(alpha: 0.22),
                shape: BoxShape.circle,
              ),
            ),
          Container(
            width: 32,
            height: 32,
            decoration: BoxDecoration(
              color: tone,
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
              helpTypeIcon(request.type),
              size: 17,
              color: Colors.white,
            ),
          ),
        ],
      ),
    );
  }
}

class _MapLegend extends StatelessWidget {
  const _MapLegend({required this.requestCount});

  final int requestCount;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
      child: Row(
        children: [
          const Icon(Icons.location_on, color: AppColors.brandInk, size: 19),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              '$requestCount submitted request${requestCount == 1 ? '' : 's'}',
              style: const TextStyle(
                fontWeight: FontWeight.w600,
                fontSize: 13,
                color: AppColors.ink,
              ),
            ),
          ),
          const Text(
            'Tap a pin for details',
            style: TextStyle(color: AppColors.body, fontSize: 11),
          ),
        ],
      ),
    );
  }
}

/// The card shown over the map when there is nothing to plot, or the fetch
/// failed.
class _MapMessage extends StatelessWidget {
  const _MapMessage({
    required this.icon,
    required this.tone,
    required this.title,
    required this.message,
  });

  final IconData icon;
  final Color tone;
  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: AppCard(
        padding: const EdgeInsets.all(20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 32, color: tone),
            const SizedBox(height: 10),
            Text(
              title,
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontWeight: FontWeight.w700,
                fontSize: 14.5,
                color: AppColors.ink,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              message,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: AppColors.body,
                fontSize: 12.5,
                height: 1.4,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _RequestInfoSheet extends StatelessWidget {
  const _RequestInfoSheet({required this.request});

  final HelpRequest request;

  @override
  Widget build(BuildContext context) {
    final submittedAt =
        '${request.createdAt.day.toString().padLeft(2, '0')}/'
        '${request.createdAt.month.toString().padLeft(2, '0')}/'
        '${request.createdAt.year} · '
        '${TimeOfDay.fromDateTime(request.createdAt).format(context)}';

    return DraggableScrollableSheet(
      initialChildSize: .62,
      minChildSize: .35,
      maxChildSize: .92,
      builder: (context, controller) => Container(
        decoration: const BoxDecoration(
          color: AppColors.surface,
          borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
        ),
        child: ListView(
          controller: controller,
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.gutter,
            10,
            AppSpacing.gutter,
            32,
          ),
          children: [
            Center(
              child: Container(
                width: 42,
                height: 4,
                decoration: BoxDecoration(
                  color: AppColors.border,
                  borderRadius: BorderRadius.circular(3),
                ),
              ),
            ),
            const SizedBox(height: 18),
            Row(
              children: [
                Container(
                  padding: const EdgeInsets.all(10),
                  decoration: BoxDecoration(
                    color: AppColors.surfaceAlt,
                    borderRadius: BorderRadius.circular(11),
                  ),
                  child: Icon(helpTypeIcon(request.type), color: AppColors.ink),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        helpRequestTypeLabels[request.type],
                        style: const TextStyle(
                          fontWeight: FontWeight.w700,
                          fontSize: 17,
                          color: AppColors.ink,
                        ),
                      ),
                      Text(
                        'Submitted $submittedAt',
                        style: const TextStyle(
                          color: AppColors.body,
                          fontSize: 12,
                        ),
                      ),
                    ],
                  ),
                ),
                AppPill(
                  helpRequestStatusLabels[request.status],
                  tone: helpStatusTone(request.status),
                ),
              ],
            ),
            const SizedBox(height: 18),
            if (request.imageUrl != null) ...[
              ClipRRect(
                borderRadius: AppSpacing.radius,
                child: Image.network(
                  request.imageUrl!,
                  height: 190,
                  width: double.infinity,
                  fit: BoxFit.cover,
                  errorBuilder: (_, _, _) => const SizedBox(
                    height: 100,
                    child: Center(
                      child: Text('Unable to load submitted photo'),
                    ),
                  ),
                ),
              ),
              const SizedBox(height: 16),
            ],
            const AppSectionTitle('Request details'),
            Text(
              request.description,
              style: const TextStyle(fontSize: 14.5, height: 1.45),
            ),
            const SizedBox(height: 18),
            _InfoRow(
              icon: Icons.priority_high,
              label: 'Urgency score',
              value: '${request.urgencyScore}/100',
            ),
            _InfoRow(
              icon: Icons.verified_user_outlined,
              label: 'Verification',
              value: verificationStatusLabels[request.verificationStatus],
            ),
            _InfoRow(
              icon: Icons.location_on_outlined,
              label: 'Submitted location',
              value:
                  '${request.latitude.toStringAsFixed(5)}, '
                  '${request.longitude.toStringAsFixed(5)}',
            ),
            if (request.verificationNotes != null &&
                request.verificationNotes!.isNotEmpty)
              _InfoRow(
                icon: Icons.notes_outlined,
                label: 'Coordinator note',
                value: request.verificationNotes!,
              ),
          ],
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: AppColors.body),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label,
                  style: const TextStyle(color: AppColors.body, fontSize: 11),
                ),
                const SizedBox(height: 2),
                Text(
                  value,
                  style: const TextStyle(
                    fontWeight: FontWeight.w600,
                    fontSize: 13,
                    color: AppColors.ink,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
