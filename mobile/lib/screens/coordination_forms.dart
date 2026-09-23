import 'package:flutter/material.dart';

import '../models/auth_models.dart';
import '../models/coordination_requests.dart';
import '../models/incident_reference.dart';
import '../services/rescue_coordination_service.dart';

String mutationError(Object error) => error is RescueCoordinationException
    ? error.message
    : 'Unable to complete the action. Refresh before trying again.';

Future<bool> confirmCoordinationAction(
  BuildContext context,
  String message,
) async =>
    await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Confirm action'),
        content: Text(message),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Confirm'),
          ),
        ],
      ),
    ) ??
    false;

enum ResourceKind { team, member, vehicle }

class ResourceEditor extends StatefulWidget {
  const ResourceEditor({
    super.key,
    required this.session,
    required this.kind,
    this.teamId,
    this.existing,
  });
  final AuthSession session;
  final ResourceKind kind;
  final String? teamId;
  final Map<String, dynamic>? existing;
  @override
  State<ResourceEditor> createState() => _ResourceEditorState();
}

class _ResourceEditorState extends State<ResourceEditor> {
  final _form = GlobalKey<FormState>();
  final _service = RescueCoordinationService();
  final Map<String, String> _text = {};
  String? _status;
  String? _skill;
  String? _type;
  bool _available = true;
  bool _busy = false;
  String? _error;
  bool get _editing => widget.existing != null;

  @override
  void initState() {
    super.initState();
    final item = widget.existing ?? {};
    for (final key in [
      'name',
      'baseLatitude',
      'baseLongitude',
      'fullName',
      'phone',
      'plateNumber',
      'capacity',
    ]) {
      _text[key] = item[key]?.toString() ?? '';
    }
    _status = item['status'] as String?;
    _skill = item['skill'] as String?;
    _type = item['type'] as String?;
    _available = item['isAvailable'] as bool? ?? true;
  }

  Widget _field(
    String key,
    String label, {
    int? max,
    bool optional = false,
    bool numeric = false,
    String? Function(String)? validate,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: TextFormField(
      initialValue: _text[key],
      enabled: !_busy,
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
      ),
      keyboardType: numeric
          ? const TextInputType.numberWithOptions(decimal: true, signed: true)
          : TextInputType.text,
      maxLength: max,
      onChanged: (v) => _text[key] = v.trim(),
      validator: (v) {
        final value = v?.trim() ?? '';
        if (value.isEmpty) return optional ? null : 'Required';
        return validate?.call(value);
      },
    ),
  );

