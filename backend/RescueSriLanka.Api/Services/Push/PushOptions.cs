namespace RescueSriLanka.Api.Services.Push;

/// <summary>
/// The "Push" configuration section. Pushes go out through Firebase Cloud
/// Messaging. Until a service account is configured they are logged instead, so
/// the feature runs for anyone who clones the repository.
/// </summary>
public class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Master switch for every push notification.</summary>
    public bool Enabled { get; set; } = true;

    public FcmOptions Fcm { get; set; } = new();

    public class FcmOptions
    {
        /// <summary>The Firebase project id, as shown in the Firebase console's project settings.</summary>
        public string? ProjectId { get; set; }

        /// <summary>
        /// Path to the service-account JSON downloaded from Firebase. It is a
        /// credential, so keep it outside the repository.
        /// </summary>
        public string? CredentialsFile { get; set; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(CredentialsFile);
    }
}
