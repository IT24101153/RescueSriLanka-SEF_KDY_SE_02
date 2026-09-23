import 'dart:io';

import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';

import '../../../../shared/core/config.dart';
import '../../../../shared/core/theme.dart';
import '../../services/api_client.dart';
import '../../../../shared/services/auth_service.dart';
import '../../../../shared/screens/auth/login_screen.dart';
import '../../../../shared/widgets/app_ui.dart';

/// The disaster types the API accepts, in IncidentType order.
const List<String> _incidentTypes = [
  'Flood',
  'Landslide',
  'Fire',
  'Accident',
  'Storm',
  'Tsunami',
  'Other',
];

const Map<String, IconData> _typeIcons = {
  'Flood': Icons.water_drop_outlined,
  'Landslide': Icons.terrain_outlined,
  'Fire': Icons.local_fire_department_outlined,
  'Accident': Icons.car_crash_outlined,
  'Storm': Icons.thunderstorm_outlined,
  'Tsunami': Icons.tsunami_outlined,
  'Other': Icons.report_problem_outlined,
};

/// Report a disaster.
///
/// Deliberately does not ask how severe it is. Grading is the Incident
/// Analysis Agent's job and a coordinator's decision — someone standing in
/// flood water should describe what they see, not rank it.
class ReportScreen extends StatefulWidget {
  const ReportScreen({super.key, required this.auth});

  final AuthService auth;

  @override
  State<ReportScreen> createState() => _ReportScreenState();
}

