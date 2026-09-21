import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import '../services/help_request_service.dart';

class SubmitRequestScreen extends StatefulWidget {
  const SubmitRequestScreen({super.key});

  @override
  State<SubmitRequestScreen> createState() => _SubmitRequestScreenState();
}

class _SubmitRequestScreenState extends State<SubmitRequestScreen> {
  final _descriptionController = TextEditingController();
  int _selectedType = 2; // default to Medical
  double? _latitude;
  double? _longitude;
  bool _locating = false;
  bool _submitting = false;
  String? _error;

  Future<void> _useMyLocation() async {
    setState(() {
      _locating = true;
      _error = null;
    });

    try {
      final serviceEnabled = await Geolocator.isLocationServiceEnabled();
      if (!serviceEnabled) {
        setState(() => _error = 'Location services are turned off. Enable them to continue.');
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
      if (permission == LocationPermission.deniedForever) {
        setState(() => _error = 'Location permission permanently denied. Enable it in system settings.');
        return;
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(accuracy: LocationAccuracy.high),
      );

      setState(() {
        _latitude = position.latitude;
        _longitude = position.longitude;
      });
    } catch (_) {
      setState(() => _error = 'Could not get your location. Try again.');
    } finally {
      setState(() => _locating = false);
    }
  }

  Future<void> _submit() async {
    if (_descriptionController.text.trim().isEmpty) {
      setState(() => _error = 'Please describe what help you need.');
      return;
    }
    if (_latitude == null || _longitude == null) {
      setState(() => _error = 'Please share your location first.');
      return;
    }

    setState(() {
      _submitting = true;
      _error = null;
    });

    final result = await HelpRequestService.submit(
      type: _selectedType,
      description: _descriptionController.text.trim(),
      latitude: _latitude!,
      longitude: _longitude!,
    );

    setState(() => _submitting = false);

    if (!mounted) return;

    if (result != null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Request submitted. Help is on the way.')),
      );
      Navigator.of(context).pop();
    } else {
      setState(() => _error = 'Could not submit your request. Try again.');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Request Help')),
      backgroundColor: const Color(0xFFFAFAFB),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (_error != null) ...[
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFFDF0EF),
                    borderRadius: BorderRadius.circular(10),
                    border: Border.all(color: const Color(0xFFF3C9C7)),
                  ),
                  child: Text(_error!, style: const TextStyle(color: Color(0xFFD9433F), fontSize: 13)),
                ),
                const SizedBox(height: 16),
              ],

              const Text('What do you need?', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: List.generate(helpRequestTypeLabels.length, (i) {
                  final selected = _selectedType == i;
                  return ChoiceChip(
                    label: Text(helpRequestTypeLabels[i]),
                    selected: selected,
                    onSelected: (_) => setState(() => _selectedType = i),
                    selectedColor: const Color(0xFFE8960B),
                    labelStyle: TextStyle(color: selected ? Colors.white : const Color(0xFF14161C)),
                    backgroundColor: const Color(0xFFF0F0F3),
                  );
                }),
              ),

              const SizedBox(height: 22),
              const Text('Describe the situation', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
              const SizedBox(height: 10),
              TextField(
                controller: _descriptionController,
                maxLines: 4,
                decoration: const InputDecoration(
                  hintText: 'e.g. Family trapped on roof due to rising flood water',
                  border: OutlineInputBorder(),
                ),
              ),

              const SizedBox(height: 22),
              const Text('Your location', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
              const SizedBox(height: 10),
              if (_latitude != null && _longitude != null)
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFE9F7EF),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Row(
                    children: [
                      const Icon(Icons.check_circle, color: Color(0xFF0E8F56), size: 18),
                      const SizedBox(width: 8),
                      Text(
                        'Location captured: ${_latitude!.toStringAsFixed(4)}, ${_longitude!.toStringAsFixed(4)}',
                        style: const TextStyle(color: Color(0xFF0E8F56), fontSize: 13),
                      ),
                    ],
                  ),
                ),
              const SizedBox(height: 10),
              OutlinedButton.icon(
                onPressed: _locating ? null : _useMyLocation,
                icon: _locating
                    ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                    : const Icon(Icons.my_location),
                label: Text(_latitude == null ? 'Use my current location' : 'Update location'),
              ),

              const SizedBox(height: 28),
              ElevatedButton(
                onPressed: _submitting ? null : _submit,
                child: _submitting
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                      )
                    : const Text('Submit request'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}