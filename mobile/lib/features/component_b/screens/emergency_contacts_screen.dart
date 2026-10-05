import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import '../../../shared/widgets/app_ui.dart';
import '../services/help_request_service.dart';
import '../widgets/emergency_contacts_section.dart';

/// Emergency numbers on their own, reachable without a request. It uses the
/// device's last known position when location is already allowed — never
/// prompting — and otherwise lists the national numbers.
class EmergencyContactsScreen extends StatelessWidget {
  const EmergencyContactsScreen({super.key});

  Future<EmergencyContacts?> _load() async {
    double? latitude;
    double? longitude;
    try {
      final permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.always ||
          permission == LocationPermission.whileInUse) {
        final position = await Geolocator.getLastKnownPosition();
        latitude = position?.latitude;
        longitude = position?.longitude;
      }
    } catch (_) {
      // No location is fine: the national numbers still apply.
    }
    return HelpRequestService.getEmergencyContacts(
      latitude: latitude,
      longitude: longitude,
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Emergency contacts',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.gutter,
            18,
            AppSpacing.gutter,
            28,
          ),
          children: [EmergencyContactsSection(load: _load)],
        ),
      ),
    );
  }
}
