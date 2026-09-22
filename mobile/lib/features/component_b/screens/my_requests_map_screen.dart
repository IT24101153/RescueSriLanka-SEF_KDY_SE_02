import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';
import '../services/help_request_service.dart';

const _typeColors = <Color>[
  Color(0xFF1687D3), // Water
  Color(0xFFED8A22), // Food
  Color(0xFFDF4552), // Medical
  Color(0xFF8258D5), // Rescue
  Color(0xFF20A56A), // Shelter
  Color(0xFF7B8190), // Other
];

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
    final points = _requests.map((r) => LatLng(r.latitude, r.longitude)).toList();
    if (points.length == 1) {
      _mapController.move(points.first, 14);
      return;
    }
    _mapController.fitCamera(CameraFit.bounds(
      bounds: LatLngBounds.fromPoints(points),
      padding: const EdgeInsets.all(48),
    ));
  }

  void _showRequestDetails(HelpRequest request) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => _RequestInfoSheet(request: request),
    );
  }

  Color _colorForType(int type) => type >= 0 && type < _typeColors.length ? _typeColors[type] : _typeColors.last;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('My Request Map'),
        actions: [
          IconButton(icon: const Icon(Icons.refresh), tooltip: 'Refresh', onPressed: _loading ? null : _load),
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
                      urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                      userAgentPackageName: 'com.example.mobile',
                    ),
                    MarkerLayer(
                      markers: _requests.map((request) => Marker(
                        point: LatLng(request.latitude, request.longitude),
                        width: 52,
                        height: 62,
                        child: GestureDetector(
                          onTap: () => _showRequestDetails(request),
                          child: _RequestPin(
                            color: _colorForType(request.type),
                            isUrgent: request.urgencyScore >= 70,
                            icon: _iconForType(request.type),
                          ),
                        ),
                      )).toList(),
                    ),
                    RichAttributionWidget(
                      attributions: [
                        TextSourceAttribution('OpenStreetMap contributors', onTap: () {}),
                      ],
                    ),
                  ],
                ),
                if (_requests.isEmpty && _error == null)
                  const Center(child: _EmptyMapMessage()),
                if (_error != null)
                  Center(child: _MapErrorMessage(message: _error!)),
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
                      backgroundColor: Colors.white,
                      foregroundColor: const Color(0xFF14161C),
                      child: const Icon(Icons.center_focus_strong),
                    ),
                  ),
              ],
            ),
    );
  }
}

IconData _iconForType(int type) => switch (type) {
  0 => Icons.water_drop_outlined,
  1 => Icons.restaurant_outlined,
  2 => Icons.medical_services_outlined,
  3 => Icons.emergency_outlined,
  4 => Icons.home_outlined,
  _ => Icons.help_outline,
};

class _RequestPin extends StatelessWidget {
  final Color color;
  final bool isUrgent;
  final IconData icon;
  const _RequestPin({required this.color, required this.isUrgent, required this.icon});

  @override
  Widget build(BuildContext context) {
    return Stack(
      alignment: Alignment.topCenter,
      children: [
        if (isUrgent)
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(color: color.withValues(alpha: 0.22), shape: BoxShape.circle),
          ),
        Container(
          width: 38,
          height: 38,
          decoration: BoxDecoration(
            color: color,
            shape: BoxShape.circle,
            border: Border.all(color: Colors.white, width: 3),
            boxShadow: const [BoxShadow(color: Colors.black38, blurRadius: 7, offset: Offset(0, 3))],
          ),
          child: Icon(icon, size: 19, color: Colors.white),
        ),
        Positioned(top: 34, child: CustomPaint(size: const Size(16, 14), painter: _PinTailPainter(color))),
      ],
    );
  }
}

class _PinTailPainter extends CustomPainter {
  final Color color;
  _PinTailPainter(this.color);
  @override
  void paint(Canvas canvas, Size size) {
    final path = ui.Path()..moveTo(0, 0)..lineTo(size.width, 0)..lineTo(size.width / 2, size.height)..close();
    canvas.drawPath(path, Paint()..color = color);
  }
  @override
  bool shouldRepaint(covariant _PinTailPainter oldDelegate) => oldDelegate.color != color;
}

class _MapLegend extends StatelessWidget {
  final int requestCount;
  const _MapLegend({required this.requestCount});
  @override
  Widget build(BuildContext context) {
    return Card(
      elevation: 5,
      shadowColor: Colors.black26,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
        child: Row(
          children: [
            const Icon(Icons.location_on, color: Color(0xFFE8960B), size: 20),
            const SizedBox(width: 7),
            Expanded(child: Text('$requestCount submitted request${requestCount == 1 ? '' : 's'}', style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13))),
            const Text('Tap a pin for details', style: TextStyle(color: Color(0xFF747783), fontSize: 11)),
          ],
        ),
      ),
    );
  }
}

