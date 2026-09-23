namespace RescueSriLanka.Api.Models;

/// <summary>Roles recognised by the platform. Stored in the database as text.</summary>
public enum UserRole
{
    /// <summary>Citizen or tourist — Flutter app only.</summary>
    Citizen,

    /// <summary>Manages incidents and approves AI-proposed response plans.</summary>
    EmergencyCoordinator,

    /// <summary>Manages shelters, medical supplies, food/water stock and vehicles.</summary>
    ResourceManager,

    /// <summary>Receives assignments and reports status from the field.</summary>
    RescueTeam,

    /// <summary>Verifies and triages citizen help requests (Help request dashboard).</summary>
    HelpRequestManager
}
