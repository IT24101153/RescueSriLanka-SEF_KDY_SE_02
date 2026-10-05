import 'dart:io';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';

import '../../../shared/core/theme.dart';
import '../../../shared/widgets/app_ui.dart';
import '../help_style.dart';
import '../services/help_request_service.dart';
import '../widgets/estimated_people_field.dart';
import '../../../shared/services/auth_service.dart';

class SubmitRequestScreen extends StatefulWidget {
  const SubmitRequestScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  State<SubmitRequestScreen> createState() => _SubmitRequestScreenState();
}

class _SubmitRequestScreenState extends State<SubmitRequestScreen> {
  final _descriptionController = TextEditingController();
  final _peopleController = TextEditingController();
  final _landmarkController = TextEditingController();
  final _peopleFormKey = GlobalKey<FormState>();
  final _picker = ImagePicker();

  int _selectedType = 2; // default to Medical
  double? _latitude;
  double? _longitude;
  String _locationChoice = 'current';
  String? _selectedDistrict;
  bool _locationUnavailable = false;
  String? _locationError;
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
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _useMyLocation());
  }

  @override
  void dispose() {
    _descriptionController.dispose();
    _peopleController.dispose();
    _landmarkController.dispose();
    super.dispose();
  }

  Future<void> _useMyLocation() async {
    setState(() {
      _locating = true;
      _locationError = null;
      _locationUnavailable = false;
    });

    try {
      final serviceEnabled = await Geolocator.isLocationServiceEnabled();
      if (!serviceEnabled) {
        setState(() {
          _locationUnavailable = true;
          _locationError = 'Location services are turned off.';
        });
        return;
      }

      LocationPermission permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
        if (permission == LocationPermission.denied) {
          setState(() {
            _locationUnavailable = true;
            _locationError = 'Could not read your location.';
          });
          return;
        }
      }
      if (permission == LocationPermission.deniedForever) {
        setState(() {
          _locationUnavailable = true;
          _locationError = 'Location permission is disabled. Enable it in settings.';
        });
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
        _locationUnavailable = false;
        _locationError = null;
      });
    } catch (_) {
      setState(() {
        _locationUnavailable = true;
        _locationError = 'Could not read your location.';
      });
    } finally {
      setState(() => _locating = false);
    }
  }

  static const _districts = <String>[
    'Ampara', 'Anuradhapura', 'Badulla', 'Batticaloa', 'Colombo', 'Galle',
    'Gampaha', 'Hambantota', 'Jaffna', 'Kalutara', 'Kandy', 'Kegalle',
    'Kilinochchi', 'Kurunegala', 'Mannar', 'Matale', 'Matara', 'Monaragala',
    'Mullaitivu', 'Nuwara Eliya', 'Polonnaruwa', 'Puttalam', 'Ratnapura',
    'Trincomalee', 'Vavuniya',
  ];

  // Approximate district centres for reports made about an area rather than
  // the sender's precise GPS position.
  static const _districtCentres = <String, List<double>>{
    'Ampara': [7.30, 81.67], 'Anuradhapura': [8.31, 80.40], 'Badulla': [6.99, 81.06],
    'Batticaloa': [7.71, 81.69], 'Colombo': [6.93, 79.86], 'Galle': [6.05, 80.22],
    'Gampaha': [7.09, 79.99], 'Hambantota': [6.12, 81.12], 'Jaffna': [9.66, 80.01],
    'Kalutara': [6.58, 79.96], 'Kandy': [7.29, 80.64], 'Kegalle': [7.25, 80.35],
    'Kilinochchi': [9.38, 80.40], 'Kurunegala': [7.48, 80.36], 'Mannar': [8.98, 79.90],
    'Matale': [7.47, 80.62], 'Matara': [5.95, 80.55], 'Monaragala': [6.87, 81.35],
    'Mullaitivu': [9.27, 80.81], 'Nuwara Eliya': [6.95, 80.78], 'Polonnaruwa': [7.94, 81.00],
    'Puttalam': [8.04, 79.84], 'Ratnapura': [6.68, 80.40], 'Trincomalee': [8.57, 81.23],
    'Vavuniya': [8.75, 80.50],
  };

  void _selectDistrict(String? district) {
    if (district == null) return;
    final centre = _districtCentres[district]!;
    setState(() {
      _selectedDistrict = district;
      _latitude = centre[0];
      _longitude = centre[1];
      _locationUnavailable = false;
      _locationError = null;
    });
  }

  void _chooseLocation(String choice) {
    final profileDistrict = widget.auth.user?.district;
    setState(() {
      _locationChoice = choice;
      _locationError = null;
      if (choice == 'current') {
        _latitude = null;
        _longitude = null;
      } else if (choice == 'profile' && profileDistrict != null &&
          _districtCentres.containsKey(profileDistrict)) {
        final centre = _districtCentres[profileDistrict]!;
        _selectedDistrict = profileDistrict;
        _latitude = centre[0];
        _longitude = centre[1];
      } else {
        _selectedDistrict = null;
        _latitude = null;
        _longitude = null;
      }
    });
    if (choice == 'current') _useMyLocation();
  }

  Widget _locationOption({required String value, required IconData icon,
    required String title, required String subtitle}) {
    final selected = _locationChoice == value;
    return InkWell(
      onTap: () => _chooseLocation(value),
      borderRadius: BorderRadius.circular(16),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 160),
        margin: const EdgeInsets.only(bottom: 9),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 13),
        decoration: BoxDecoration(
          color: selected ? AppColors.brand.withValues(alpha: .12) : AppColors.surfaceAlt,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: selected ? AppColors.brandInk : AppColors.border,
            width: selected ? 1.5 : 1),
        ),
        child: Row(children: [
          CircleAvatar(backgroundColor: selected ? AppColors.brand : AppColors.surface,
            foregroundColor: AppColors.ink, child: Icon(icon, size: 20)),
          const SizedBox(width: 12),
          Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start,
            children: [Text(title, style: const TextStyle(fontWeight: FontWeight.w700,
              color: AppColors.ink)), const SizedBox(height: 3), Text(subtitle,
              style: const TextStyle(color: AppColors.body, fontSize: 12.5))])),
          Icon(selected ? Icons.radio_button_checked : Icons.radio_button_off,
            color: selected ? AppColors.brandInk : AppColors.border),
        ]),
      ),
    );
  }

  Widget _buildLocationSection() {
    final profileDistrict = widget.auth.user?.district;
    final needsDistrict = _locationChoice == 'profile' || _locationChoice == 'other';
    return AppCard(child: Column(crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Row(children: [
          CircleAvatar(radius: 18, backgroundColor: AppColors.surfaceAlt,
            child: Text('2', style: TextStyle(fontWeight: FontWeight.w700))),
          SizedBox(width: 11), Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start,
            children: [Text('Where is it?', style: TextStyle(fontWeight: FontWeight.w700,
              fontSize: 16, color: AppColors.ink)), SizedBox(height: 3), Text(
              'Choose what fits where you are right now.',
              style: TextStyle(color: AppColors.body, fontSize: 13))])),
        ]),
        const SizedBox(height: 18),
        _locationOption(value: 'current', icon: Icons.my_location,
          title: 'My current location', subtitle: 'GPS — best when you are at the scene.'),
        _locationOption(value: 'profile', icon: Icons.home_outlined,
          title: profileDistrict == null ? 'My area' : 'My area · $profileDistrict',
          subtitle: 'The district on your profile.'),
        _locationOption(value: 'other', icon: Icons.travel_explore_outlined,
          title: 'Another area', subtitle: 'Somewhere else — choose a district.'),
        if (_locationChoice == 'current' && (_locationUnavailable || _locating))
          Container(margin: const EdgeInsets.only(bottom: 10), padding: const EdgeInsets.all(13),
            decoration: BoxDecoration(color: AppColors.surfaceAlt,
              borderRadius: BorderRadius.circular(15), border: Border.all(color: AppColors.border)),
            child: Row(children: [
              const Icon(Icons.my_location_outlined, color: AppColors.body),
              const SizedBox(width: 10), Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start,
                children: [const Text('Location unavailable', style: TextStyle(fontWeight: FontWeight.w600)),
                  Text(_locating ? 'Reading your location…' : (_locationError ?? 'Could not read your location.'),
                    style: const TextStyle(color: AppColors.body, fontSize: 12))])),
              TextButton(onPressed: _locating ? null : _useMyLocation,
                child: Text(_locating ? 'Locating…' : 'Locate')),
            ])),
        if (needsDistrict) ...[
          const SizedBox(height: 3),
          DropdownButtonFormField<String>(
            value: _locationChoice == 'profile' && profileDistrict != null && _districts.contains(profileDistrict)
                ? profileDistrict : _selectedDistrict,
            decoration: const InputDecoration(labelText: 'District',
              prefixIcon: Icon(Icons.location_city_outlined), border: OutlineInputBorder()),
            hint: const Text('Select district'),
            items: _districts.map((d) => DropdownMenuItem(value: d, child: Text(d))).toList(),
            onChanged: _locationChoice == 'profile' && profileDistrict != null
                ? null : _selectDistrict,
          ),
          const SizedBox(height: 10),
          TextField(controller: _landmarkController,
            decoration: const InputDecoration(labelText: 'Landmark or address (optional)',
              prefixIcon: Icon(Icons.place_outlined), border: OutlineInputBorder())),
          if (_selectedDistrict != null)
            const Padding(padding: EdgeInsets.only(top: 7), child: Text(
              'Using an approximate point near the district centre. Add a landmark for responders.',
              style: TextStyle(color: AppColors.body, fontSize: 11.5))),
        ],
        if (_locationChoice == 'profile' && profileDistrict == null)
          const Padding(padding: EdgeInsets.only(top: 8), child: Text(
            'No district is saved on your profile. Select one below.',
            style: TextStyle(color: AppColors.body, fontSize: 12))),
      ]));
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
    if (!_peopleFormKey.currentState!.validate()) return;
    final estimatedPeopleCount = int.parse(_peopleController.text);
    if (_latitude == null || _longitude == null) {
      setState(() => _error = 'Please share your location first.');
      return;
    }

    setState(() {
      _submitting = true;
      _error = null;
    });

    // Upload the photo through the API first (if one was picked), then submit the request with its URL.
    // XFile supports byte uploads on both Flutter Web and mobile.
    String? imageUrl;
    if (_selectedImageFile != null) {
      setState(() => _uploadingImage = true);
      imageUrl = await HelpRequestService.uploadPhoto(_selectedImageFile!);
      setState(() => _uploadingImage = false);

      if (imageUrl == null) {
        setState(() {
          _submitting = false;
          _error = 'Could not upload the photo. You can submit without it, or try again.';
        });
        return;
      }
    }

    final landmark = _landmarkController.text.trim();
    final description = landmark.isEmpty
        ? _descriptionController.text.trim()
        : '${_descriptionController.text.trim()}\nLandmark: $landmark';
    final result = await HelpRequestService.submit(
      type: _selectedType,
      description: description,
      estimatedPeopleCount: estimatedPeopleCount,
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
                      children: [0, 1, 2, 3, 5].map((i) {
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
                      }).toList(),
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
                    const AppSectionTitle('Approximate number of people affected'),
                    Form(
                      key: _peopleFormKey,
                      child: EstimatedPeopleField(controller: _peopleController),
                    ),
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
                    _buildLocationSection(),
                    if (_latitude != null && _longitude != null &&
                        _locationChoice == 'current') ...[
                      const SizedBox(height: 8),
                      Text('GPS location: ${_latitude!.toStringAsFixed(4)}, '
                        '${_longitude!.toStringAsFixed(4)}',
                        style: const TextStyle(color: AppColors.body, fontSize: 12)),
                    ],

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
