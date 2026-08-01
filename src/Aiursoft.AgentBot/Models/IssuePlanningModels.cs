using System.Text.Json.Serialization;

namespace Aiursoft.AgentBot.Models;

public enum IssuePlanningDecision
{
    ContinueDiscussion,
    ApprovalCandidate
}

public sealed class IssuePlannerResponse
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = string.Empty;

    [JsonPropertyName("approval_note_id")]
    public long? ApprovalNoteId { get; set; }

    [JsonPropertyName("plan_markdown")]
    public string PlanMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("response_markdown")]
    public string ResponseMarkdown { get; set; } = string.Empty;

    public IssuePlanningDecision ParsedDecision =>
        string.Equals(Decision, "approval_candidate", StringComparison.OrdinalIgnoreCase)
            ? IssuePlanningDecision.ApprovalCandidate
            : IssuePlanningDecision.ContinueDiscussion;
}

public sealed record IssuePlanState(int Version, long NoteId, string Markdown);

public sealed record IssuePlanningOutcome(bool Approved, string? ApprovedPlan, string Message)
{
    public static IssuePlanningOutcome Waiting(string message) => new(false, null, message);
    public static IssuePlanningOutcome Ready(string plan) => new(true, plan, "Plan approved");
}
