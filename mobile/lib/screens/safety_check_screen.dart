import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
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
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
      );

      final res = await HelpRequestApi.post('/api/TravelAdvisories/check-safety', {
        'points': [
          {'latitude': position.latitude, 'longitude': position.longitude}
        ],
      });

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

  Color _levelColor(int level) {
    switch (level) {
      case 2:
        return const Color(0xFFD9433F); // Danger
      case 1:
        return const Color(0xFFB8720A); // Caution
      default:
        return const Color(0xFF0E8F56); // Safe
    }
  }

  String _levelLabel(int level) {
    switch (level) {
      case 2:
        return 'Danger';
      case 1:
        return 'Caution';
      default:
        return 'Safe';
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Safety Check')),
      backgroundColor: const Color(0xFFFAFAFB),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text(
                'Check if your current location is in an active danger zone before traveling.',
                style: TextStyle(color: Color(0xFF7A7D89), fontSize: 14),
              ),
              const SizedBox(height: 20),
              if (_error != null)
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFFDF0EF),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Text(_error!, style: const TextStyle(color: Color(0xFFD9433F))),
                ),
              if (_result != null) ...[
                Container(
                  padding: const EdgeInsets.all(18),
                  decoration: BoxDecoration(
                    color: _levelColor(_result!['overallSafetyLevel']).withValues(alpha: 0.1),
                    borderRadius: BorderRadius.circular(14),
                    border: Border.all(color: _levelColor(_result!['overallSafetyLevel'])),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        _levelLabel(_result!['overallSafetyLevel']),
                        style: TextStyle(
                          color: _levelColor(_result!['overallSafetyLevel']),
                          fontWeight: FontWeight.bold,
                          fontSize: 20,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(_result!['reason'] ?? '', style: const TextStyle(fontSize: 14)),
                      if (_result!['overallSafetyLevel'] == 0) ...[
                        const SizedBox(height: 10),
                        const Text(
                          'This means no active advisories in our system cover this location. It is not a guarantee that conditions are safe. Follow official warnings and avoid travel if conditions look unsafe.',
                          style: TextStyle(fontSize: 12, color: Color(0xFF4B6860), height: 1.35),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
              const SizedBox(height: 24),
              ElevatedButton(
                onPressed: _loading ? null : _checkSafety,
                child: _loading
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                      )
                    : const Text('Check my current location'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
