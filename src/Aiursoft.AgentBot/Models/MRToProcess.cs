using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;

namespace Aiursoft.AgentBot.Models;

public class MRToProcess
{
    public required MergeRequestSearchResult SearchResult { get; init; }
    public required DetailedMergeRequest Details { get; init; }
    public bool HasConflicts { get; init; }
    public bool HasNewHumanReview { get; init; }
    public bool PipelineFailed { get; init; }
    public string TargetBranch { get; init; } = "main";
    public string TargetRepositoryCloneUrl { get; init; } = string.Empty;
    public string? AuthorName { get; init; }
    public string? Discussions { get; init; }
    public MergeRequestDiscussionDecision? DiscussionDecision { get; init; }
    public string ReviewCommitSha { get; init; } = string.Empty;
}