class _EmptyMapMessage extends StatelessWidget {
  const _EmptyMapMessage();
  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(mainAxisSize: MainAxisSize.min, children: const [
        Icon(Icons.location_off_outlined, size: 34, color: Color(0xFF7A7D89)),
        SizedBox(height: 8),
        Text('No submitted requests yet', style: TextStyle(fontWeight: FontWeight.bold)),
        SizedBox(height: 4),
        Text('Your request locations will appear here.', style: TextStyle(color: Color(0xFF7A7D89), fontSize: 12)),
      ]),
    ),
  );
}

class _MapErrorMessage extends StatelessWidget {
  final String message;
  const _MapErrorMessage({required this.message});

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(mainAxisSize: MainAxisSize.min, children: [
        const Icon(Icons.cloud_off_outlined, size: 34, color: Color(0xFFC8453C)),
        const SizedBox(height: 8),
        const Text('Unable to load request locations', style: TextStyle(fontWeight: FontWeight.bold)),
        const SizedBox(height: 4),
        Text(message, textAlign: TextAlign.center, style: const TextStyle(color: Color(0xFF7A7D89), fontSize: 12)),
      ]),
    ),
  );
}

class _RequestInfoSheet extends StatelessWidget {
  final HelpRequest request;
  const _RequestInfoSheet({required this.request});
  Color get _typeColor => request.type >= 0 && request.type < _typeColors.length ? _typeColors[request.type] : _typeColors.last;
  Color get _statusColor => switch (request.status) { 3 => const Color(0xFF0E8F56), 4 => const Color(0xFF858894), 1 || 2 => const Color(0xFF2F6FB0), _ => const Color(0xFFB8720A) };

  @override
  Widget build(BuildContext context) {
    final submittedAt = '${request.createdAt.day.toString().padLeft(2, '0')}/${request.createdAt.month.toString().padLeft(2, '0')}/${request.createdAt.year} · ${TimeOfDay.fromDateTime(request.createdAt).format(context)}';
    return DraggableScrollableSheet(
      initialChildSize: .62,
      minChildSize: .35,
      maxChildSize: .92,
      builder: (context, controller) => Container(
        decoration: const BoxDecoration(color: Color(0xFFFAFAFB), borderRadius: BorderRadius.vertical(top: Radius.circular(22))),
        child: ListView(
          controller: controller,
          padding: const EdgeInsets.fromLTRB(20, 10, 20, 32),
          children: [
            Center(child: Container(width: 42, height: 4, decoration: BoxDecoration(color: const Color(0xFFD7D8DE), borderRadius: BorderRadius.circular(3)))),
            const SizedBox(height: 18),
            Row(children: [
              Container(padding: const EdgeInsets.all(10), decoration: BoxDecoration(color: _typeColor.withValues(alpha: .13), borderRadius: BorderRadius.circular(12)), child: Icon(_iconForType(request.type), color: _typeColor)),
              const SizedBox(width: 11),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(helpRequestTypeLabels[request.type], style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
                Text('Submitted $submittedAt', style: const TextStyle(color: Color(0xFF777A85), fontSize: 12)),
              ])),
              _StatusChip(label: helpRequestStatusLabels[request.status], color: _statusColor),
            ]),
            const SizedBox(height: 18),
            if (request.imageUrl != null) ...[
              ClipRRect(borderRadius: BorderRadius.circular(14), child: Image.network(request.imageUrl!, height: 190, width: double.infinity, fit: BoxFit.cover, errorBuilder: (_, _,_) => const SizedBox(height: 100, child: Center(child: Text('Unable to load submitted photo'))))),
              const SizedBox(height: 16),
            ],
            const Text('Request details', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14)),
            const SizedBox(height: 7),
            Text(request.description, style: const TextStyle(fontSize: 15, height: 1.45)),
            const SizedBox(height: 18),
            _InfoRow(icon: Icons.priority_high, label: 'Urgency score', value: '${request.urgencyScore}/100'),
            _InfoRow(icon: Icons.verified_user_outlined, label: 'Verification', value: verificationStatusLabels[request.verificationStatus]),
            _InfoRow(icon: Icons.location_on_outlined, label: 'Submitted location', value: '${request.latitude.toStringAsFixed(5)}, ${request.longitude.toStringAsFixed(5)}'),
            if (request.verificationNotes != null && request.verificationNotes!.isNotEmpty)
              _InfoRow(icon: Icons.notes_outlined, label: 'Coordinator note', value: request.verificationNotes!),
          ],
        ),
      ),
    );
  }
}

class _StatusChip extends StatelessWidget {
  final String label; final Color color;
  const _StatusChip({required this.label, required this.color});
  @override
  Widget build(BuildContext context) => Container(padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 5), decoration: BoxDecoration(color: color.withValues(alpha: .12), borderRadius: BorderRadius.circular(999)), child: Text(label, style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 11)));
}

class _InfoRow extends StatelessWidget {
  final IconData icon; final String label; final String value;
  const _InfoRow({required this.icon, required this.label, required this.value});
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Icon(icon, size: 18, color: const Color(0xFF777A85)), const SizedBox(width: 10),
      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Text(label, style: const TextStyle(color: Color(0xFF777A85), fontSize: 11)), const SizedBox(height: 2), Text(value, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13))])),
    ]),
  );
}
