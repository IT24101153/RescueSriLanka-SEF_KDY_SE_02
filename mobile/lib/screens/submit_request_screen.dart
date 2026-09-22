import 'dart:io';
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';
import '../services/help_request_service.dart';
import '../services/cloudinary_service.dart';

class SubmitRequestScreen extends StatefulWidget {
  const SubmitRequestScreen({super.key});

  @override
  State<SubmitRequestScreen> createState() => _SubmitRequestScreenState();
}

class _SubmitRequestScreenState extends State<SubmitRequestScreen> {
  final _descriptionController = TextEditingController();
  final _picker = ImagePicker();

  int _selectedType = 2; // default to Medical
  double? _latitude;
  double? _longitude;
  File? _selectedImage;
  String? _selectedImagePath; // works on both web (blob URL) and mobile

  bool _locating = false;
  bool _uploadingImage = false;
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

  Future<void> _pickImage(ImageSource source) async {
    final picked = await _picker.pickImage(source: source, imageQuality: 80, maxWidth: 1600);
    if (picked != null) {
      setState(() {
        _selectedImagePath = picked.path;
        _selectedImage = kIsWeb ? null : File(picked.path);
      });
    }
  }

  void _showImageSourceSheet() {
    showModalBottomSheet(
      context: context,
      backgroundColor: Colors.white,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(18)),
      ),
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 8),
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined, color: Color(0xFFE8960B)),
              title: const Text('Take a photo'),
              onTap: () {
                Navigator.of(context).pop();
                _pickImage(ImageSource.camera);
              },
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined, color: Color(0xFFE8960B)),
              title: const Text('Choose from gallery'),
              onTap: () {
                Navigator.of(context).pop();
                _pickImage(ImageSource.gallery);
              },
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
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

    // Upload the photo first (if one was picked), then submit the request with its URL.
    // NOTE: real upload only works on mobile — Flutter Web doesn't expose a real
    // File for picked images, so we skip upload there (photo capture is a
    // mobile-only feature by nature anyway — most citizens will use the phone app).
    String? imageUrl;
    if (_selectedImage != null && !kIsWeb) {
      setState(() => _uploadingImage = true);
      imageUrl = await CloudinaryService.uploadImage(_selectedImage!);
      setState(() => _uploadingImage = false);

      if (imageUrl == null) {
        setState(() {
          _submitting = false;
          _error = 'Could not upload the photo. You can submit without it, or try again.';
        });
        return;
      }
    }

    final result = await HelpRequestService.submit(
      type: _selectedType,
      description: _descriptionController.text.trim(),
      latitude: _latitude!,
      longitude: _longitude!,
      imageUrl: imageUrl,
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
              const Text('Photo (optional)', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
              const SizedBox(height: 10),
              if (_selectedImagePath != null)
                Stack(
                  children: [
                    ClipRRect(
                      borderRadius: BorderRadius.circular(12),
                      child: kIsWeb
                          ? Image.network(_selectedImagePath!, height: 180, width: double.infinity, fit: BoxFit.cover)
                          : Image.file(_selectedImage!, height: 180, width: double.infinity, fit: BoxFit.cover),
                    ),
                    Positioned(
                      top: 8,
                      right: 8,
                      child: GestureDetector(
                        onTap: () => setState(() {
                          _selectedImage = null;
                          _selectedImagePath = null;
                        }),
                        child: Container(
                          padding: const EdgeInsets.all(6),
                          decoration: const BoxDecoration(color: Colors.black54, shape: BoxShape.circle),
                          child: const Icon(Icons.close, color: Colors.white, size: 16),
                        ),
                      ),
                    ),
                  ],
                )
              else
                OutlinedButton.icon(
                  onPressed: _showImageSourceSheet,
                  icon: const Icon(Icons.add_a_photo_outlined),
                  label: const Text('Add a photo'),
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
                    ? Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          const SizedBox(
                            width: 18,
                            height: 18,
                            child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white),
                          ),
                          const SizedBox(width: 10),
                          Text(_uploadingImage ? 'Uploading photo…' : 'Submitting…'),
                        ],
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