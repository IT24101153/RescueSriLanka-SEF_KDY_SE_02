import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
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
  XFile? _selectedImageFile;
  String? _selectedImagePath; // works on both web (blob URL) and mobile

  bool _locating = false;
  bool _uploadingImage = false;
  bool _submitting = false;
  bool _analyzingDraft = false;
  String? _error;
  AiRequestAnalysis? _draftAnalysis;

  @override
  void dispose() {
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _useMyLocation() async {
    setState(() {
      _locating = true;
      _error = null;
    });

    try {
      final serviceEnabled = await Geolocator.isLocationServiceEnabled();
      if (!serviceEnabled) {
        setState(
          () => _error =
              'Location services are turned off. Enable them to continue.',
        );
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
        setState(
          () => _error = 'Location permission permanently denied. Enable it in system settings.',
        );
        return;
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
        ),
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
    final picked = await _picker.pickImage(
      source: source,
      imageQuality: 80,
      maxWidth: 1600,
    );
    if (picked != null) {
      setState(() {
        _selectedImagePath = picked.path;
        _selectedImageFile = picked;
        _selectedImage = kIsWeb ? null : File(picked.path);
      });
    }
  }

  void _showImageSourceSheet() {
    showModalBottomSheet(
      context: context,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(18)),
      ),
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 8),
            ListTile(
              leading: const Icon(
                Icons.photo_camera_outlined,
                color: AppColors.brandInk,
              ),
              title: const Text('Take a photo'),
              onTap: () {
                Navigator.of(context).pop();
                _pickImage(ImageSource.camera);
              },
            ),
            ListTile(
              leading: const Icon(
                Icons.photo_library_outlined,
                color: AppColors.brandInk,
              ),
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
    // XFile supports byte uploads on both Flutter Web and mobile.
    String? imageUrl;
    if (_selectedImageFile != null) {
      setState(() => _uploadingImage = true);
      imageUrl = await CloudinaryService.uploadImage(_selectedImageFile!);
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

  Future<void> _analyzeDraft() async {
    final description = _descriptionController.text.trim();
    if (description.isEmpty) {
      setState(
        () => _error = 'Describe the situation before requesting AI guidance.',
      );
      return;
    }
    setState(() {
      _analyzingDraft = true;
      _error = null;
      _draftAnalysis = null;
    });
    final result = await HelpRequestService.analyzeDraft(
      type: _selectedType,
      description: description,
    );
    if (!mounted) return;
    setState(() {
      _analyzingDraft = false;
      _draftAnalysis = result;
      if (result == null) _error = 'AI guidance is unavailable right now. You can still submit your request.';
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        titleSpacing: 16,
        title: const Text(
          'Request help',
          style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
        ),
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (_error != null) AppErrorBanner(message: _error!),
            Expanded(
              child: SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(
                  AppSpacing.gutter,
                  18,
                  AppSpacing.gutter,
                  28,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const AppSectionTitle('What do you need?'),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: List.generate(helpRequestTypeLabels.length, (
                        i,
                      ) {
                        final selected = _selectedType == i;
                        return ChoiceChip(
                          avatar: Icon(
                            helpTypeIcon(i),
                            size: 16,
                            color: selected
                                ? AppColors.brandInk
                                : AppColors.body,
                          ),
                          label: Text(helpRequestTypeLabels[i]),
                          selected: selected,
                          showCheckmark: false,
                          onSelected: (_) => setState(() {
                            _selectedType = i;
                            _draftAnalysis = null;
                          }),
                          selectedColor: AppColors.brand,
                          backgroundColor: AppColors.surface,
                          side: const BorderSide(color: AppColors.border),
                          labelStyle: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w600,
                            color: selected
                                ? AppColors.brandInk
                                : AppColors.body,
                          ),
                        );
                      }),
                    ),

                    const SizedBox(height: 24),
                    const AppSectionTitle('Describe the situation'),
                    TextField(
                      controller: _descriptionController,
                      maxLines: 4,
                      decoration: const InputDecoration(
                        hintText: 'e.g. Family trapped on roof due to rising flood water',
                        border: OutlineInputBorder(),
                      ),
                    ),
                    const SizedBox(height: 10),
                    OutlinedButton.icon(
                      onPressed: _analyzingDraft ? null : _analyzeDraft,
                      icon: _analyzingDraft
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.auto_awesome_outlined, size: 18),
                      label: Text(
                        _analyzingDraft
                            ? 'Reviewing your report…'
                            : 'Improve report with AI',
                      ),
                    ),
                    if (_draftAnalysis != null) ...[
                      const SizedBox(height: AppSpacing.gap),
                      AppCard(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const Row(
                              children: [
                                Icon(
                                  Icons.auto_awesome_outlined,
                                  size: 17,
                                  color: AppColors.brandInk,
                                ),
                                SizedBox(width: 7),
                                Text(
                                  'AI report guidance',
                                  style: TextStyle(
                                    fontSize: 13.5,
                                    fontWeight: FontWeight.w700,
                                    color: AppColors.ink,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 8),
                            Text(
                              _draftAnalysis!.reasoning,
                              style: const TextStyle(fontSize: 13, height: 1.4),
                            ),
                            if (_draftAnalysis!.suggestedAction.isNotEmpty) ...[
                              const SizedBox(height: 8),
                              Text(
                                _draftAnalysis!.suggestedAction,
                                style: const TextStyle(
                                  fontSize: 13,
                                  fontWeight: FontWeight.w600,
                                  height: 1.35,
                                ),
                              ),
                            ],
                            const SizedBox(height: 10),
                            const Text(
                              'This is guidance only. Do not delay submitting an '
                              'emergency request.',
                              style: TextStyle(
                                fontSize: 11.5,
                                color: AppColors.body,
                                height: 1.35,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],

                    const SizedBox(height: 24),
                    const AppSectionTitle('Photo (optional)'),
                    if (_selectedImagePath != null)
                      Stack(
                        children: [
                          ClipRRect(
                            borderRadius: AppSpacing.radius,
                            child: kIsWeb
                                ? Image.network(
                                    _selectedImagePath!,
                                    height: 180,
                                    width: double.infinity,
                                    fit: BoxFit.cover,
                                  )
                                : Image.file(
                                    _selectedImage!,
                                    height: 180,
                                    width: double.infinity,
                                    fit: BoxFit.cover,
                                  ),
                          ),
                          Positioned(
                            top: 8,
                            right: 8,
                            child: GestureDetector(
                              onTap: () => setState(() {
                                _selectedImage = null;
                                _selectedImageFile = null;
                                _selectedImagePath = null;
                              }),
                              child: Container(
                                padding: const EdgeInsets.all(6),
                                decoration: const BoxDecoration(
                                  color: Colors.black54,
                                  shape: BoxShape.circle,
                                ),
                                child: const Icon(
                                  Icons.close,
                                  color: Colors.white,
                                  size: 16,
                                ),
                              ),
                            ),
                          ),
                        ],
                      )
                    else
                      OutlinedButton.icon(
                        onPressed: _showImageSourceSheet,
                        icon: const Icon(Icons.add_a_photo_outlined, size: 18),
                        label: const Text('Add a photo'),
                      ),

                    const SizedBox(height: 24),
                    const AppSectionTitle('Your location'),
                    if (_latitude != null && _longitude != null) ...[
                      AppCard(
                        accent: AppColors.safe,
                        child: Row(
                          children: [
                            const Icon(
                              Icons.check_circle_outline,
                              color: AppColors.safe,
                              size: 18,
                            ),
                            const SizedBox(width: 10),
                            Expanded(
                              child: Text(
                                'Location captured: '
                                '${_latitude!.toStringAsFixed(4)}, '
                                '${_longitude!.toStringAsFixed(4)}',
                                style: const TextStyle(fontSize: 13),
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 10),
                    ],
                    OutlinedButton.icon(
                      onPressed: _locating ? null : _useMyLocation,
                      icon: _locating
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.my_location, size: 18),
                      label: Text(
                        _latitude == null
                            ? 'Use my current location'
                            : 'Update location',
                      ),
                    ),

                    const SizedBox(height: 26),
                    AppPrimaryButton(
                      label: _submitting
                          ? (_uploadingImage
                                ? 'Uploading photo…'
                                : 'Submitting…')
                          : 'Submit request',
                      icon: Icons.send,
                      busy: _submitting,
                      onPressed: _submit,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
