using System.Text.Json.Serialization;

namespace Aiursoft.AgentBot.Models;

public enum IssuePlanningAction
{
    Respond,
    PublishPlan,
    ApprovalCandidate
}

public sealed class IssuePlannerResponse
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("approval_note_id")]
    public long? ApprovalNoteId { get; set; }

    [JsonPropertyName("plan_markdown")]
    public string? PlanMarkdown { get; set; }

    [JsonPropertyName("response_markdown")]
    public string ResponseMarkdown { get; set; } = string.Empty;

    public IssuePlanningAction ParsedAction => (Action ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "respond" => IssuePlanningAction.Respond,
        "publish_plan" => IssuePlanningAction.PublishPlan,
        "approve_current_plan" => IssuePlanningAction.ApprovalCandidate,
        _ => throw new InvalidOperationException($"Planner returned an unsupported action: '{Action}'.")
    };
}

public sealed record IssuePlanState(int Version, long NoteId, string Markdown);

public sealed record IssueDiscussionState(long ThroughNoteId);

public sealed record IssuePlanningOutcome(bool Approved, string? ApprovedPlan, string Message)
{
    public static IssuePlanningOutcome Waiting(string message) => new(false, null, message);
    public static IssuePlanningOutcome Ready(string plan) => new(true, plan, "Plan approved");
}
