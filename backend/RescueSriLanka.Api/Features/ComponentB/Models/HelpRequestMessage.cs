namespace RescueSriLanka.Api.Features.ComponentB.Models
{
    // A message from the response team to the citizen who filed a help request:
    // a note, and/or safety guidance as lists of things to do and not to do.
    // Messages are only ever added or removed, never edited, so what the citizen
    // was told at a given time stays on record.
    public class HelpRequestMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid HelpRequestId { get; set; }
        public Guid AuthorUserId { get; set; }
        public string? Message { get; set; }
        public List<string> DoItems { get; set; } = [];
        public List<string> DontItems { get; set; } = [];
        // Urgent advice, such as "evacuate now", is shown first and pushed as important.
        public bool IsCritical { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
