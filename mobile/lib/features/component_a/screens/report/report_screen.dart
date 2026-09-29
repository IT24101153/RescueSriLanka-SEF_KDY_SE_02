import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:geolocator/geolocator.dart';
import 'package:image_picker/image_picker.dart';
import 'package:latlong2/latlong.dart';

import '../../../../shared/core/config.dart';
import '../../../../shared/core/theme.dart';
import '../../models/district_towns.dart';
import '../../models/incident.dart';
import '../../services/api_client.dart';
import '../../services/my_reports_store.dart';
import '../../../../shared/services/auth_service.dart';
import '../../../../shared/screens/auth/login_screen.dart';
import '../../../../shared/widgets/app_ui.dart';
import 'location_picker_screen.dart';
import 'my_reports_view.dart';
import 'report_visuals.dart';

/// Whether the floating glass tab bar sits over the bottom of the screen.
bool get _isIOS => defaultTargetPlatform == TargetPlatform.iOS;

/// Where the reported place comes from — each suits a different situation.
enum _LocationSource {
  /// Standing at the scene: the phone's GPS.
  gps,

  /// Near home: starts from the district on the reporter's profile.
  home,

  /// Somewhere else: a district or a spot chosen on the map.
  other,
}

/// Whole-island view, for a map with nothing chosen yet.
const LatLng _islandCentre = LatLng(7.85, 80.75);