  Widget _choice(
    String label,
    String? value,
    List<String> options,
    ValueChanged<String?> changed,
  ) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: DropdownButtonFormField<String>(
      initialValue: value,
      isExpanded: true,
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
      ),
      items: options
          .map((v) => DropdownMenuItem(value: v, child: Text(v)))
          .toList(),
      onChanged: _busy ? null : changed,
      validator: (v) => v == null ? 'Required' : null,
    ),
  );

  String? _coordinate(String value, double max) {
    final number = double.tryParse(value);
    return number == null || !number.isFinite || number.abs() > max
        ? 'Enter a value from -$max to $max.'
        : null;
  }

  Future<void> _save() async {
    if (_busy || !_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final id = widget.existing?['id'] as String?;
      switch (widget.kind) {
        case ResourceKind.team:
          final data = TeamInput(
            name: _text['name']!,
            status: _status,
            baseLatitude: double.tryParse(_text['baseLatitude']!),
            baseLongitude: double.tryParse(_text['baseLongitude']!),
          );
          if (id == null) {
            await _service.createTeam(widget.session, data);
          } else {
            await _service.updateTeam(widget.session, id, data);
          }
        case ResourceKind.member:
          final data = MemberInput(
            fullName: _text['fullName']!,
            phone: _text['phone']!,
            skill: _skill!,
            isAvailable: _available,
          );
          if (id == null) {
            await _service.addMember(widget.session, widget.teamId!, data);
          } else {
            await _service.updateMember(
              widget.session,
              widget.teamId!,
              id,
              data,
            );
          }
        case ResourceKind.vehicle:
          final data = VehicleInput(
            plateNumber: _text['plateNumber']!,
            type: _type!,
            status: _status,
            capacity: int.parse(_text['capacity']!),
          );
          if (id == null) {
            await _service.addVehicle(widget.session, widget.teamId!, data);
          } else {
            await _service.updateVehicle(
              widget.session,
              widget.teamId!,
              id,
              data,
            );
          }
      }
      if (!mounted) return;
      setState(() => _busy = false);
      Navigator.pop(context, true);
    } catch (error) {
      if (mounted) {
        setState(() {
          _error = mutationError(error);
          _busy = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_busy,
    child: AlertDialog(
      title: Text('${_editing ? 'Edit' : 'Add'} ${widget.kind.name}'),
      content: SizedBox(
        width: 440,
        child: SingleChildScrollView(
          child: Form(
            key: _form,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (widget.kind == ResourceKind.team) ...[
                  _field('name', 'Team name', max: 150),
                  _field(
                    'baseLatitude',
                    'Base latitude (optional)',
                    optional: true,
                    numeric: true,
                    validate: (v) => _coordinate(v, 90),
                  ),
                  _field(
                    'baseLongitude',
                    'Base longitude (optional)',
                    optional: true,
                    numeric: true,
                    validate: (v) => _coordinate(v, 180),
                  ),
                  if (_editing)
                    _choice(
                      'Status',
                      _status,
                      teamStatuses,
                      (v) => setState(() => _status = v),
                    ),
                ],
                if (widget.kind == ResourceKind.member) ...[
                  _field('fullName', 'Full name', max: 150),
                  _field(
                    'phone',
                    'Phone',
                    validate: (v) => RegExp(r'^[0-9+\-\s]{7,15}$').hasMatch(v)
                        ? null
                        : 'Use 7-15 phone characters.',
                  ),
                  _choice(
                    'Skill',
                    _skill,
                    coordinationSkills,
                    (v) => setState(() => _skill = v),
                  ),
                  if (_editing)
                    SwitchListTile(
                      title: const Text('Available'),
                      value: _available,
                      onChanged: _busy
                          ? null
                          : (v) => setState(() => _available = v),
                    ),
                ],
                if (widget.kind == ResourceKind.vehicle) ...[
                  _field('plateNumber', 'Registration', max: 20),
                  _choice(
                    'Vehicle type',
                    _type,
                    vehicleTypes,
                    (v) => setState(() => _type = v),
                  ),
                  _field(
                    'capacity',
                    'Capacity',
                    numeric: true,
                    validate: (v) {
                      final n = int.tryParse(v);
                      return n == null || n < 1 || n > 100
                          ? 'Capacity must be 1-100.'
                          : null;
                    },
                  ),
                  if (_editing)
                    _choice(
                      'Status',
                      _status,
                      vehicleStatuses,
                      (v) => setState(() => _status = v),
                    ),
                ],
                if (_error != null)
                  Text(
                    _error!,
                    style: const TextStyle(color: Color(0xFFB3261E)),
                  ),
                if (_busy) const LinearProgressIndicator(),
              ],
            ),
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: _busy ? null : () => Navigator.pop(context, false),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _busy ? null : _save,
          child: const Text('Save'),
        ),
      ],
    ),
  );
}

class AssignmentEditor extends StatefulWidget {
  const AssignmentEditor({super.key, required this.session, this.existing});
  final AuthSession session;
  final Map<String, dynamic>? existing;
  @override
  State<AssignmentEditor> createState() => _AssignmentEditorState();
}

class _AssignmentEditorState extends State<AssignmentEditor> {
  final _form = GlobalKey<FormState>();
  final _service = RescueCoordinationService();
  List<Map<String, dynamic>> _teams = [];
  String? _teamId, _vehicleId, _skill;
  String _capacity = '', _notes = '';
  bool _loading = false, _saving = false;
  String? _loadError, _error;
  bool get _editing => widget.existing != null;
  List<IncidentReference> _incidents = [];
  IncidentReference? _existingIncident;
  String? _selectedIncidentId;
  bool _incidentsLoading = false;
  String? _incidentsError;

  IncidentReference? get _selectedIncident {
    for (final incident in _incidents) {
      if (incident.id == _selectedIncidentId && incident.isActive) {
        return incident;
      }
    }
    return null;
  }

  Future<void> _loadIncidents() async {
    if (_incidentsLoading || _saving) return;
    if (_editing && widget.existing?['incidentId'] == null) return;
    setState(() {
      _incidentsLoading = true;
      _incidentsError = null;
    });
    try {
      if (_editing) {
        // By-ID also resolves inactive incidents without changing their reference.
        final incident = await _service.getIncidentById(
          widget.session,
          widget.existing!['incidentId'],
        );
        if (!mounted) return;
        setState(() => _existingIncident = incident);
      } else {
        final incidents = await _service.getActiveIncidents(widget.session);
        if (!mounted) return;
        setState(() {
          _incidents = incidents;
          if (_selectedIncident == null) _selectedIncidentId = null;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(
          () => _incidentsError =
              'Unable to load incidents. ${mutationError(error)}',
        );
      }
    } finally {
      if (mounted) setState(() => _incidentsLoading = false);
    }
  }

  Widget _incidentSummary(IncidentReference incident) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          incident.title,
          style: const TextStyle(fontWeight: FontWeight.w700),
        ),
        Text('Type: ${incident.type}'),
        if (incident.district?.trim().isNotEmpty == true)
          Text('District: ${incident.district}')
        else if (incident.addressText?.trim().isNotEmpty == true)
          Text('Location: ${incident.addressText}'),
        Text('Severity: ${incident.severity} • Status: ${incident.status}'),
      ],
    ),
  );

  Widget _referenceSelector() {
    if (_editing && widget.existing?['incidentId'] == null) {
      return const Text(
        'Existing help request reference (read-only). Details are unavailable; the reference remains unchanged.',
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(_editing ? 'Incident (read-only)' : 'Reference type: Incident'),
        const SizedBox(height: 12),
        if (_incidentsLoading) ...[
          const LinearProgressIndicator(),
          const Text('Loading incidents...'),
        ] else if (_incidentsError != null) ...[
          Text(
            _incidentsError!,
            style: const TextStyle(color: Color(0xFFB3261E)),
          ),
          TextButton.icon(
            onPressed: _saving ? null : _loadIncidents,
            icon: const Icon(Icons.refresh),
            label: const Text('Retry'),
          ),
          if (_editing) const Text('The existing reference remains unchanged.'),
        ] else if (_editing) ...[
          if (_existingIncident != null) _incidentSummary(_existingIncident!),
        ] else if (_incidents.isEmpty)
          const Text('No active incidents available.')
        else ...[
          DropdownButtonFormField<String>(
            initialValue: _selectedIncidentId,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: 'Incident',
              border: OutlineInputBorder(),
            ),
            items: _incidents
                .map(
                  (incident) => DropdownMenuItem(
                    value: incident.id,
                    child: Text(
                      incident.label,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                )
                .toList(),
            onChanged: _saving
                ? null
                : (id) => setState(() => _selectedIncidentId = id),
            validator: (_) =>
                _selectedIncident == null ? 'Select an incident.' : null,
          ),
          if (_selectedIncident != null) _incidentSummary(_selectedIncident!),
        ],
      ],
    );
  }

  List<Map<String, dynamic>> get _vehicles {
    final selected = _teams.where((t) => t['id'] == _teamId);
    return selected.isEmpty
        ? []
        : (selected.first['vehicles'] as List).cast<Map<String, dynamic>>();
  }

  @override
  void initState() {
    super.initState();
    final a = widget.existing;
    _teamId = a?['rescueTeamId'];
    _vehicleId = a?['vehicleId'];
    _skill = a?['requiredSkill'];
    _capacity = a?['requiredCapacity']?.toString() ?? '';
    _notes = a?['notes'] ?? '';
    _loadTeams();
    _loadIncidents();
  }

  Future<void> _loadTeams() async {
    if (_loading || _saving) return;
    setState(() {
      _loading = true;
      _loadError = null;
    });
    try {
      final data = await _service.getRescueTeams(widget.session);
      if (!mounted) return;
      final teams = data.cast<Map<String, dynamic>>().toList();
      setState(() {
        _teams = teams;
        if (!_teams.any((t) => t['id'] == _teamId)) _teamId = null;
        if (!_vehicles.any((v) => v['id'] == _vehicleId)) _vehicleId = null;
      });
    } catch (error) {
      if (mounted) setState(() => _loadError = mutationError(error));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _save() async {
    if (_saving ||
        _loading ||
        (!_editing &&
            (_incidentsLoading ||
                _incidentsError != null ||
                _selectedIncident == null)) ||
        !_form.currentState!.validate()) {
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      final data = AssignmentInput(
        rescueTeamId: _teamId!,
        vehicleId: _vehicleId!,
        requiredSkill: _skill!,
        requiredCapacity: int.parse(_capacity),
        notes: _notes.trim().isEmpty ? null : _notes.trim(),
        incidentId: _editing
            ? (widget.existing?['incidentId'] as String?)
            : _selectedIncident!.id,
        helpRequestId: widget.existing?['helpRequestId'] as String?,
      );
      if (_editing) {
        await _service.reviseAssignment(
          widget.session,
          widget.existing!['id'],
          data,
        );
      } else {
        await _service.createAssignment(widget.session, data);
      }
      if (!mounted) return;
      setState(() => _saving = false);
      Navigator.pop(context, true);
    } catch (error) {
      if (mounted) {
        setState(() {
          _error = mutationError(error);
          _saving = false;
        });
      }
    }
  }

  Widget _select(
    String label,
    String? value,
    List<DropdownMenuItem<String>> items,
    ValueChanged<String?> changed, {
    Key? key,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: DropdownButtonFormField<String>(
      key: key,
      initialValue: value,
      isExpanded: true,
      decoration: InputDecoration(
        labelText: label,
        border: const OutlineInputBorder(),
      ),
      items: items,
      onChanged: _saving ? null : changed,
      validator: (v) => v == null ? 'Required' : null,
    ),
  );

  @override
  Widget build(BuildContext context) => PopScope(
    canPop: !_saving,
    child: Scaffold(
      backgroundColor: const Color(0xFF14283F),
      appBar: AppBar(
        title: Text(
          _editing
              ? 'REVISE PLAN v${widget.existing!['planVersion']}'
              : 'CREATE NEW PLAN',
        ),
      ),
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 650),
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(20),
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(20),
                  child: _loading
                      ? const Center(child: CircularProgressIndicator())
                      : _loadError != null
                      ? Column(
                          children: [
                            Text(_loadError!),
                            TextButton(
                              onPressed: _loadTeams,
                              child: const Text('Retry'),
                            ),
                          ],
                        )
                      : Form(
                          key: _form,
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              if (_teams.isEmpty)
                                const Text(
                                  'No rescue teams available. Add resources before creating a plan.',
                                ),
                              _select(
                                'Team',
                                _teamId,
                                _teams
                                    .map(
                                      (t) => DropdownMenuItem<String>(
                                        value: t['id'],
                                        child: Text(
                                          '${t['name']} (${t['status']})',
                                          overflow: TextOverflow.ellipsis,
                                        ),
                                      ),
                                    )
                                    .toList(),
                                (v) => setState(() {
                                  _teamId = v;
                                  _vehicleId = null;
                                }),
                              ),
                              _select(
                                'Vehicle',
                                _vehicleId,
                                _vehicles
                                    .map(
                                      (v) => DropdownMenuItem<String>(
                                        value: v['id'],
                                        child: Text(
                                          '${v['plateNumber']} - capacity ${v['capacity']} (${v['status']})',
                                          overflow: TextOverflow.ellipsis,
                                        ),
                                      ),
                                    )
                                    .toList(),
                                (v) => setState(() => _vehicleId = v),
                                key: ValueKey(_teamId),
                              ),
                              _select(
                                'Required skill',
                                _skill,
                                coordinationSkills
                                    .map(
                                      (s) => DropdownMenuItem(
                                        value: s,
                                        child: Text(s),
                                      ),
                                    )
                                    .toList(),
                                (v) => setState(() => _skill = v),
                              ),
                              TextFormField(
                                initialValue: _capacity,
                                enabled: !_saving,
                                decoration: const InputDecoration(
                                  labelText: 'Required capacity',
                                ),
                                keyboardType: TextInputType.number,
                                onChanged: (v) => _capacity = v.trim(),
                                validator: (v) {
                                  final capacity = int.tryParse(v ?? '');
                                  if (capacity == null || capacity < 1) {
                                    return 'Capacity must be at least 1.';
                                  }
                                  final vehicles = _vehicles.where(
                                    (v) => v['id'] == _vehicleId,
                                  );
                                  if (vehicles.isEmpty) {
                                    return 'Select a vehicle.';
                                  }
                                  if (capacity >
                                      (vehicles.first['capacity'] as num)) {
                                    return 'Exceeds selected vehicle capacity.';
                                  }
                                  return null;
                                },
                              ),
                              const SizedBox(height: 16),
                              _referenceSelector(),
                              TextFormField(
                                initialValue: _notes,
                                enabled: !_saving,
                                maxLength: 500,
                                maxLines: 3,
                                decoration: const InputDecoration(
                                  labelText: 'Notes (optional)',
                                ),
                                onChanged: (v) => _notes = v,
                              ),
                              if (_error != null)
                                Text(
                                  _error!,
                                  style: const TextStyle(
                                    color: Color(0xFFB3261E),
                                  ),
                                ),
                              if (_saving) const LinearProgressIndicator(),
                              FilledButton(
                                onPressed:
                                    _saving ||
                                        _loading ||
                                        _teams.isEmpty ||
                                        (!_editing &&
                                            (_incidentsLoading ||
                                                _incidentsError != null ||
                                                _selectedIncident == null))
                                    ? null
                                    : _save,
                                child: Text(
                                  _editing
                                      ? 'Save revised plan'
                                      : 'Create assignment',
                                ),
                              ),
                            ],
                          ),
                        ),
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
