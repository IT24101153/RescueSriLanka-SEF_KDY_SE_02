import 'dart:convert';

import 'package:http/http.dart' as http;

import '../config/api_config.dart';
import '../models/auth_models.dart';
import '../models/coordination_requests.dart';
import '../models/incident_reference.dart';

class RescueCoordinationException implements Exception {
  const RescueCoordinationException(this.message);
  final String message;
  @override
  String toString() => message;
}

class RescueCoordinationService {
  Future<List<IncidentReference>> getActiveIncidents(
    AuthSession session,
  ) async {
    final data = await _getList('/api/incidents?activeOnly=true', session);
    try {
      return data
          .map(
            (item) => IncidentReference.fromJson(item as Map<String, dynamic>),
          )
          .where((incident) => incident.isActive)
          .toList();
    } on FormatException {
      throw const RescueCoordinationException('Unable to read incidents.');
    } on TypeError {
      throw const RescueCoordinationException('Unable to read incidents.');
    }
  }

  Future<IncidentReference> getIncidentById(
    AuthSession session,
    String id,
  ) async {
    final data = await _object('GET', '/api/incidents/${_id(id)}', session);
    try {
      return IncidentReference.fromJson(data);
    } on FormatException {
      throw const RescueCoordinationException('Unable to read the incident.');
    } on TypeError {
      throw const RescueCoordinationException('Unable to read the incident.');
    }
  }

  Future<List<dynamic>> getRescueTeams(AuthSession session) =>
      _getList('/api/rescueteams', session);
  Future<List<dynamic>> getAssignments(AuthSession session) =>
      _getList('/api/assignments', session);
  Future<List<dynamic>> getDispatches(AuthSession session) =>
      _getList('/api/dispatches', session);

  String _id(String id) => Uri.encodeComponent(id);
  Future<Map<String, dynamic>> createTeam(AuthSession s, TeamInput data) =>
      _object('POST', '/api/rescueteams', s, data.toJson(editing: false));
  Future<Map<String, dynamic>> updateTeam(
    AuthSession s,
    String id,
    TeamInput data,
  ) => _object(
    'PUT',
    '/api/rescueteams/${_id(id)}',
    s,
    data.toJson(editing: true),
  );
  Future<void> deleteTeam(AuthSession s, String id) async {
    await _request('DELETE', '/api/rescueteams/${_id(id)}', s);
  }

  Future<Map<String, dynamic>> addMember(
    AuthSession s,
    String teamId,
    MemberInput data,
  ) => _object(
    'POST',
    '/api/rescueteams/${_id(teamId)}/members',
    s,
    data.toJson(editing: false),
  );
  Future<Map<String, dynamic>> updateMember(
    AuthSession s,
    String teamId,
    String id,
    MemberInput data,
  ) => _object(
    'PUT',
    '/api/rescueteams/${_id(teamId)}/members/${_id(id)}',
    s,
    data.toJson(editing: true),
  );
  Future<void> deleteMember(AuthSession s, String teamId, String id) async {
    await _request(
      'DELETE',
      '/api/rescueteams/${_id(teamId)}/members/${_id(id)}',
      s,
    );
  }

  Future<Map<String, dynamic>> addVehicle(
    AuthSession s,
    String teamId,
    VehicleInput data,
  ) => _object(
    'POST',
    '/api/rescueteams/${_id(teamId)}/vehicles',
    s,
    data.toJson(editing: false),
  );
  Future<Map<String, dynamic>> updateVehicle(
    AuthSession s,
    String teamId,
    String id,
    VehicleInput data,
  ) => _object(
    'PUT',
    '/api/rescueteams/${_id(teamId)}/vehicles/${_id(id)}',
    s,
    data.toJson(editing: true),
  );
  Future<void> deleteVehicle(AuthSession s, String teamId, String id) async {
    await _request(
      'DELETE',
      '/api/rescueteams/${_id(teamId)}/vehicles/${_id(id)}',
      s,
    );
  }

  Future<Map<String, dynamic>> createAssignment(
    AuthSession s,
    AssignmentInput data,
  ) => _object('POST', '/api/assignments', s, data.toJson(editing: false));
  Future<Map<String, dynamic>> reviseAssignment(
    AuthSession s,
    String id,
    AssignmentInput data,
  ) => _object(
    'POST',
    '/api/assignments/${_id(id)}/revise',
    s,
    data.toJson(editing: true),
  );
  Future<Map<String, dynamic>> validateAssignment(AuthSession s, String id) =>
      _object('POST', '/api/assignments/${_id(id)}/validate', s);
  Future<Map<String, dynamic>> decideAssignment(
    AuthSession s,
    String id, {
    required String workflowId,
    required int planVersion,
    required HumanDecision decision,
    String? notes,
  }) => _object('POST', '/api/assignments/${_id(id)}/decision', s, {
    'workflowId': workflowId,
    'planVersion': planVersion,
    'decision': decision.name.toUpperCase(),
    'notes': notes,
  });
  Future<Map<String, dynamic>> transitionDispatch(
    AuthSession s,
    String id,
    DispatchTransition status, {
    String? notes,
  }) => _object('PATCH', '/api/dispatches/${_id(id)}/status', s, {
    'newStatus': '${status.name[0].toUpperCase()}${status.name.substring(1)}',
    'notes': notes,
  });

