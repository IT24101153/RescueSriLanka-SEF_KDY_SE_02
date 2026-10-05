using System.ComponentModel.DataAnnotations;

namespace RescueSriLanka.Api.Features.ComponentB.DTOs
{
    public class CreateHelpRequestMessageDto
    {
        [StringLength(1000)]
        public string? Message { get; set; }
        [MaxLength(10)]
        public List<string>? DoItems { get; set; }
        [MaxLength(10)]
        public List<string>? DontItems { get; set; }
        public bool IsCritical { get; set; }
    }

    public class HelpRequestMessageDto
    {
        public Guid Id { get; set; }
        public Guid HelpRequestId { get; set; }
        public string? Message { get; set; }
        public List<string> DoItems { get; set; } = [];
        public List<string> DontItems { get; set; } = [];
        public bool IsCritical { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
