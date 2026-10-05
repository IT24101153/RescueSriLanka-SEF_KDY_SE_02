using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Services.Email;

namespace RescueSriLanka.Api.Features.ComponentB.Services
{
    public interface IHelpRequestMessageService
    {
        // Null when the request does not exist. Throws ArgumentException for an empty or oversized message.
        Task<HelpRequestMessageDto?> AddAsync(Guid helpRequestId, Guid authorUserId, CreateHelpRequestMessageDto dto, CancellationToken ct = default);
        Task<List<HelpRequestMessageDto>> GetAsync(Guid helpRequestId, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid helpRequestId, Guid messageId, CancellationToken ct = default);
    }

    public class HelpRequestMessageService(AppDbContext db, IActionEmailService emails) : IHelpRequestMessageService
    {
        public const int MaxItemLength = 200;

        public async Task<HelpRequestMessageDto?> AddAsync(
            Guid helpRequestId, Guid authorUserId, CreateHelpRequestMessageDto dto, CancellationToken ct = default)
        {
            var message = string.IsNullOrWhiteSpace(dto.Message) ? null : dto.Message.Trim();
            var doItems = Clean(dto.DoItems);
            var dontItems = Clean(dto.DontItems);

            if (message is null && doItems.Count == 0 && dontItems.Count == 0)
                throw new ArgumentException("Write a note or add at least one thing to do or not to do.");

            if (!await db.HelpRequests.AnyAsync(r => r.Id == helpRequestId, ct)) return null;

            var entity = new HelpRequestMessage
            {
                HelpRequestId = helpRequestId,
                AuthorUserId = authorUserId,
                Message = message,
                DoItems = doItems,
                DontItems = dontItems,
                IsCritical = dto.IsCritical
            };
            db.HelpRequestMessages.Add(entity);
            await db.SaveChangesAsync(ct);

            await emails.HelpRequestGuidanceAsync(helpRequestId, entity.IsCritical, ct);
            return ToDto(entity);
        }

        // Newest first, with urgent ones above the rest so they are seen before older advice.
        public async Task<List<HelpRequestMessageDto>> GetAsync(Guid helpRequestId, CancellationToken ct = default)
        {
            var messages = await db.HelpRequestMessages.AsNoTracking()
                .Where(m => m.HelpRequestId == helpRequestId)
                .ToListAsync(ct);

            return [.. messages
                .OrderByDescending(m => m.IsCritical)
                .ThenByDescending(m => m.CreatedAt)
                .Select(ToDto)];
        }

        public async Task<bool> DeleteAsync(Guid helpRequestId, Guid messageId, CancellationToken ct = default)
        {
            var entity = await db.HelpRequestMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.HelpRequestId == helpRequestId, ct);
            if (entity is null) return false;

            db.HelpRequestMessages.Remove(entity);
            await db.SaveChangesAsync(ct);
            return true;
        }

        // Blank lines are dropped and the rest trimmed, so a list typed one per line can be sent as is.
        private static List<string> Clean(List<string>? items)
        {
            var cleaned = (items ?? [])
                .Select(item => item?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .ToList();

            if (cleaned.Any(item => item.Length > MaxItemLength))
                throw new ArgumentException($"Each item must be {MaxItemLength} characters or fewer.");

            return cleaned;
        }

        private static HelpRequestMessageDto ToDto(HelpRequestMessage m) => new()
        {
            Id = m.Id,
            HelpRequestId = m.HelpRequestId,
            Message = m.Message,
            DoItems = m.DoItems,
            DontItems = m.DontItems,
            IsCritical = m.IsCritical,
            CreatedAt = m.CreatedAt
        };
    }
}