class _ReportScreenState extends State<ReportScreen> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _description = TextEditingController();
  final _district = TextEditingController();
  final _address = TextEditingController();
  final _people = TextEditingController();

  String _type = 'Flood';
  Position? _position;
  File? _photo;

  bool _locating = false;
  bool _submitting = false;
  String? _locationError;
  String? _error;

  @override
  void initState() {
    super.initState();
    // Most reports are filed from the scene, so lead with the device's own
    // position rather than making someone type coordinates.
    if (widget.auth.isSignedIn) _locate();
  }

  @override
  void dispose() {
    for (final controller in [
      _title,
      _description,
      _district,
      _address,
      _people,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  // -------------------------------------------------------------- location

  Future<void> _locate() async {
    setState(() {
      _locating = true;
      _locationError = null;
    });

    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        throw 'Location services are switched off on this device.';
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }

      if (permission == LocationPermission.denied) {
        throw 'Location permission was declined.';
      }
      if (permission == LocationPermission.deniedForever) {
        throw 'Location permission is blocked. Enable it in system settings.';
      }

      final position = await Geolocator.getCurrentPosition(
        locationSettings: const LocationSettings(
          accuracy: LocationAccuracy.high,
          timeLimit: Duration(seconds: 20),
        ),
      );

      if (!mounted) return;
      setState(() {
        _position = position;
        _locating = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _locating = false;
        _locationError = error is String
            ? error
            : 'Could not read your location.';
      });
    }
  }

  bool get _inSriLanka {
    final position = _position;
    if (position == null) return false;
    return position.latitude >= AppConfig.minLatitude &&
        position.latitude <= AppConfig.maxLatitude &&
        position.longitude >= AppConfig.minLongitude &&
        position.longitude <= AppConfig.maxLongitude;
  }

  // ----------------------------------------------------------------- photo

  Future<void> _addPhoto(ImageSource source) async {
    try {
      final picked = await ImagePicker().pickImage(
        source: source,
        // The API caps uploads at 8 MB and the agent only needs enough detail
        // to judge the scene, so shrink before sending rather than after.
        maxWidth: 1600,
        imageQuality: 82,
      );

      if (picked == null || !mounted) return;
      setState(() => _photo = File(picked.path));
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not open the camera on this device.');
    }
  }

  // ---------------------------------------------------------------- submit

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;

    final position = _position;
    if (position == null) {
      setState(
        () => _error = 'A location is needed before a report can be filed.',
      );
      return;
    }

    setState(() {
      _submitting = true;
      _error = null;
    });

    final api = ApiClient(auth: widget.auth);

    try {
      // With a photo, report and photo go up together so the analysis agent
      // sees the picture. The report is filed even if only the photo fails.
      String? photoWarning;
      final photo = _photo;
      if (photo != null) {
        final result = await api.createIncidentWithPhoto(
          title: _title.text,
          description: _description.text,
          type: _type,
          latitude: position.latitude,
          longitude: position.longitude,
          district: _district.text,
          addressText: _address.text,
          estimatedAffectedPeople: int.tryParse(_people.text.trim()),
          photo: photo,
        );
        photoWarning = result.photoError;
      } else {
        await api.createIncident(
          title: _title.text,
          description: _description.text,
          type: _type,
          latitude: position.latitude,
          longitude: position.longitude,
          district: _district.text,
          addressText: _address.text,
          estimatedAffectedPeople: int.tryParse(_people.text.trim()),
        );
      }

      if (!mounted) return;
      _showSubmitted(photoWarning);
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _submitting = false;
        _error = error is ApiException
            ? error.message
            : 'Could not file the report.';
      });
    } finally {
      api.dispose();
    }
  }

  void _showSubmitted(String? photoWarning) {
    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) => AlertDialog(
        icon: const Icon(
          Icons.check_circle_outline,
          color: AppColors.low,
          size: 34,
        ),
        title: const Text('Report submitted'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Your report is on the map and an emergency coordinator has been '
              'notified. They will confirm it before it is acted on.',
              style: TextStyle(height: 1.45),
            ),
            if (photoWarning != null) ...[
              const SizedBox(height: 14),
              Text(
                'The photo did not upload: $photoWarning',
                style: const TextStyle(
                  fontSize: 12.5,
                  height: 1.4,
                  color: AppColors.high,
                ),
              ),
            ],
          ],
        ),
        actions: [
          TextButton(
            onPressed: () {
              Navigator.of(dialogContext).pop();
              _reset();
            },
            child: const Text('Done'),
          ),
        ],
      ),
    );
  }

  void _reset() {
    _formKey.currentState?.reset();
    for (final controller in [
      _title,
      _description,
      _district,
      _address,
      _people,
    ]) {
      controller.clear();
    }
    setState(() {
      _type = 'Flood';
      _photo = null;
      _submitting = false;
      _error = null;
    });
  }

  // ------------------------------------------------------------------ build

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.auth,
      builder: (context, _) {
        if (!widget.auth.isSignedIn) return _SignInPrompt(auth: widget.auth);
        return _buildForm(context);
      },
    );
  }

  Widget _buildForm(BuildContext context) {
    return Scaffold(
      appBar: AppHeader(
        title: 'Report a disaster',
        loading: _locating || _submitting,
      ),
      body: SafeArea(
        child: RefreshIndicator(
          // A form has nothing to reload but where you are, so a pull
          // re-reads the location. Anything already typed is kept.
          onRefresh: _locate,
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 32),
            children: [
              const Text(
                'Describe what you can see. A coordinator confirms every report '
                'before it is acted on — you do not need to judge how serious it is.',
                style: TextStyle(fontSize: 13.5, height: 1.45),
              ),
              const SizedBox(height: 20),

              if (_error != null) ...[
                _Banner(
                  message: _error!,
                  color: AppColors.critical,
                  icon: Icons.error_outline,
                ),
                const SizedBox(height: 16),
              ],

              _LocationCard(
                position: _position,
                busy: _locating,
                error: _locationError,
                outOfBounds: _position != null && !_inSriLanka,
                onRetry: _locate,
              ),
              const SizedBox(height: 20),

              Form(
                key: _formKey,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const _FieldLabel('What kind of disaster?'),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        for (final type in _incidentTypes)
                          ChoiceChip(
                            label: Text(type),
                            avatar: Icon(
                              _typeIcons[type] ?? Icons.report_problem_outlined,
                              size: 17,
                            ),
                            selected: _type == type,
                            onSelected: (_) => setState(() => _type = type),
                            selectedColor: AppColors.brand.withValues(
                              alpha: 0.22,
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 20),

                    TextFormField(
                      controller: _title,
                      maxLength: 200,
                      textCapitalization: TextCapitalization.sentences,
                      decoration: const InputDecoration(
                        labelText: 'Short summary',
                        hintText: 'Main road flooded near the bridge',
                        border: OutlineInputBorder(),
                      ),
                      validator: (value) {
                        final text = value?.trim() ?? '';
                        if (text.isEmpty) {
                          return 'Give the report a short summary.';
                        }
                        if (text.length < 8) return 'Add a little more detail.';
                        return null;
                      },
                    ),
                    const SizedBox(height: 6),

                    TextFormField(
                      controller: _description,
                      maxLines: 5,
                      maxLength: 4000,
                      textCapitalization: TextCapitalization.sentences,
                      decoration: const InputDecoration(
                        labelText: 'What is happening?',
                        alignLabelWithHint: true,
                        hintText: 'Water is waist deep and rising. Several houses cut off.',
                        border: OutlineInputBorder(),
                      ),
                      validator: (value) {
                        final text = value?.trim() ?? '';
                        if (text.isEmpty) return 'Describe what you can see.';
                        if (text.length < 15) {
                          return 'A fuller description helps responders decide.';
                        }
                        return null;
                      },
                    ),
                    const SizedBox(height: 6),

                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: TextFormField(
                            controller: _district,
                            textCapitalization: TextCapitalization.words,
                            decoration: const InputDecoration(
                              labelText: 'District',
                              border: OutlineInputBorder(),
                            ),
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: TextFormField(
                            controller: _people,
                            keyboardType: TextInputType.number,
                            decoration: const InputDecoration(
                              labelText: 'People affected',
                              hintText: 'Estimate',
                              border: OutlineInputBorder(),
                            ),
                            validator: (value) {
                              final text = value?.trim() ?? '';
                              if (text.isEmpty) return null;
                              final parsed = int.tryParse(text);
                              if (parsed == null || parsed < 0) {
                                return 'Enter a number.';
                              }
                              return null;
                            },
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 14),

                    TextFormField(
                      controller: _address,
                      maxLength: 300,
                      textCapitalization: TextCapitalization.sentences,
                      decoration: const InputDecoration(
                        labelText: 'Landmark or address (optional)',
                        hintText: 'Near the Kelani bridge, Peliyagoda side',
                        border: OutlineInputBorder(),
                      ),
                    ),
                  ],
                ),
              ),

              const SizedBox(height: 4),
              _PhotoField(
                photo: _photo,
                onCamera: () => _addPhoto(ImageSource.camera),
                onGallery: () => _addPhoto(ImageSource.gallery),
                onRemove: () => setState(() => _photo = null),
              ),

              const SizedBox(height: 24),
              FilledButton.icon(
                onPressed: _submitting || _position == null ? null : _submit,
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(50),
                  backgroundColor: AppColors.brand,
                  foregroundColor: AppColors.brandInk,
                ),
                icon: _submitting
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2.2),
                      )
                    : const Icon(Icons.send_outlined, size: 19),
                label: Text(_submitting ? 'Submitting…' : 'Submit report'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------- fragments

class _SignInPrompt extends StatelessWidget {
  const _SignInPrompt({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: const AppHeader(title: 'Report a disaster'),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(
                Icons.campaign_outlined,
                size: 54,
                color: AppColors.brand,
              ),
              const SizedBox(height: 18),
              Text(
                'Sign in to file a report',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w600,
                  color: AppColors.ink,
                ),
              ),
              const SizedBox(height: 10),
              const Text(
                'The map stays open to everyone. An account is needed only so a '
                'coordinator can follow up on what you report.',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 13.5, height: 1.5),
              ),
              const SizedBox(height: 24),
              FilledButton(
                onPressed: () => Navigator.of(context).push<bool>(
                  MaterialPageRoute(builder: (_) => LoginScreen(auth: auth)),
                ),
                style: FilledButton.styleFrom(
                  minimumSize: const Size(220, 46),
                  backgroundColor: AppColors.brand,
                  foregroundColor: AppColors.brandInk,
                ),
                child: const Text('Sign in'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _LocationCard extends StatelessWidget {
  const _LocationCard({
    required this.position,
    required this.busy,
    required this.error,
    required this.outOfBounds,
    required this.onRetry,
  });

  final Position? position;
  final bool busy;
  final String? error;
  final bool outOfBounds;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final resolved = position;

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.surfaceAlt,
        border: Border.all(color: AppColors.border),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            resolved != null ? Icons.my_location : Icons.location_searching,
            size: 20,
            color: resolved != null ? AppColors.low : AppColors.body,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Location',
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w600,
                    color: AppColors.ink,
                  ),
                ),
                const SizedBox(height: 4),
                if (busy)
                  const Text(
                    'Finding your location…',
                    style: TextStyle(fontSize: 13),
                  )
                else if (error != null)
                  Text(
                    error!,
                    style: const TextStyle(
                      fontSize: 13,
                      height: 1.4,
                      color: AppColors.high,
                    ),
                  )
                else if (resolved != null) ...[
                  Text(
                    '${resolved.latitude.toStringAsFixed(5)}, '
                    '${resolved.longitude.toStringAsFixed(5)}',
                    style: const TextStyle(fontSize: 13.5, height: 1.35),
                  ),
                  Text(
                    'Accurate to about ${resolved.accuracy.round()} m',
                    style: const TextStyle(fontSize: 12, color: AppColors.body),
                  ),
                  if (outOfBounds)
                    const Padding(
                      padding: EdgeInsets.only(top: 6),
                      child: Text(
                        'This position is outside Sri Lanka. It can still be '
                        'filed, but check it is right.',
                        style: TextStyle(
                          fontSize: 12,
                          height: 1.4,
                          color: AppColors.high,
                        ),
                      ),
                    ),
                ] else
                  const Text('Not set yet.', style: TextStyle(fontSize: 13)),
              ],
            ),
          ),
          TextButton(
            onPressed: busy ? null : onRetry,
            child: Text(resolved == null ? 'Locate' : 'Update'),
          ),
        ],
      ),
    );
  }
}

