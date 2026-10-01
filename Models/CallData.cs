namespace SAFETY_STEPS.Models;

/// <summary>
/// Represents a call document stored in the Realtime DB at: calls/{channelName}
/// Plain C# class — no Firebase SDK attributes needed (we use REST).
/// </summary>
public class CallData
{
    public string CallId { get; set; } = "";
    public string CallerId { get; set; } = "";
    public string CallerName { get; set; } = "";
    public string StudentNumber { get; set; } = "";    // ← NEW: caller's student number
    public string ReceiverId { get; set; } = "";
    public string ReceiverUid { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public bool IsUrgent { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Values: "calling" | "accepted" | "rejected" | "ended" | "missed"</summary>
    public string Status { get; set; } = "calling";

    /// <summary>
    /// Base64-encoded profile image of the caller.
    /// Populated when the call document is read so the admin can display
    /// the caller's avatar without a separate Firestore lookup.
    /// Empty string when not available.
    /// </summary>
    public string ProfileImageBase64 { get; set; } = "";
}