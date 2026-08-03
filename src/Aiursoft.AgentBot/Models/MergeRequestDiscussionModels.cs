using System.Text.Json.Serialization;

namespace Aiursoft.AgentBot.Models;

public enum MergeRequestDiscussionAction
{
    ReplyOnly,
    DismissFinding,
    AskClarification,
    ImplementFeedback
}

public sealed class MergeRequestDiscussionResponse
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("addressed_note_ids")]
    public IReadOnlyCollection<long> AddressedNoteIds { get; set; } = [];

    [JsonPropertyName("response_markdown")]
    public string ResponseMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("implementation_brief")]
    public string? ImplementationBrief { get; set; }

    public MergeRequestDiscussionAction ParsedAction => (Action ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "reply_only" => MergeRequestDiscussionAction.ReplyOnly,
        "dismiss_finding" => MergeRequestDiscussionAction.DismissFinding,
        "ask_clarification" => MergeRequestDiscussionAction.AskClarification,
        "implement_feedback" => MergeRequestDiscussionAction.ImplementFeedback,
        _ => throw new InvalidOperationException($"Discussion worker returned an unsupported action: '{Action}'.")
    };
}

public sealed class MergeRequestDiscussionDecision(
    MergeRequestDiscussionAction action,
    string responseMarkdown,
    string? implementationBrief,
    long throughNoteId,
    string targetDiscussionId)
{
    public MergeRequestDiscussionAction Action { get; } = action;
    public string ResponseMarkdown { get; } = responseMarkdown;
    public string? ImplementationBrief { get; } = implementationBrief;
    public long ThroughNoteId { get; } = throughNoteId;
    public string TargetDiscussionId { get; } = targetDiscussionId;
}
