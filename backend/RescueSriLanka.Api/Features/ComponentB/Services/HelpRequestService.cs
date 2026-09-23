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
        Task<HelpRequestResponseDto?> UpdateStatusAsync(Guid id, Guid changedByUserId, UpdateHelpRequestStatusDto dto);
        Task<List<StatusHistoryDto>> GetHistoryAsync(Guid id);
        Task<HelpRequestResponseDto?> VerifyAsync(Guid id, Guid verifiedByUserId, VerifyHelpRequestDto dto);
    }

    public class HelpRequestService : IHelpRequestService
    {
        private readonly AppDbContext _db;

        public HelpRequestService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<HelpRequestResponseDto> CreateAsync(Guid citizenId, CreateHelpRequestDto dto)
        {
            var entity = new HelpRequest
            {
                CitizenId = citizenId,
                Type = dto.Type,
                Description = dto.Description,
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

            return ToDto(entity);
        }

        public async Task<HelpRequestResponseDto?> GetByIdAsync(Guid id)
        {
            var entity = await _db.HelpRequests.FindAsync(id);
            return entity is null ? null : ToDto(entity);
        }

        public async Task<List<HelpRequestResponseDto>> GetAllAsync()
        {
            var entities = await _db.HelpRequests
                .OrderByDescending(r => r.UrgencyScore)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            return entities.Select(ToDto).ToList();
        }

        public async Task<List<HelpRequestResponseDto>> GetByCitizenAsync(Guid citizenId)
        {
            var entities = await _db.HelpRequests
                .Where(r => r.CitizenId == citizenId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return entities.Select(ToDto).ToList();
        }

        public async Task<HelpRequestResponseDto?> UpdateStatusAsync(Guid id, Guid changedByUserId, UpdateHelpRequestStatusDto dto)
        {
            var entity = await _db.HelpRequests.FindAsync(id);
            if (entity is null) return null;

            var oldStatus = entity.Status;
            entity.Status = dto.NewStatus;
            entity.UpdatedAt = DateTime.UtcNow;

            // Business rule: every status change is recorded in history
            _db.RequestStatusHistories.Add(new RequestStatusHistory
            {
                HelpRequestId = entity.Id,
                OldStatus = oldStatus,
                NewStatus = dto.NewStatus,
                ChangedByUserId = changedByUserId,
                Notes = dto.Notes
            });

            await _db.SaveChangesAsync();
            return ToDto(entity);
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
                entity.Status = HelpRequestStatus.Cancelled;
            }

            await _db.SaveChangesAsync();
            return ToDto(entity);
        }

        public async Task<List<StatusHistoryDto>> GetHistoryAsync(Guid id)
        {
            var history = await _db.RequestStatusHistories
                .Where(h => h.HelpRequestId == id)
                .OrderBy(h => h.ChangedAt)
                .ToListAsync();

            return history.Select(h => new StatusHistoryDto
            {
                OldStatus = h.OldStatus,
                NewStatus = h.NewStatus,
                Notes = h.Notes,
                ChangedAt = h.ChangedAt
            }).ToList();
        }

        // Business-specific operation: urgency scoring.
        // Simple, transparent, explainable rule-based formula — easy to justify in the viva.
        private async Task<int> CalculateUrgencyScoreAsync(HelpRequest request)
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

        private static HelpRequestResponseDto ToDto(HelpRequest entity) => new()
        {
            Id = entity.Id,
            CitizenId = entity.CitizenId,
            Type = entity.Type,
            Description = entity.Description,
            Latitude = entity.Latitude,
            Longitude = entity.Longitude,
            UrgencyScore = entity.UrgencyScore,
            Status = entity.Status,
            VerificationStatus = entity.VerificationStatus,
            VerificationNotes = entity.VerificationNotes,
            ImageUrl = entity.ImageUrl,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}