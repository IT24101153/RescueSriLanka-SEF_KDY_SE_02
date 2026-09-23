class TeamInput {
  const TeamInput({
    required this.name,
    this.status,
    this.baseLatitude,
    this.baseLongitude,
  });
  final String name;
  final String? status;
  final double? baseLatitude;
  final double? baseLongitude;
  Map<String, dynamic> toJson({required bool editing}) => {
    'name': name,
    'baseLatitude': baseLatitude,
    'baseLongitude': baseLongitude,
    if (editing) 'status': status,
  };
}

class MemberInput {
  const MemberInput({
    required this.fullName,
    required this.phone,
    required this.skill,
    this.isAvailable = true,
  });
  final String fullName;
  final String phone;
  final String skill;
  final bool isAvailable;
  Map<String, dynamic> toJson({required bool editing}) => {
    'fullName': fullName,
    'phone': phone,
    'skill': skill,
    if (editing) 'isAvailable': isAvailable,
  };
}

class VehicleInput {
  const VehicleInput({
    required this.plateNumber,
    required this.type,
    required this.capacity,
    this.status,
  });
  final String plateNumber;
  final String type;
  final int capacity;
  final String? status;
  Map<String, dynamic> toJson({required bool editing}) => {
    'plateNumber': plateNumber,
    'type': type,
    'capacity': capacity,
    if (editing) 'status': status,
  };
}

class AssignmentInput {
  const AssignmentInput({
    required this.rescueTeamId,
    required this.vehicleId,
    required this.requiredSkill,
    required this.requiredCapacity,
    this.incidentId,
    this.helpRequestId,
    this.notes,
  });
  final String rescueTeamId;
  final String vehicleId;
  final String requiredSkill;
  final int requiredCapacity;
  final String? incidentId;
  final String? helpRequestId;
  final String? notes;
  Map<String, dynamic> toJson({required bool editing}) => {
    'rescueTeamId': rescueTeamId,
    'vehicleId': vehicleId,
    'requiredSkill': requiredSkill,
    'requiredCapacity': requiredCapacity,
    'notes': notes,
    if (!editing) 'incidentId': incidentId,
    if (!editing) 'helpRequestId': helpRequestId,
  };
}

enum HumanDecision { approve, revise, reject }

enum DispatchTransition { dispatched, enRoute, onScene, resolved, cancelled }

const coordinationSkills = [
  'WaterRescue',
  'FirstAid',
  'Paramedic',
  'StructuralCollapse',
  'FireResponse',
  'Logistics',
  'Driving',
];
const teamStatuses = ['Available', 'OnMission', 'OffDuty'];
const vehicleTypes = ['Ambulance', 'Boat', 'FireTruck', 'FourByFour', 'Truck'];
const vehicleStatuses = ['Available', 'InUse', 'UnderMaintenance'];
