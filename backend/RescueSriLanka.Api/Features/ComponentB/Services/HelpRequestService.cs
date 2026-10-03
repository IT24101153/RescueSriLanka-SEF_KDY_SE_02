using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Services
{
    public interface IHelpRequestService
    {
        Task<HelpRequestResponseDto> CreateAsync(Guid citizenId, CreateHelpRequestDto dto);
        Task<HelpRequestResponseDto?> GetByIdAsync(Guid id);
        Task<List<HelpRequestResponseDto>> GetAllAsync();
        Task<List<HelpRequestResponseDto>> GetByCitizenAsync(Guid citizenId);
        Task<HelpRequestResponseDto?> UpdateAsync(Guid id, Guid citizenId, UpdateHelpRequestDto dto);
        Task<HelpRequestResponseDto?> UpdateStatusAsync(Guid id, Guid changedByUserId, UpdateHelpRequestStatusDto dto);
        Task<List<StatusHistoryDto>> GetHistoryAsync(Guid id);
        Task<HelpRequestResponseDto?> VerifyAsync(Guid id, Guid verifiedByUserId, VerifyHelpRequestDto dto);
    }

    public class HelpRequestService(AppDbContext db, IHelpRequestAnalysisQueue analysisQueue) : IHelpRequestService
    {
        private readonly AppDbContext _db = db;

        public async Task<HelpRequestResponseDto> CreateAsync(Guid citizenId, CreateHelpRequestDto dto)
        {
            ValidatePeopleCount(dto.EstimatedPeopleCount);
            await EnsureRelatedIncidentExistsAsync(dto.RelatedIncidentId);

            var entity = new HelpRequest
            {
                CitizenId = citizenId,
                Type = dto.Type,
                Description = dto.Description,
                EstimatedPeopleCount = dto.EstimatedPeopleCount,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                RelatedIncidentId = dto.RelatedIncidentId,
                ImageUrl = dto.ImageUrl,
                Status = HelpRequestStatus.Pending
            };

            // Business-specific operation #1: urgency scoring
            entity.UrgencyScore = await CalculateUrgencyScoreAsync(entity);

            _db.HelpRequests.Add(entity);
            await _db.SaveChangesAsync();

            // Triage in the background so the citizen never waits on Gemini —
            // by the time a manager opens this request, the Planner Agent's
            // severity/reasoning is usually already sitting there to review.
            analysisQueue.Enqueue(entity.Id);

            return ToDto(entity, await GetIdentityAsync(entity.CitizenId));
        }

        public async Task<HelpRequestResponseDto?> GetByIdAsync(Guid id)
        {
            var entity = await _db.HelpRequests.FindAsync(id);
            return entity is null ? null : ToDto(entity, await GetIdentityAsync(entity.CitizenId));
        }

        public async Task<List<HelpRequestResponseDto>> GetAllAsync()
        {
            var entities = await _db.HelpRequests
                .OrderByDescending(r => r.UrgencyScore)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            var citizenIds = entities.Select(r => r.CitizenId).Distinct().ToArray();
            var identities = await _db.Users
                .AsNoTracking()
                .Where(u => citizenIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.FullName, u.PhoneNumber, u.District));

            return [.. entities.Select(r =>
                ToDto(r, identities.TryGetValue(r.CitizenId, out var identity) ? identity : default))];
        }

        public async Task<List<HelpRequestResponseDto>> GetByCitizenAsync(Guid citizenId)
        {
            var entities = await _db.HelpRequests
                .Where(r => r.CitizenId == citizenId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var identity = await GetIdentityAsync(citizenId);

            return [.. entities.Select(r => ToDto(r, identity))];
        }

        // Who filed the request — the Help Request Manager and the Rescue
        // Coordinator both need this to actually contact the person, not just
        // a CitizenId. A missing user degrades to nulls rather than failing.
        private async Task<(string? FullName, string? PhoneNumber, string? District)> GetIdentityAsync(Guid citizenId)
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == citizenId)
                .Select(u => new { u.FullName, u.PhoneNumber, u.District })
                .FirstOrDefaultAsync();
            return user is null ? default : (user.FullName, user.PhoneNumber, user.District);
        }

        public async Task<HelpRequestResponseDto?> UpdateAsync(Guid id, Guid citizenId, UpdateHelpRequestDto dto)
        {
            var entity = await _db.HelpRequests.FindAsync(id);
            if (entity is null) return null;
            if (entity.CitizenId != citizenId)
                throw new UnauthorizedAccessException("Only the requester can edit this help request.");
            if (entity.Status != HelpRequestStatus.Pending ||
                entity.VerificationStatus != VerificationStatus.PendingVerification)
            {
                throw new InvalidOperationException(
                    "A request can only be edited while it is pending verification and assignment.");
            }

            await EnsureRelatedIncidentExistsAsync(dto.RelatedIncidentId);
            ValidatePeopleCount(dto.EstimatedPeopleCount);
            entity.Type = dto.Type;
            entity.EstimatedPeopleCount = dto.EstimatedPeopleCount;
            entity.Description = dto.Description;
            entity.Latitude = dto.Latitude;
            entity.Longitude = dto.Longitude;
            entity.RelatedIncidentId = dto.RelatedIncidentId;
            entity.ImageUrl = dto.ImageUrl;
            entity.UrgencyScore = await CalculateUrgencyScoreAsync(entity);
            entity.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return ToDto(entity, await GetIdentityAsync(entity.CitizenId));
        }

        public async Task<HelpRequestResponseDto?> UpdateStatusAsync(Guid id, Guid changedByUserId, UpdateHelpRequestStatusDto dto)
        {
            var entity = await _db.HelpRequests.FindAsync(id);
            if (entity is null) return null;

            _db.RequestStatusHistories.Add(HelpRequestStatusTransition.Apply(
                entity, dto.NewStatus, changedByUserId, dto.Notes));

            await _db.SaveChangesAsync();
            return ToDto(entity, await GetIdentityAsync(entity.CitizenId));
        }

        public async Task<HelpRequestResponseDto?> VerifyAsync(Guid id, Guid verifiedByUserId, VerifyHelpRequestDto dto)
        {
            var entity = await _db.HelpRequests.FindAsync(id);
            if (entity is null) return null;

            entity.VerificationStatus = dto.IsReal
                ? Models.VerificationStatus.Verified
                : Models.VerificationStatus.RejectedFake;
            entity.VerifiedByUserId = verifiedByUserId;
            entity.VerifiedAt = DateTime.UtcNow;
            entity.VerificationNotes = dto.Notes;
            entity.UpdatedAt = DateTime.UtcNow;

            // A request rejected as fake should not remain actionable —
            // ASSUMPTION: auto-cancel it. Confirm with team if a different
            // behaviour is wanted (e.g. leave status untouched).
            if (!dto.IsReal)
            {
                var oldStatus = entity.Status;
                entity.Status = HelpRequestStatus.Cancelled;
                if (oldStatus != HelpRequestStatus.Cancelled)
                {
                    _db.RequestStatusHistories.Add(new RequestStatusHistory
                    {
                        HelpRequestId = entity.Id,
                        OldStatus = oldStatus,
                        NewStatus = HelpRequestStatus.Cancelled,
                        ChangedByUserId = verifiedByUserId,
                        Notes = dto.Notes ?? "Report rejected during verification."
                    });
                }
            }

            await _db.SaveChangesAsync();
            return ToDto(entity, await GetIdentityAsync(entity.CitizenId));
        }

        public async Task<List<StatusHistoryDto>> GetHistoryAsync(Guid id)
        {
            var history = await _db.RequestStatusHistories
                .Where(h => h.HelpRequestId == id)
                .OrderBy(h => h.ChangedAt)
                .ToListAsync();

            return [.. history.Select(h => new StatusHistoryDto
            {
                OldStatus = h.OldStatus,
                NewStatus = h.NewStatus,
                Notes = h.Notes,
                ChangedAt = h.ChangedAt
            })];
        }

        // Business-specific operation: urgency scoring.
        // Simple, transparent, explainable rule-based formula — easy to justify in the viva.
        private static async Task<int> CalculateUrgencyScoreAsync(HelpRequest request)
        {
            int score = request.Type switch
            {
                HelpRequestType.Medical => 80,
                HelpRequestType.Rescue => 90,
                HelpRequestType.Shelter => 50,
                HelpRequestType.Water => 40,
                HelpRequestType.Food => 30,
                _ => 20
            };

            // Proximity bonus: if within 2km of an active high-severity incident, bump the score.
            // NOTE: this queries Student A's Incident/SafetyZone data — once that table exists,
            // replace this stub with a real geospatial distance check.
            bool nearActiveHighSeverityIncident = false; // placeholder until Incident table is available
            if (nearActiveHighSeverityIncident)
            {
                score += 20;
            }

            score = Math.Min(score, 100);
            return await Task.FromResult(score);
        }

        private async Task EnsureRelatedIncidentExistsAsync(Guid? incidentId)
        {
            if (incidentId is not null &&
                !await _db.Incidents.AnyAsync(incident => incident.Id == incidentId.Value))
            {
                throw new ArgumentException("The related incident does not exist.", nameof(incidentId));
            }
        }

        private static void ValidatePeopleCount(int? count)
        {
            if (count is null or < 1)
                throw new ArgumentException("Approximate number of people affected must be at least 1.", nameof(count));
        }

        private static HelpRequestResponseDto ToDto(
            HelpRequest entity, (string? FullName, string? PhoneNumber, string? District) identity = default) => new()
        {
            Id = entity.Id,
            CitizenId = entity.CitizenId,
            CitizenName = identity.FullName,
            CitizenPhoneNumber = identity.PhoneNumber,
            Type = entity.Type,
            Description = entity.Description,
            EstimatedPeopleCount = entity.EstimatedPeopleCount,
            Latitude = entity.Latitude,
            Longitude = entity.Longitude,
            UrgencyScore = entity.UrgencyScore,
            Status = entity.Status,
            VerificationStatus = entity.VerificationStatus,
            VerificationNotes = entity.VerificationNotes,
            ImageUrl = entity.ImageUrl,
            District = identity.District,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}