class _PhotoField extends StatelessWidget {
  const _PhotoField({
    required this.photo,
    required this.onCamera,
    required this.onGallery,
    required this.onRemove,
  });

  final File? photo;
  final VoidCallback onCamera;
  final VoidCallback onGallery;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const _FieldLabel('Photo (optional)'),
        const SizedBox(height: 4),
        const Text(
          'A photo is the strongest signal the analysis agent has — it reads '
          'the image along with your description.',
          style: TextStyle(fontSize: 12.5, height: 1.4, color: AppColors.body),
        ),
        const SizedBox(height: 12),
        if (photo != null)
          Stack(
            children: [
              ClipRRect(
                borderRadius: BorderRadius.circular(12),
                child: Image.file(
                  photo!,
                  height: 190,
                  width: double.infinity,
                  fit: BoxFit.cover,
                ),
              ),
              Positioned(
                top: 8,
                right: 8,
                child: Material(
                  color: Colors.black54,
                  shape: const CircleBorder(),
                  child: IconButton(
                    icon: const Icon(
                      Icons.close,
                      size: 18,
                      color: Colors.white,
                    ),
                    onPressed: onRemove,
                    tooltip: 'Remove photo',
                  ),
                ),
              ),
            ],
          )
        else
          Row(
            children: [
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: onCamera,
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(46),
                  ),
                  icon: const Icon(Icons.photo_camera_outlined, size: 19),
                  label: const Text('Camera'),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: onGallery,
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(46),
                  ),
                  icon: const Icon(Icons.photo_library_outlined, size: 19),
                  label: const Text('Gallery'),
                ),
              ),
            ],
          ),
      ],
    );
  }
}

class _FieldLabel extends StatelessWidget {
  const _FieldLabel(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Text(
    text,
    style: const TextStyle(
      fontSize: 13,
      fontWeight: FontWeight.w600,
      color: AppColors.ink,
    ),
  );
}

class _Banner extends StatelessWidget {
  const _Banner({
    required this.message,
    required this.color,
    required this.icon,
  });

  final String message;
  final Color color;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.08),
        border: Border.all(color: color.withValues(alpha: 0.3)),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 19, color: color),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              message,
              style: TextStyle(fontSize: 13, height: 1.4, color: color),
            ),
          ),
        ],
      ),
    );
  }
}