/// Report a disaster, and follow the reports already sent.
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
  final _formScroll = ScrollController();
  final _title = TextEditingController();
  final _description = TextEditingController();
  final _district = TextEditingController();
  final _address = TextEditingController();
  final _people = TextEditingController();

  /// 0 is the form, 1 is "My reports".
  int _view = 0;

  String _type = 'Flood';
  _LocationSource _source = _LocationSource.gps;
  Position? _position;

  /// The pin for "My area", and whether the reporter placed it themselves
  /// rather than it sitting on the district's main town.
  LatLng? _homePoint;
  bool _homeExact = false;

  /// The profile district [_homePoint] was started from, so a changed
  /// profile starts the pin afresh.
  String? _homeDistrict;

  /// The pin for "Another area", same idea.
  LatLng? _otherPoint;
  bool _otherExact = false;

  File? _photo;
  List<String> _districts = const [];
  bool _districtsUnavailable = false;

  bool _locating = false;
  bool _submitting = false;
  String? _locationError;
  String? _error;

  List<Incident> _myReports = const [];
  bool _reportsLoading = false;
  String? _reportsError;

  /// The account "My reports" was loaded for, so switching accounts never
  /// shows one person's reports to another.
  String? _reportsOwner;

  @override
  void initState() {
    super.initState();
    // The checklist above the send button follows what has been typed.
    _title.addListener(_onTextChanged);
    _description.addListener(_onTextChanged);
    // Most reports are filed from the scene, so lead with the device's own
    // position rather than making someone type coordinates.
    if (widget.auth.isSignedIn) _locate();
    if (widget.auth.isSignedIn) _loadDistricts();
  }

  void _onTextChanged() => setState(() {});

  Future<void> _loadDistricts() async {
    final api = ApiClient(auth: widget.auth);
    try {
      final districts = await api.fetchDistricts();
      if (mounted) {
        setState(() {
          _districts = districts;
          _districtsUnavailable = districts.isEmpty;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _districtsUnavailable = true);
    } finally {
      api.dispose();
    }
  }

  @override
  void dispose() {
    _formScroll.dispose();
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

  /// The district on the reporter's profile, if they gave one.
  String? get _profileDistrict {
    final district = widget.auth.user?.district;
    return district == null || district.trim().isEmpty ? null : district;
  }

  /// The point the report is filed at, from whichever source is chosen.
  LatLng? get _reportPoint => switch (_source) {
    _LocationSource.gps =>
      _position == null
          ? null
          : LatLng(_position!.latitude, _position!.longitude),
    _LocationSource.home => _homePoint,
    _LocationSource.other => _otherPoint,
  };

  /// True when the pin is only a district's main town, not the real spot.
  bool get _pinApproximate => switch (_source) {
    _LocationSource.gps => false,
    _LocationSource.home => _homePoint != null && !_homeExact,
    _LocationSource.other => _otherPoint != null && !_otherExact,
  };

  void _chooseSource(_LocationSource source) {
    FocusScope.of(context).unfocus();
    setState(() {
      _source = source;
      _error = null;

      if (source == _LocationSource.home) {
        final district = _profileDistrict;
        // Start from the district's main town; a changed profile starts over.
        if (_homeDistrict != district) {
          _homeDistrict = district;
          _homePoint = townFor(district);
          _homeExact = false;
        }
        if (district != null) {
          _district.text = matchDistrict(district, _districts);
        }
      }
    });
  }

  /// A district picked while reporting "Another area" moves its pin there.
  void _onDistrictChanged(String district) {
    setState(() {
      _district.text = district;
      if (_source == _LocationSource.other) {
        final town = townFor(district);
        if (town != null) {
          _otherPoint = town;
          _otherExact = false;
        }
      }
    });
  }

  Future<void> _pickOnMap() async {
    FocusScope.of(context).unfocus();
    final current = _reportPoint;
    final gps = _position;
    final start = current ??
        (gps == null ? _islandCentre : LatLng(gps.latitude, gps.longitude));
    final zoom = current == null
        ? (gps == null ? 7.2 : 14.0)
        : (_pinApproximate ? 12.0 : 16.0);

    final picked = await Navigator.of(context).push<LatLng>(
      MaterialPageRoute(
        builder: (_) => LocationPickerScreen(initial: start, initialZoom: zoom),
      ),
    );
    if (picked == null || !mounted) return;

    setState(() {
      if (_source == _LocationSource.home) {
        _homePoint = picked;
        _homeExact = true;
      } else {
        _source = _LocationSource.other;
        _otherPoint = picked;
        _otherExact = true;
        // Guess the district from the pin, but never over one already chosen.
        if (_district.text.trim().isEmpty) {
          _district.text = matchDistrict(nearestDistrict(picked), _districts);
        }
      }
    });
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

  // ------------------------------------------------------------ my reports

  Future<void> _loadMyReports() async {
    final userId = widget.auth.user?.id;
    if (userId == null) return;

    final cached = await MyReportsStore.load(userId);
    if (!mounted || widget.auth.user?.id != userId) return;
    setState(() => _myReports = cached);
    await _refreshMyReports();
  }

  /// Re-reads every report by id for its current status. One that cannot be
  /// read keeps its last known state rather than vanishing from the list.
  Future<void> _refreshMyReports() async {
    final userId = widget.auth.user?.id;
    final snapshot = _myReports;
    if (userId == null || snapshot.isEmpty) return;

    setState(() {
      _reportsLoading = true;
      _reportsError = null;
    });

    final api = ApiClient(auth: widget.auth);
    var failures = 0;
    try {
      final fresh = await Future.wait(
        snapshot.map((report) async {
          try {
            return await api.fetchIncident(report.id);
          } catch (_) {
            failures++;
            return report;
          }
        }),
      );
      if (!mounted || widget.auth.user?.id != userId) return;

      // A report sent while this was running is kept, not overwritten.
      final byId = {for (final report in fresh) report.id: report};
      final merged = [for (final report in _myReports) byId[report.id] ?? report];

      setState(() {
        _myReports = merged;
        _reportsError = failures == snapshot.length
            ? 'Could not check for updates. Showing the last known status.'
            : null;
      });
      await MyReportsStore.save(userId, merged);
    } finally {
      api.dispose();
      if (mounted) setState(() => _reportsLoading = false);
    }
  }

  Future<void> _remember(Incident incident) async {
    final userId = widget.auth.user?.id;
    if (userId == null) return;

    setState(() {
      _myReports = [
        incident,
        ..._myReports.where((report) => report.id != incident.id),
      ];
    });
    await MyReportsStore.save(userId, _myReports);
  }

  // ---------------------------------------------------------------- submit

  /// What still stops the report being sent, in the words the checklist uses.
  List<String> get _missing => [
    if (_reportPoint == null) 'the location',
    if (_title.text.trim().length < 8) 'a short summary',
    if (_description.text.trim().length < 15) 'a description',
  ];

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    if (!(_formKey.currentState?.validate() ?? false)) return;

    final position = _reportPoint;
    if (position == null) {
      setState(
        () => _error = 'A location is needed before a report can be filed.',
      );
      return;
    }
    // "My area" always files under the profile's district.
    final district = _source == _LocationSource.home
        ? (_profileDistrict ?? _district.text)
        : _district.text;

    setState(() {
      _submitting = true;
      _error = null;
    });

    final api = ApiClient(auth: widget.auth);

    try {
      // With a photo, report and photo go up together so the analysis agent
      // sees the picture. The report is filed even if only the photo fails.
      String? photoWarning;
      Incident incident;
      final photo = _photo;
      if (photo != null) {
        final result = await api.createIncidentWithPhoto(
          title: _title.text,
          description: _description.text,
          type: _type,
          latitude: position.latitude,
          longitude: position.longitude,
          district: district,
          addressText: _address.text,
          estimatedAffectedPeople: int.tryParse(_people.text.trim()),
          photo: photo,
        );
        incident = result.incident;
        photoWarning = result.photoError;
      } else {
        incident = await api.createIncident(
          title: _title.text,
          description: _description.text,
          type: _type,
          latitude: position.latitude,
          longitude: position.longitude,
          district: district,
          addressText: _address.text,
          estimatedAffectedPeople: int.tryParse(_people.text.trim()),
        );
      }

      if (!mounted) return;
      await _remember(incident);
      if (!mounted) return;
      await _showSubmitted(photoWarning);
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _submitting = false;
        _error = error is ApiException
            ? error.message
            : 'Could not file the report.';
      });
      _formScroll.animateTo(
        0,
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeOut,
      );
    } finally {
      api.dispose();
    }
  }

  Future<void> _showSubmitted(String? photoWarning) async {
    final track = await showModalBottomSheet<bool>(
      context: context,
      isDismissible: false,
      enableDrag: false,
      useSafeArea: true,
      // Without this a sheet is capped at 9/16 of the screen, which the
      // buttons (plus a photo warning) overflow on smaller phones.
      isScrollControlled: true,
      backgroundColor: AppColors.surface,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(20)),
      ),
      builder: (_) => _SubmittedSheet(photoWarning: photoWarning),
    );

    if (!mounted) return;
    _reset();
    if (track ?? false) setState(() => _view = 1);
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
      // The chosen source and pins stay: the next report is often nearby.
      final home = _profileDistrict;
      if (_source == _LocationSource.home && home != null) {
        _district.text = matchDistrict(home, _districts);
      }
    });
    if (_formScroll.hasClients) _formScroll.jumpTo(0);
  }

  // ------------------------------------------------------------------ build

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: widget.auth,
      builder: (context, _) {
        final user = widget.auth.user;
        if (user == null) return _SignInPrompt(auth: widget.auth);

        if (_reportsOwner != user.id) {
          _reportsOwner = user.id;
          _myReports = const [];
          _reportsError = null;
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (mounted) _loadMyReports();
          });
        }

        return _buildScreen(context);
      },
    );
  }

  Widget _buildScreen(BuildContext context) {
    // On iOS the glass tab bar floats over the content, so the end of each
    // list must scroll clear of it.
    final bottomInset =
        MediaQuery.paddingOf(context).bottom + (_isIOS ? 100 : 28);

    return Scaffold(
      backgroundColor: AppColors.surfaceAlt,
      appBar: AppHeader(
        title: 'Reports',
        loading: _locating || _submitting || _reportsLoading,
      ),
      body: SafeArea(
        bottom: false,
        child: Column(
          children: [
            _ViewSwitcher(
              selected: _view,
              reportCount: _myReports.length,
              onChanged: (view) {
                FocusScope.of(context).unfocus();
                setState(() => _view = view);
              },
            ),
            Expanded(
              child: IndexedStack(
                index: _view,
                // Both stay alive: a half-filled report survives a look at
                // the list, and the list keeps its scroll position.
                children: [
                  _buildForm(bottomInset),
                  MyReportsView(
                    reports: _myReports,
                    loading: _reportsLoading,
                    error: _reportsError,
                    onRefresh: _refreshMyReports,
                    onNewReport: () => setState(() => _view = 0),
                    bottomInset: bottomInset,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildForm(double bottomInset) {
    final summaryDone = _title.text.trim().length >= 8;
    final descriptionDone = _description.text.trim().length >= 15;

    return RefreshIndicator(
      // A form has nothing to reload but where you are, so a pull re-reads
      // the location. Anything already typed is kept.
      onRefresh: _locate,
      child: ListView(
        controller: _formScroll,
        physics: const AlwaysScrollableScrollPhysics(),
        keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
        padding: EdgeInsets.fromLTRB(16, 16, 16, bottomInset),
        children: [
          const _SafetyNote(),
          const SizedBox(height: 16),

          if (_error != null) ...[
            _Banner(
              message: _error!,
              color: AppColors.critical,
              icon: Icons.error_outline,
            ),
            const SizedBox(height: 16),
          ],

          Form(
            key: _formKey,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _StepCard(
                  number: 1,
                  title: 'What is happening?',
                  subtitle: 'Pick the closest match.',
                  done: true,
                  child: _TypePicker(
                    selected: _type,
                    onChanged: (type) => setState(() => _type = type),
                  ),
                ),
                const SizedBox(height: AppSpacing.gap),

                _StepCard(
                  number: 2,
                  title: 'Where is it?',
                  subtitle: 'Choose what fits where you are right now.',
                  done: _reportPoint != null,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _SourceOption(
                        icon: Icons.my_location,
                        title: 'My current location',
                        subtitle: 'GPS — best when you are at the scene.',
                        selected: _source == _LocationSource.gps,
                        onTap: () => _chooseSource(_LocationSource.gps),
                      ),
                      const SizedBox(height: 8),
                      _SourceOption(
                        icon: Icons.home_outlined,
                        title: _profileDistrict == null
                            ? 'My area'
                            : 'My area · $_profileDistrict',
                        subtitle: _profileDistrict == null
                            ? 'Add your district in Profile to use this.'
                            : 'The district on your profile.',
                        selected: _source == _LocationSource.home,
                        enabled: _profileDistrict != null,
                        onTap: () => _chooseSource(_LocationSource.home),
                      ),
                      const SizedBox(height: 8),
                      _SourceOption(
                        icon: Icons.travel_explore,
                        title: 'Another area',
                        subtitle: 'Somewhere else — pick a district or the map.',
                        selected: _source == _LocationSource.other,
                        onTap: () => _chooseSource(_LocationSource.other),
                      ),
                      const SizedBox(height: 14),

                      if (_source == _LocationSource.gps)
                        _LocationStatus(
                          position: _position,
                          busy: _locating,
                          error: _locationError,
                          outOfBounds: _position != null && !_inSriLanka,
                          onRetry: _locate,
                        )
                      else
                        _AreaPanel(
                          point: _reportPoint,
                          approximate: _pinApproximate,
                          placeName: _district.text.trim().isEmpty
                              ? null
                              : _district.text.trim(),
                          emptyMessage: _source == _LocationSource.home
                              ? 'Your profile district could not be placed on '
                                    'the map. Mark the spot instead.'
                              : 'Choose the district below, or find the '
                                    'place on the map.',
                          onPickOnMap: _pickOnMap,
                        ),
                      const SizedBox(height: 14),

                      if (_source == _LocationSource.home)
                        _HomeDistrictNote(district: _profileDistrict ?? '')
                      else
                        _buildDistrictField(),
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _address,
                        maxLength: 300,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: _fieldDecoration(
                          'Landmark or address (optional)',
                          hint: 'Near the Kelani bridge, Peliyagoda side',
                          icon: Icons.place_outlined,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: AppSpacing.gap),

                _StepCard(
                  number: 3,
                  title: 'Describe it',
                  subtitle: 'Say what you can see — not how serious it is.',
                  done: summaryDone && descriptionDone,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      TextFormField(
                        controller: _title,
                        maxLength: 200,
                        textCapitalization: TextCapitalization.sentences,
                        textInputAction: TextInputAction.next,
                        decoration: _fieldDecoration(
                          'Short summary',
                          hint: 'Main road flooded near the bridge',
                          icon: Icons.short_text,
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
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _description,
                        minLines: 4,
                        maxLines: 8,
                        maxLength: 4000,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: _fieldDecoration(
                          'What is happening?',
                          hint:
                              'Water is waist deep and rising. Several houses '
                              'cut off.',
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
                      const SizedBox(height: 12),
                      TextFormField(
                        controller: _people,
                        keyboardType: TextInputType.number,
                        decoration: _fieldDecoration(
                          'People affected (optional)',
                          hint: 'A rough estimate is fine',
                          icon: Icons.groups_outlined,
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
                    ],
                  ),
                ),
                const SizedBox(height: AppSpacing.gap),

                _StepCard(
                  number: 4,
                  title: 'Add a photo',
                  subtitle:
                      'The strongest signal for the analysis agent — it reads '
                      'the image with your description.',
                  optional: true,
                  done: _photo != null,
                  child: _PhotoField(
                    photo: _photo,
                    onCamera: () => _addPhoto(ImageSource.camera),
                    onGallery: () => _addPhoto(ImageSource.gallery),
                    onRemove: () => setState(() => _photo = null),
                  ),
                ),
              ],
            ),
          ),

          const SizedBox(height: 20),
          _SubmitSection(
            missing: _missing,
            submitting: _submitting,
            withPhoto: _photo != null,
            onSubmit: _reportPoint == null ? null : _submit,
          ),
        ],
      ),
    );
  }

  Widget _buildDistrictField() {
    if (_districts.isNotEmpty) {
      final current = _districts.contains(_district.text) ? _district.text : null;
      return DropdownButtonFormField<String>(
        // initialValue is read once; keying on it lets a district set in
        // code (a map pin, "My area") show up here too.
        key: ValueKey('district-$current'),
        initialValue: current,
        isExpanded: true,
        decoration: _fieldDecoration(
          'District',
          icon: Icons.location_city_outlined,
        ),
        hint: const Text('Select district'),
        items: _districts
            .map(
              (district) => DropdownMenuItem(
                value: district,
                child: Text(district, overflow: TextOverflow.ellipsis),
              ),
            )
            .toList(),
        onChanged: (district) => _onDistrictChanged(district ?? ''),
      );
    }

    return TextFormField(
      controller: _district,
      textCapitalization: TextCapitalization.words,
      onFieldSubmitted: _onDistrictChanged,
      decoration: _fieldDecoration(
        'District',
        hint: _districtsUnavailable ? 'Enter district' : 'Loading districts…',
        icon: Icons.location_city_outlined,
      ),
    );
  }
}

/// One look for every field on the form: filled, rounded, amber on focus.
InputDecoration _fieldDecoration(String label, {String? hint, IconData? icon}) {
  OutlineInputBorder border(Color colour, [double width = 1]) =>
      OutlineInputBorder(
        borderRadius: BorderRadius.circular(10),
        borderSide: BorderSide(color: colour, width: width),
      );

  return InputDecoration(
    labelText: label,
    hintText: hint,
    hintStyle: const TextStyle(color: Color(0xFF9AA3AF)),
    prefixIcon: icon == null ? null : Icon(icon, size: 20),
    alignLabelWithHint: true,
    filled: true,
    fillColor: AppColors.surfaceAlt,
    // Length limits still apply; a running count is just noise here.
    counterText: '',
    border: border(AppColors.border),
    enabledBorder: border(AppColors.border),
    focusedBorder: border(AppColors.brand, 1.8),
    errorBorder: border(AppColors.critical),
    focusedErrorBorder: border(AppColors.critical, 1.8),
  );
}

// ---------------------------------------------------------------- fragments

class _ViewSwitcher extends StatelessWidget {
  const _ViewSwitcher({
    required this.selected,
    required this.reportCount,
    required this.onChanged,
  });

  final int selected;
  final int reportCount;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
      decoration: const BoxDecoration(
        color: AppColors.surface,
        border: Border(bottom: BorderSide(color: AppColors.border)),
      ),
      child: Container(
        padding: const EdgeInsets.all(4),
        decoration: BoxDecoration(
          color: AppColors.surfaceAlt,
          border: Border.all(color: AppColors.border),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Row(
          children: [
            _segment(0, Icons.edit_note, 'New report'),
            _segment(1, Icons.inbox_outlined, 'My reports', badge: reportCount),
          ],
        ),
      ),
    );
  }

  Widget _segment(int index, IconData icon, String label, {int badge = 0}) {
    final active = selected == index;

    return Expanded(
      child: Semantics(
        button: true,
        selected: active,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: () => onChanged(index),
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 200),
            padding: const EdgeInsets.symmetric(vertical: 10),
            decoration: BoxDecoration(
              color: active ? AppColors.surface : Colors.transparent,
              borderRadius: BorderRadius.circular(9),
              boxShadow: active
                  ? const [
                      BoxShadow(
                        color: Color(0x14000000),
                        blurRadius: 6,
                        offset: Offset(0, 1),
                      ),
                    ]
                  : null,
            ),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(
                  icon,
                  size: 18,
                  color: active ? AppColors.ink : AppColors.body,
                ),
                const SizedBox(width: 6),
                Flexible(
                  child: Text(
                    label,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 13.5,
                      fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                      color: active ? AppColors.ink : AppColors.body,
                    ),
                  ),
                ),
                if (badge > 0) ...[
                  const SizedBox(width: 6),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 7,
                      vertical: 1,
                    ),
                    decoration: BoxDecoration(
                      color: AppColors.brand,
                      borderRadius: BorderRadius.circular(99),
                    ),
                    child: Text(
                      '$badge',
                      style: const TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: AppColors.brandInk,
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _SafetyNote extends StatelessWidget {
  const _SafetyNote();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: AppColors.brand.withValues(alpha: 0.12),
        border: Border.all(color: AppColors.brand.withValues(alpha: 0.4)),
        borderRadius: AppSpacing.radius,
      ),
      child: const Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.shield_outlined, size: 22, color: AppColors.brandInk),
          SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Stay safe first',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w700,
                    color: AppColors.ink,
                  ),
                ),
                SizedBox(height: 3),
                Text(
                  'Report from a safe place. You do not need to judge how '
                  'serious it is — a coordinator reviews every report.',
                  style: TextStyle(fontSize: 12.5, height: 1.4),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// A numbered section of the form. Its badge turns into a tick once the
/// section has what it needs, so progress is visible at a glance.
class _StepCard extends StatelessWidget {
  const _StepCard({
    required this.number,
    required this.title,
    required this.done,
    required this.child,
    this.subtitle,
    this.optional = false,
  });

  final int number;
  final String title;
  final String? subtitle;
  final bool done;
  final bool optional;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              AnimatedContainer(
                duration: const Duration(milliseconds: 200),
                width: 28,
                height: 28,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: done ? AppColors.brand : AppColors.surfaceAlt,
                  shape: BoxShape.circle,
                  border: done ? null : Border.all(color: AppColors.border),
                ),
                child: done
                    ? const Icon(Icons.check, size: 16, color: AppColors.brandInk)
                    : Text(
                        '$number',
                        style: const TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                          color: AppColors.body,
                        ),
                      ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Flexible(
                          child: Text(
                            title,
                            style: const TextStyle(
                              fontSize: 15.5,
                              fontWeight: FontWeight.w700,
                              color: AppColors.ink,
                            ),
                          ),
                        ),
                        if (optional) ...[
                          const SizedBox(width: 8),
                          const AppPill('Optional'),
                        ],
                      ],
                    ),
                    if (subtitle != null) ...[
                      const SizedBox(height: 2),
                      Text(
                        subtitle!,
                        style: const TextStyle(fontSize: 12.5, height: 1.4),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          child,
        ],
      ),
    );
  }
}

/// Disaster types as a grid of large tiles — easier to hit with a wet thumb
/// than a row of chips.
class _TypePicker extends StatelessWidget {
  const _TypePicker({required this.selected, required this.onChanged});

  final String selected;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        const spacing = 8.0;
        const columns = 4;
        final width =
            (constraints.maxWidth - spacing * (columns - 1)) / columns;

        return Wrap(
          spacing: spacing,
          runSpacing: spacing,
          children: [
            for (final type in incidentTypes)
              _TypeTile(
                type: type,
                width: width,
                selected: type == selected,
                onTap: () => onChanged(type),
              ),
          ],
        );
      },
    );
  }
}

class _TypeTile extends StatelessWidget {
  const _TypeTile({
    required this.type,
    required this.width,
    required this.selected,
    required this.onTap,
  });

  final String type;
  final double width;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      selected: selected,
      label: type,
      excludeSemantics: true,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(12),
          child: AnimatedContainer(
            duration: const Duration(milliseconds: 180),
            width: width,
            height: 76,
            decoration: BoxDecoration(
              color: selected
                  ? AppColors.brand.withValues(alpha: 0.16)
                  : AppColors.surfaceAlt,
              border: Border.all(
                color: selected ? AppColors.brand : AppColors.border,
                width: selected ? 1.8 : 1,
              ),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(
                  iconForType(type),
                  size: 26,
                  color: selected ? AppColors.brandInk : AppColors.body,
                ),
                const SizedBox(height: 6),
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 4),
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Text(
                      type,
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                        color: selected ? AppColors.ink : AppColors.body,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// One of the three "where is it?" choices, as a large radio row.
class _SourceOption extends StatelessWidget {
  const _SourceOption({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.selected,
    required this.onTap,
    this.enabled = true,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final bool selected;
  final bool enabled;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final ink = enabled ? AppColors.ink : AppColors.body;

    return Semantics(
      button: true,
      selected: selected,
      enabled: enabled,
      child: Opacity(
        opacity: enabled ? 1 : 0.55,
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            onTap: enabled ? onTap : null,
            borderRadius: BorderRadius.circular(12),
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 180),
              padding: const EdgeInsets.fromLTRB(12, 11, 10, 11),
              decoration: BoxDecoration(
                color: selected
                    ? AppColors.brand.withValues(alpha: 0.12)
                    : AppColors.surfaceAlt,
                border: Border.all(
                  color: selected ? AppColors.brand : AppColors.border,
                  width: selected ? 1.8 : 1,
                ),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    decoration: BoxDecoration(
                      color: selected ? AppColors.brand : AppColors.surface,
                      shape: BoxShape.circle,
                      border: selected
                          ? null
                          : Border.all(color: AppColors.border),
                    ),
                    child: Icon(
                      icon,
                      size: 19,
                      color: selected ? AppColors.brandInk : ink,
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                            color: ink,
                          ),
                        ),
                        const SizedBox(height: 1),
                        Text(
                          subtitle,
                          style: const TextStyle(fontSize: 12, height: 1.35),
                        ),
                      ],
                    ),
                  ),
                  Icon(
                    selected
                        ? Icons.radio_button_checked
                        : Icons.radio_button_unchecked,
                    size: 22,
                    color: selected ? AppColors.brandInk : AppColors.border,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// "My area" / "Another area": the pin on a small map, and a clear word
/// when it is only a district's main town rather than the real spot.
class _AreaPanel extends StatelessWidget {
  const _AreaPanel({
    required this.point,
    required this.approximate,
    required this.placeName,
    required this.emptyMessage,
    required this.onPickOnMap,
  });

  final LatLng? point;
  final bool approximate;
  final String? placeName;
  final String emptyMessage;
  final VoidCallback onPickOnMap;

  @override
  Widget build(BuildContext context) {
    final pin = point;

    if (pin == null) {
      return Container(
        padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(
          color: AppColors.surfaceAlt,
          border: Border.all(color: AppColors.border),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(emptyMessage, style: const TextStyle(fontSize: 13, height: 1.4)),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: onPickOnMap,
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(44),
                foregroundColor: AppColors.ink,
              ),
              icon: const Icon(Icons.map_outlined, size: 19),
              label: const Text('Find on map'),
            ),
          ],
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(10),
          child: SizedBox(
            height: 170,
            child: Stack(
              children: [
                // A preview only: panning a map inside a scrolling form
                // fights the scroll, so adjusting happens full screen.
                FlutterMap(
                  key: ValueKey('${pin.latitude},${pin.longitude},$approximate'),
                  options: MapOptions(
                    initialCenter: pin,
                    initialZoom: approximate ? 11 : 15,
                    interactionOptions: const InteractionOptions(
                      flags: InteractiveFlag.none,
                    ),
                    onTap: (_, _) => onPickOnMap(),
                  ),
                  children: [
                    TileLayer(
                      urlTemplate:
                          'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
                      userAgentPackageName: 'lk.rescuesrilanka.mobile',
                    ),
                    MarkerLayer(
                      markers: [
                        Marker(
                          point: pin,
                          width: 40,
                          height: 40,
                          alignment: Alignment.topCenter,
                          child: const Icon(
                            Icons.location_on,
                            size: 40,
                            color: AppColors.ink,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
                Positioned(
                  right: 8,
                  bottom: 8,
                  child: FilledButton.icon(
                    onPressed: onPickOnMap,
                    style: FilledButton.styleFrom(
                      backgroundColor: AppColors.surface,
                      foregroundColor: AppColors.ink,
                      side: const BorderSide(color: AppColors.border),
                      visualDensity: VisualDensity.compact,
                    ),
                    icon: const Icon(Icons.edit_location_alt_outlined, size: 18),
                    label: Text(approximate ? 'Mark exact spot' : 'Adjust pin'),
                  ),
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: 10),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              approximate ? Icons.info_outline : Icons.check_circle_outline,
              size: 17,
              color: approximate ? AppColors.high : AppColors.low,
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                approximate
                    ? 'Pinned at the main town of ${placeName ?? 'the district'} '
                          '— only roughly where it is. Mark the exact spot if '
                          'you know it, so responders go to the right place.'
                    : 'Exact spot marked · '
                          '${pin.latitude.toStringAsFixed(5)}, '
                          '${pin.longitude.toStringAsFixed(5)}',
                style: const TextStyle(fontSize: 12.5, height: 1.4),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

/// Under "My area" the district is the profile's, so it is stated rather
/// than asked for again.
class _HomeDistrictNote extends StatelessWidget {
  const _HomeDistrictNote({required this.district});

  final String district;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
      decoration: BoxDecoration(
        color: AppColors.surfaceAlt,
        border: Border.all(color: AppColors.border),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        children: [
          const Icon(Icons.location_city_outlined, size: 20, color: AppColors.body),
          const SizedBox(width: 12),
          const Text('District', style: TextStyle(fontSize: 13)),
          const Spacer(),
          Text(
            district,
            style: const TextStyle(
              fontSize: 13.5,
              fontWeight: FontWeight.w600,
              color: AppColors.ink,
            ),
          ),
          const SizedBox(width: 6),
          const AppPill('From profile'),
        ],
      ),
    );
  }
}

class _LocationStatus extends StatelessWidget {
  const _LocationStatus({
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
    final found = resolved != null && !busy;

    final String heading;
    if (busy) {
      heading = 'Finding your location…';
    } else if (error != null && resolved == null) {
      heading = 'Location not available';
    } else if (resolved != null) {
      heading = 'Using your current location';
    } else {
      heading = 'Location not set yet';
    }

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: found ? AppColors.low.withValues(alpha: 0.07) : AppColors.surfaceAlt,
        border: Border.all(
          color: found ? AppColors.low.withValues(alpha: 0.35) : AppColors.border,
        ),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: found
                  ? AppColors.low.withValues(alpha: 0.15)
                  : AppColors.surface,
              shape: BoxShape.circle,
              border: found ? null : Border.all(color: AppColors.border),
            ),
            child: busy
                ? const Padding(
                    padding: EdgeInsets.all(10),
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Icon(
                    found ? Icons.my_location : Icons.location_searching,
                    size: 19,
                    color: found ? AppColors.low : AppColors.body,
                  ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  heading,
                  style: const TextStyle(
                    fontSize: 13.5,
                    fontWeight: FontWeight.w600,
                    color: AppColors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                if (!busy && error != null)
                  Text(
                    error!,
                    style: const TextStyle(
                      fontSize: 12.5,
                      height: 1.4,
                      color: AppColors.high,
                    ),
                  )
                else if (resolved != null) ...[
                  Text(
                    '${resolved.latitude.toStringAsFixed(5)}, '
                    '${resolved.longitude.toStringAsFixed(5)}  ·  '
                    '±${resolved.accuracy.round()} m',
                    style: const TextStyle(fontSize: 12.5),
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
                ] else if (!busy)
                  const Text(
                    'Needed so responders can find the scene.',
                    style: TextStyle(fontSize: 12.5),
                  ),
              ],
            ),
          ),
          if (!busy)
            TextButton(
              onPressed: onRetry,
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
    final picked = photo;

    if (picked != null) {
      return Stack(
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: Image.file(
              picked,
              height: 200,
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
                icon: const Icon(Icons.close, size: 18, color: Colors.white),
                onPressed: onRemove,
                tooltip: 'Remove photo',
              ),
            ),
          ),
          Positioned(
            left: 8,
            bottom: 8,
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
              decoration: BoxDecoration(
                color: Colors.black54,
                borderRadius: BorderRadius.circular(99),
              ),
              child: const Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.check_circle, size: 14, color: Colors.white),
                  SizedBox(width: 5),
                  Text(
                    'Photo attached',
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w600,
                      color: Colors.white,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      );
    }

    return Row(
      children: [
        Expanded(
          child: _PhotoSource(
            icon: Icons.photo_camera_outlined,
            label: 'Take photo',
            onTap: onCamera,
          ),
        ),
        const SizedBox(width: 10),
        Expanded(
          child: _PhotoSource(
            icon: Icons.photo_library_outlined,
            label: 'From gallery',
            onTap: onGallery,
          ),
        ),
      ],
    );
  }
}

class _PhotoSource extends StatelessWidget {
  const _PhotoSource({
    required this.icon,
    required this.label,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppColors.surfaceAlt,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: const BorderSide(color: AppColors.border),
      ),
      child: InkWell(
        onTap: onTap,
        customBorder: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(12),
        ),
        child: SizedBox(
          height: 92,
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 28, color: AppColors.ink),
              const SizedBox(height: 8),
              Text(
                label,
                style: const TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                  color: AppColors.ink,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SubmitSection extends StatelessWidget {
  const _SubmitSection({
    required this.missing,
    required this.submitting,
    required this.withPhoto,
    required this.onSubmit,
  });

  final List<String> missing;
  final bool submitting;
  final bool withPhoto;
  final VoidCallback? onSubmit;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (missing.isNotEmpty) ...[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.info_outline, size: 16, color: AppColors.body),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Still needed: ${missing.join(', ')}.',
                  style: const TextStyle(fontSize: 12.5, height: 1.4),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
        ],
        AppPrimaryButton(
          label: submitting
              ? (withPhoto ? 'Sending report and photo…' : 'Sending report…')
              : 'Send report',
          icon: Icons.send_rounded,
          busy: submitting,
          onPressed: onSubmit,
        ),
        const SizedBox(height: 10),
        const Text(
          'You can follow its progress under My reports.',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 12, color: AppColors.body),
        ),
      ],
    );
  }
}

/// Shown once a report is in. Pops true to go and follow it.
class _SubmittedSheet extends StatelessWidget {
  const _SubmittedSheet({required this.photoWarning});

  final String? photoWarning;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(
        24,
        28,
        24,
        20 + MediaQuery.paddingOf(context).bottom,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Center(
            child: Container(
              width: 64,
              height: 64,
              decoration: BoxDecoration(
                color: AppColors.low.withValues(alpha: 0.14),
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.check_rounded,
                size: 36,
                color: AppColors.low,
              ),
            ),
          ),
          const SizedBox(height: 16),
          const Text(
            'Report sent',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 20,
              fontWeight: FontWeight.w700,
              color: AppColors.ink,
            ),
          ),
          const SizedBox(height: 8),
          const Text(
            'An emergency coordinator has been notified and will review it '
            'before it is acted on. Thank you for keeping others safe.',
            textAlign: TextAlign.center,
            style: TextStyle(fontSize: 13.5, height: 1.5),
          ),
          if (photoWarning != null) ...[
            const SizedBox(height: 14),
            _Banner(
              message: 'The report was sent, but the photo did not upload: '
                  '$photoWarning',
              color: AppColors.high,
              icon: Icons.image_not_supported_outlined,
            ),
          ],
          const SizedBox(height: 24),
          AppPrimaryButton(
            label: 'Track in My reports',
            icon: Icons.inbox_outlined,
            onPressed: () => Navigator.of(context).pop(true),
          ),
          const SizedBox(height: 10),
          OutlinedButton(
            onPressed: () => Navigator.of(context).pop(false),
            style: OutlinedButton.styleFrom(
              minimumSize: const Size.fromHeight(48),
              foregroundColor: AppColors.ink,
            ),
            child: const Text('File another report'),
          ),
        ],
      ),
    );
  }
}

class _SignInPrompt extends StatelessWidget {
  const _SignInPrompt({required this.auth});

  final AuthService auth;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: const AppHeader(title: 'Reports'),
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