  Future<List<dynamic>> _getList(String path, AuthSession s) async {
    final data = await _request('GET', path, s);
    if (data is List<dynamic>) return data;
    throw const RescueCoordinationException(
      'Unable to read rescue coordination data.',
    );
  }

  Future<Map<String, dynamic>> _object(
    String method,
    String path,
    AuthSession s, [
    Map<String, dynamic>? body,
  ]) async {
    final data = await _request(method, path, s, body);
    if (data is Map<String, dynamic>) return data;
    throw const RescueCoordinationException(
      'Unable to read the server response. Refresh before trying again.',
    );
  }

  Future<dynamic> _request(
    String method,
    String path,
    AuthSession session, [
    Map<String, dynamic>? body,
  ]) async {
    final client = http.Client();
    try {
      final request = http.Request(
        method,
        Uri.parse('${ApiConfig.baseUrl}$path'),
      );
      request.headers.addAll({
        'Authorization': 'Bearer ${session.token}',
        'Accept': 'application/json',
        'Content-Type': 'application/json',
      });
      if (body != null) request.body = jsonEncode(body);
      final response = await http.Response.fromStream(
        await client.send(request),
      );
      if (response.statusCode < 200 || response.statusCode >= 300) {
        throw RescueCoordinationException(_error(response));
      }
      if (response.body.isEmpty) return null;
      try {
        return jsonDecode(response.body);
      } on FormatException {
        throw const RescueCoordinationException(
          'Unable to read the server response. Refresh before trying again.',
        );
      }
    } on http.ClientException {
      throw const RescueCoordinationException(
        'Cannot reach the RescueSriLanka server. Refresh before retrying a change.',
      );
    } finally {
      client.close();
    }
  }

  String _error(http.Response response) {
    final code = response.statusCode;
    if (code == 401) return 'Your session has expired or is unauthorized.';
    if (code == 403) return 'You are not authorized to perform this action.';
    // Only expected validation/conflict responses can provide a user-facing reason.
    // Never display server errors, headers, traces, or arbitrary response bodies.
    if (code == 400 || code == 409 || code == 404) {
      try {
        final data = jsonDecode(response.body);
        final reason = data is Map ? data['detail'] ?? data['error'] : data;
        final safe = _safeReason(reason);
        if (safe != null) return '$safe (HTTP $code)';
        if (data is Map && data['errors'] is Map) {
          return 'Some fields are invalid. Check the form values (HTTP $code).';
        }
      } on FormatException {
        final safe = _safeReason(response.body);
        if (safe != null) return '$safe (HTTP $code)';
      }
    }
    return 'Unable to complete the request (HTTP $code). Refresh before trying again.';
  }

  String? _safeReason(dynamic value) {
    if (value is! String || value.length > 500) return null;
    // Backend business messages contain no user-supplied notes or credentials.
    const reasons = {
      'Team not found.',
      'Team member not found.',
      'Vehicle not found.',
      'Assignment not found.',
      'Dispatch not found.',
      'Safety validation workflow not found.',
      'Safety validation is invalid or stale.',
      'Live deterministic safety validation failed; revalidation is required.',
      'Team or vehicle is no longer available.',
      'Assignment already has a dispatch.',
      'Assignment is not in an approvable state.',
      'Concurrent or duplicate dispatch commit detected; retry safely.',
      'Dispatch has not been approved by a coordinator yet.',
      'Assignment must reference exactly one incident or help request.',
      'Required capacity must be greater than zero.',
      'Rescue team not found.',
      'Rescue team must be available.',
      'Vehicle must belong to the selected rescue team.',
      'Vehicle must be available.',
      'Vehicle does not meet the required capacity.',
      'Vehicle is already committed to another active assignment or dispatch.',
      'Rescue team is already committed to another active assignment or dispatch.',
      'Assignment cannot be revised unless it is proposed, pending approval, or rejected.',
      'An assignment with an active dispatch cannot be revised.',
      'Cannot delete a team while it is on mission.',
      'Cannot delete a team with existing assignments. Reassign or remove its assignments first.',
      'Team members cannot be changed while the team has an active assignment or mission.',
      'Team members cannot be removed while the team has an active assignment or mission.',
      'Vehicles cannot be removed while the team has an active assignment or mission.',
      'An in-use vehicle cannot be changed.',
      'An in-use vehicle cannot be removed.',
      'Vehicles cannot be changed while the team has an active assignment or mission.',
    };
    if (reasons.contains(value)) return value;
    for (final skill in coordinationSkills) {
      if (value ==
          "Rescue team has no available member with required skill '$skill'.") {
        return value;
      }
    }
    return null;
  }
}
