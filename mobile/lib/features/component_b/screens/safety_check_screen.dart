import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
import '../services/help_request_api.dart';

class SafetyCheckScreen extends StatefulWidget {
  const SafetyCheckScreen({super.key});

  @override
  State<SafetyCheckScreen> createState() => _SafetyCheckScreenState();
}

class _SafetyCheckScreenState extends State<SafetyCheckScreen> {
  bool _loading = false;
  String? _error;
  Map<String, dynamic>? _result;

  Future<void> _checkSafety() async {
    setState(() {
      _loading = true;
      _error = null;
      _result = null;
    });

    try {
      final serviceEnabled = await Geolocator.isLocationServiceEnabled();
      if (!serviceEnabled) {
        setState(() => _error = 'Location services are turned off.');
        return;
      }
      LocationPermission permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
        if (permission == LocationPermission.denied) {
          setState(() => _error = 'Location permission denied.');
          return;
        }
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
        ),
      );

      final res = await HelpRequestApi.post(
        '/api/TravelAdvisories/check-safety',
        {
          'points': [
            {'latitude': position.latitude, 'longitude': position.longitude},
          ],
        },
      );

      if (res.statusCode == 200) {
        setState(() => _result = jsonDecode(res.body));
      } else {
        setState(() => _error = 'Could not check safety right now.');
      }
    } catch (_) {
      setState(() => _error = 'Something went wrong. Try again.');
    } finally {
      setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final level = _result?['overallSafetyLevel'] as int?;

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Safety check',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (_error != null) AppErrorBanner(message: _error!),
            Expanded(
              child: ListView(
                padding: const EdgeInsets.fromLTRB(
                  AppSpacing.gutter,
                  18,
                  AppSpacing.gutter,
                  28,
                ),
                children: [
                  const Text(
                    'Check whether your current location sits in an active '
                    'danger zone before you travel.',
                    style: TextStyle(
                      fontSize: 13.5,
                      height: 1.45,
                      color: AppColors.body,
                    ),
                  ),
                  const SizedBox(height: 18),
                  if (level != null) ...[
                    AppCard(
                      accent: safetyLevelTone(level),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Icon(
                                safetyLevelIcon(level),
                                color: safetyLevelTone(level),
                                size: 22,
                              ),
                              const SizedBox(width: 10),
                              Text(
                                safetyLevelLabel(level),
                                style: TextStyle(
                                  color: safetyLevelTone(level),
                                  fontWeight: FontWeight.w700,
                                  fontSize: 18,
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 8),
                          Text(
                            (_result!['reason'] as String?) ?? '',
                            style: const TextStyle(fontSize: 14, height: 1.4),
                          ),
                          if (level == 0) ...[
                            const SizedBox(height: 10),
                            const Text(
                              'This means no active advisory in our system '
                              'covers this location. It is not a guarantee '
                              'that conditions are safe. Follow official '
                              'warnings and avoid travel if conditions look '
                              'unsafe.',
                              style: TextStyle(
                                fontSize: 12,
                                height: 1.4,
                                color: AppColors.body,
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                    const SizedBox(height: 20),
                  ],
                  AppPrimaryButton(
                    label: _loading ? 'Checking…' : 'Check my current location',
                    icon: Icons.my_location,
                    busy: _loading,
                    onPressed: _checkSafety,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
