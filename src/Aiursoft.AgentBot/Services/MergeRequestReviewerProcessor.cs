using Aiursoft.AgentBot.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Http.Headers;

namespace Aiursoft.AgentBot.Services;

/// <summary>
/// Handles reviewing merge requests where the bot is assigned as a reviewer.
/// </summary>
public class MergeRequestReviewerProcessor(
    IVersionControlService versionControl,
    BotWorkflowEngine workflowEngine,
    HttpWrapper httpWrapper,
    IHttpClientFactory httpClientFactory,
    ILogger<MergeRequestReviewerProcessor> logger)
{
    public async Task<ProcessResult> ProcessReviewRequestsAsync(Server server)
    {
        if (server.Provider != "GitLab")
        {
            logger.LogInformation("Reviewing is currently only supported for GitLab. Skipping server {EndPoint}", server.EndPoint);
            return ProcessResult.Succeeded("Skipped non-GitLab server");
        }

        try
        {
            var mrsToReview = await IdentifyMergeRequestsToReviewAsync(server);
            if (mrsToReview.Count == 0)
            {
                logger.LogInformation("No merge requests need review. All clear!");
                return ProcessResult.Succeeded("No MRs to review");
            }

            logger.LogInformation("Found {Count} merge requests to review", mrsToReview.Count);

            foreach (var item in mrsToReview)
            {
                await ReviewMergeRequestAsync(item, server);
            }

            return ProcessResult.Succeeded($"Reviewed {mrsToReview.Count} MRs");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reviewing merge requests");
            return ProcessResult.Failed("Error reviewing merge requests", ex);
        }
    }

    private async Task<List<MRToProcess>> IdentifyMergeRequestsToReviewAsync(Server server)
    {
        logger.LogInformation("Checking merge requests where {UserName} is a reviewer on {EndPoint}...", server.UserName, server.EndPoint);

        // GitLab API to find MRs where I am a reviewer
        // Using scope=reviews_for_me which is the recommended way to get MRs where the authenticated user is a reviewer
        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/merge_requests?scope=reviews_for_me&state=opened&per_page=100";
        logger.LogInformation("Fetching MRs from URL: {Url}", url);
        var gitLabMrs = await httpWrapper.SendHttpAndGetJson<List<GitLabMergeRequestDto>>(url, HttpMethod.Get, server.Token);
        logger.LogInformation("Found {Count} MRs from API response", gitLabMrs.Count);

        var mrsToReview = new List<MRToProcess>();
        foreach (var mrDto in gitLabMrs)
        {
            logger.LogInformation("Analyzing MR #{IID} for review: {Title} on {EndPoint} (Project ID: {ProjectId})...",
                mrDto.Iid, mrDto.Title, server.EndPoint, mrDto.ProjectId);

            var mrSearchResult = new MergeRequestSearchResult
            {
                IID = mrDto.Iid,
                Title = mrDto.Title,
                ProjectId = mrDto.ProjectId,
                SourceProjectId = mrDto.SourceProjectId,
                SourceBranch = mrDto.SourceBranch
            };

            var (needsReview, discussions, lastBotReviewTime) = await CheckIfNeedsReviewAsync(server, mrSearchResult);

            if (needsReview)
            {
                logger.LogInformation("MR #{IID} needs review.", mrDto.Iid);
                var details = await versionControl.GetMergeRequestDetails(server.EndPoint, server.UserName, server.Token, mrDto.ProjectId, mrDto.Iid);

                mrsToReview.Add(new MRToProcess
                {
                    SearchResult = mrSearchResult,
                    Details = details,
                    TargetBranch = mrDto.TargetBranch,
                    AuthorName = mrDto.Author.Username,
                    Discussions = discussions,
                    LastBotCommitTime = lastBotReviewTime // We repurpose this field to store last bot review time
                });
            }
            else
            {
                logger.LogInformation("MR #{IID} does not need review. Skipping.", mrDto.Iid);
            }
        }
        return mrsToReview;
    }

    private async Task<(bool NeedsReview, string Discussions, DateTime LastBotReviewTime)> CheckIfNeedsReviewAsync(Server server, MergeRequestSearchResult mr)
    {
        try
        {
            // Get last commit time
            var commitsUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{mr.ProjectId}/merge_requests/{mr.IID}/commits";
            var commits = await httpWrapper.SendHttpAndGetJson<List<GitLabCommit>>(commitsUrl, HttpMethod.Get, server.Token);
            var lastCommitTime = commits.Select(c => c.Created_at).DefaultIfEmpty(DateTime.MinValue).Max();

            // Get discussions to find last bot review
            var discussionsUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{mr.ProjectId}/merge_requests/{mr.IID}/discussions";
            var discussions = await httpWrapper.SendHttpAndGetJson<List<GitLabDiscussion>>(discussionsUrl, HttpMethod.Get, server.Token);

            var sb = new StringBuilder();
            var lastBotReviewTime = DateTime.MinValue;

            foreach (var note in discussions.SelectMany(d => d.Notes).Where(n => !n.System).OrderBy(n => n.Created_at))
            {
                var isBot = string.Equals(note.Author.Username, server.UserName, StringComparison.OrdinalIgnoreCase);
                if (isBot)
                {
                    lastBotReviewTime = note.Created_at;
                }

                sb.AppendLine($"{note.Author.Username}: {note.Body} ({note.Created_at})");
            }

            var needsReview = lastCommitTime > lastBotReviewTime;
            return (needsReview, sb.ToString(), lastBotReviewTime);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch review details for MR #{IID}", mr.IID);
            return (false, string.Empty, DateTime.MinValue);
        }
    }

    private async Task ReviewMergeRequestAsync(MRToProcess item, Server server)
    {
        var mr = item.SearchResult;
        try
        {
            logger.LogInformation("Reviewing MR #{IID}: {Title}", mr.IID, mr.Title);
            var pipelineProjectId = mr.SourceProjectId > 0 ? mr.SourceProjectId : mr.ProjectId;
            var branchName = mr.SourceBranch ?? throw new InvalidOperationException($"MR #{mr.IID} has no source branch");

            var prompt = BuildReviewPrompt(item);

            var context = new WorkflowContext
            {
                Server = server,
                ProjectId = pipelineProjectId.ToString(),
                SourceBranch = branchName,
                TargetBranch = item.TargetBranch,
                WorkspaceName = $"review-{mr.IID}",
                Prompt = prompt,
                CommitMessage = "N/A", // We are not committing
                PushBranch = branchName,
                HideGitFolder = false,
                NeedResolveConflicts = false,
                SkipCommit = true
            };

            // We use the engine to clone and run AI, but we override the finalization
            await workflowEngine.ExecuteAsync(context, async ctx =>
            {
                var reviewFilePath = Path.Combine(ctx.WorkspacePath, "review.md");
                if (File.Exists(reviewFilePath))
                {
                    var reviewContent = await File.ReadAllTextAsync(reviewFilePath);
                    if (!string.IsNullOrWhiteSpace(reviewContent))
                    {
                        await PostReviewCommentAsync(server, mr.ProjectId, mr.IID, reviewContent);
                    }
                    else
                    {
                        logger.LogWarning("AI generated an empty review.md for MR #{IID}", mr.IID);
                    }
                }
                else
                {
                    logger.LogWarning("AI did not generate a review.md for MR #{IID}", mr.IID);
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reviewing MR #{IID}", mr.IID);
        }
    }

    private string BuildReviewPrompt(MRToProcess item)
    {
        return $@"You are a code reviewer for Merge Request #{item.SearchResult.IID}: '{item.SearchResult.Title}'.
Source Branch: {item.SearchResult.SourceBranch}
Target Branch: {item.TargetBranch}

Recent discussions:
{item.Discussions ?? "No discussions found."}

Your task:
1. Analyze the changes in the current codebase compared to the target branch.
2. Provide a constructive code review.
3. Your review MUST be written in a file named 'review.md' in the root of the project.
4. Focus on code quality, security, performance, and potential bugs.
5. If everything looks good, you can simply say so in 'review.md'.
6. DO NOT modify any other files in the repository.

Please write your review into 'review.md' now.";
    }

    private async Task PostReviewCommentAsync(Server server, int projectId, int mrIid, string content)
    {
        logger.LogInformation("Posting review comment to MR #{IID}...", mrIid);
        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectId}/merge_requests/{mrIid}/notes";

        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);
        var response = await client.PostAsJsonAsync(url, new { body = content });
        response.EnsureSuccessStatusCode();
    }
}
