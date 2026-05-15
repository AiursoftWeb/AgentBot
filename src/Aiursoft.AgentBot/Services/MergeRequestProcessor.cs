using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Aiursoft.AgentBot.Services;

/// <summary>
/// Handles checking and fixing failed merge requests.
/// </summary>
public class MergeRequestProcessor(
    IVersionControlService versionControl,
    BotWorkflowEngine workflowEngine,
    HttpWrapper httpWrapper,
    IOptions<AgentBotOptions> options,
    ILogger<MergeRequestProcessor> logger)
{
    private readonly AgentBotOptions _options = options.Value;
    public async Task<ProcessResult> ProcessMergeRequestsAsync(Server server)
    {
        try
        {
            var mrsToProcess = await IdentifyMergeRequestsToProcessAsync(server);
            if (mrsToProcess.Count == 0)
            {
                logger.LogInformation("No merge requests need attention. All clear!");
                return ProcessResult.Succeeded("No MRs to fix");
            }

            logger.LogInformation("Found {Count} merge requests to fix", mrsToProcess.Count);

            foreach (var item in mrsToProcess)
            {
                await CheckAndFixMergeRequestAsync(item, server);
            }

            return ProcessResult.Succeeded($"Processed {mrsToProcess.Count} MRs");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing merge requests");
            return ProcessResult.Failed("Error processing merge requests", ex);
        }
    }

    private async Task<List<MRToProcess>> IdentifyMergeRequestsToProcessAsync(Server server)
    {
        IReadOnlyCollection<MergeRequestSearchResult> mergeRequests;
        var targetBranches = new Dictionary<int, string>();
        var gitLabMrs = new List<GitLabMergeRequestDto>();

        if (server.Provider == "GitLab")
        {
            logger.LogInformation("Checking merge requests assigned to {UserName} on {EndPoint}...", server.UserName, server.EndPoint);
            var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/merge_requests?scope=assigned_to_me&state=opened&per_page=100";
            gitLabMrs = await httpWrapper.SendHttpAndGetJson<List<GitLabMergeRequestDto>>(url, HttpMethod.Get, server.Token);
            foreach (var m in gitLabMrs) targetBranches[m.Iid] = m.TargetBranch;
            mergeRequests = gitLabMrs.Select(m => new MergeRequestSearchResult
            {
                IID = m.Iid,
                Title = m.Title,
                ProjectId = m.ProjectId,
                SourceProjectId = m.SourceProjectId,
                SourceBranch = m.SourceBranch
            }).ToList();
        }
        else
        {
            logger.LogInformation("Checking merge requests submitted by {UserName} on {EndPoint}...", server.UserName, server.EndPoint);
            mergeRequests = await versionControl.GetOpenMergeRequests(server.EndPoint, server.UserName, server.Token);
        }

        var mrsToProcess = new List<MRToProcess>();
        foreach (var mr in mergeRequests)
        {
            logger.LogInformation("Analyzing MR #{IID}: {Title} on {EndPoint} (Project ID: {ProjectId})...",
                mr.IID, mr.Title, server.EndPoint, mr.ProjectId);

            var repository = await versionControl.GetRepository(server.EndPoint, mr.ProjectId.ToString(), string.Empty, server.Token);
            logger.LogInformation("Working on repository: {RepoName} ({RepoUrl})",
                repository.Name, repository.CloneUrl);

            var details = await versionControl.GetMergeRequestDetails(server.EndPoint, server.UserName, server.Token, mr.ProjectId, mr.IID);

            var hasConflicts = details.HasConflicts;
            var (hasNewHumanReview, discussions, lastBotCommitTime) = await GetReviewDetailsAsync(server, mr);
            var pipelineFailed = details.Pipeline?.Status == "failed";

            if (hasConflicts || hasNewHumanReview || pipelineFailed)
            {
                var authorName = server.Provider == "GitLab" ? gitLabMrs.FirstOrDefault(m => m.Iid == mr.IID)?.Author.Username : null;
                var isOthersMr = server.Provider == "GitLab" && !string.Equals(authorName, server.UserName, StringComparison.OrdinalIgnoreCase);

                var reasons = new List<string>();
                if (hasConflicts) reasons.Add("Merge Conflicts");
                if (hasNewHumanReview) reasons.Add("New Human Review/Comments");
                if (pipelineFailed) reasons.Add("Pipeline Failed");

                logger.LogInformation("MR #{IID} needs attention due to: {Reasons}. Bot {WritePermission} write permissions to source branch.",
                    mr.IID,
                    string.Join(", ", reasons),
                    isOthersMr ? "DOES NOT have" : "has");

                // Get target branch: from dictionary if available, otherwise use from fetched repository
                string targetBranch;
                if (targetBranches.TryGetValue(mr.IID, out var branch))
                {
                    targetBranch = branch;
                }
                else
                {
                    targetBranch = repository.DefaultBranch ?? "master"; // Fallback to master if null
                }

                mrsToProcess.Add(new MRToProcess
                {
                    SearchResult = mr,
                    Details = details,
                    HasConflicts = hasConflicts,
                    HasNewHumanReview = hasNewHumanReview,
                    PipelineFailed = pipelineFailed,
                    TargetBranch = targetBranch,
                    AuthorName = authorName,
                    Discussions = discussions,
                    LastBotCommitTime = lastBotCommitTime
                });
            }
            else
            {
                logger.LogInformation("MR #{IID} is in good shape. Skipping.", mr.IID);
            }
        }
        return mrsToProcess;
    }

    private async Task<(bool HasNewHumanReview, string Discussions, DateTime LastBotCommitTime)> GetReviewDetailsAsync(Server server, MergeRequestSearchResult mr)
    {
        if (server.Provider != "GitLab") return (false, string.Empty, DateTime.MinValue);
        try
        {
            var commitsUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{mr.ProjectId}/merge_requests/{mr.IID}/commits";
            var commits = await httpWrapper.SendHttpAndGetJson<List<GitLabCommit>>(commitsUrl, HttpMethod.Get, server.Token);
            var lastBotCommitTime = commits.Where(c => c.Message.Contains("Agent Bot")).Select(c => c.Created_at).DefaultIfEmpty(DateTime.MinValue).Max();

            var discussionsUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{mr.ProjectId}/merge_requests/{mr.IID}/discussions";
            var discussions = await httpWrapper.SendHttpAndGetJson<List<GitLabDiscussion>>(discussionsUrl, HttpMethod.Get, server.Token);

            var sb = new StringBuilder();
            var hasNewHumanReview = false;

            foreach (var note in discussions.SelectMany(d => d.Notes).Where(n => !n.System).OrderBy(n => n.Created_at))
            {
                var isBot = string.Equals(note.Author.Username, server.UserName, StringComparison.OrdinalIgnoreCase);
                var isNew = note.Created_at > lastBotCommitTime;

                if (!isBot && isNew) hasNewHumanReview = true;

                var prefix = isNew ? "[NEW] " : "";
                sb.AppendLine($"{prefix}{note.Author.Username}: {note.Body} ({note.Created_at})");
            }

            return (hasNewHumanReview, sb.ToString(), lastBotCommitTime);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch review details for MR #{IID}", mr.IID);
            return (false, string.Empty, DateTime.MinValue);
        }
    }

    private async Task CheckAndFixMergeRequestAsync(MRToProcess item, Server server)
    {
        var mr = item.SearchResult;
        try
        {
            logger.LogInformation("Processing MR #{IID}: {Title}", mr.IID, mr.Title);
            var pipelineProjectId = mr.SourceProjectId > 0 ? mr.SourceProjectId : mr.ProjectId;
            var branchName = mr.SourceBranch ?? throw new InvalidOperationException($"MR #{mr.IID} has no source branch");
            var isOthersMr = server.Provider == "GitLab" && !string.Equals(item.AuthorName, server.UserName, StringComparison.OrdinalIgnoreCase);

            var (prompt, commitMessage) = await BuildActionDetailsAsync(item, server, pipelineProjectId);

            var context = new WorkflowContext
            {
                Server = server,
                ProjectId = pipelineProjectId.ToString(),
                SourceBranch = branchName,
                TargetBranch = item.TargetBranch,
                WorkspaceName = $"mr-{mr.IID}",
                Prompt = prompt,
                CommitMessage = commitMessage,
                PushBranch = isOthersMr ? $"fix-mr-{mr.IID}" : branchName,
                HideGitFolder = false,
                NeedResolveConflicts = item.HasConflicts
            };

            await workflowEngine.ExecuteAsync(context, async ctx =>
            {
                if (isOthersMr)
                {
                    await HandleOthersMrFinalizeAsync(ctx, mr, item.TargetBranch, ctx.AiOutput);
                }
                else
                {
                    var pushPath = versionControl.GetPushPath(server, ctx.Repository!);
                    await workflowEngine.PushAndFinalizeAsync(ctx, pushPath);
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fixing MR #{IID}", mr.IID);
        }
    }

    private async Task<(string Prompt, string CommitMessage)> BuildActionDetailsAsync(MRToProcess item, Server server, int pipelineProjectId)
    // ... (omitting intermediate methods if possible, but replace tool needs context)
    {
        var basePrompt = $@"You are working on an EXISTING Merge Request #{item.SearchResult.IID}: '{item.SearchResult.Title}'.
Source Branch: {item.SearchResult.SourceBranch}
Target Branch: {item.TargetBranch}

Recent discussions and feedback (marked [NEW] if since last bot commit):
{item.Discussions ?? "No discussions found."}
";

        if (item.HasConflicts)
            return (BuildConflictPrompt(basePrompt, item.SearchResult, item.TargetBranch), $"Resolve merge conflicts for MR #{item.SearchResult.IID} by merging {item.TargetBranch}\n\nAutomatically generated fix by Agent Bot.");

        if (item.HasNewHumanReview)
            return (BuildReviewPrompt(basePrompt), $"Address human review for MR #{item.SearchResult.IID}\n\nAutomatically generated fix by Agent Bot.");

        var logs = await GetFailureLogsAsync(server, pipelineProjectId, item.Details.Pipeline!.Id);
        return (BuildFailurePrompt(basePrompt, item.Details, logs), $"Fix pipeline failure for MR #{item.SearchResult.IID}\n\nAutomatically generated fix by Agent Bot.");
    }

    private string BuildConflictPrompt(string basePrompt, MergeRequestSearchResult mr, string targetBranch) =>
        $@"{basePrompt}
Status: CRITICAL - MERGE CONFLICTS DETECTED.
Target branch '{targetBranch}' has been merged into your current branch '{mr.SourceBranch}', and it resulted in conflicts.

Your task:
1. Identify all files with merge conflict markers (<<<<<<<, =======, >>>>>>>).
2. Resolve the conflicts by choosing the correct code or combining changes as appropriate.
3. Ensure the project still builds and all tests pass after resolution.
4. DO NOT make any unrelated changes. Focus ONLY on resolving the conflicts.
5. You MUST remove all conflict markers before finishing.

I have already triggered the merge for you, so you will see conflict markers in the affected files. Please fix them immediately.";

    private string BuildFailurePrompt(string basePrompt, DetailedMergeRequest details, string failureLogs) =>
        $@"{basePrompt}
Status: CI/CD PIPELINE FAILED.
Pipeline URL: {details.Pipeline?.WebUrl}

Failure Logs:
{failureLogs}

Please analyze the logs and the codebase to fix the failures.";

    private string BuildReviewPrompt(string basePrompt) =>
        $@"{basePrompt}
Status: NEW HUMAN REVIEW/COMMENTS.
A human has provided feedback on this MR. Please address the comments mentioned in the discussions above, especially those marked as [NEW].

Please analyze the feedback and make the necessary changes.";

    private async Task HandleOthersMrFinalizeAsync(WorkflowContext ctx, MergeRequestSearchResult oldMr, string targetBranch, string aiOutput)
    {
        logger.LogInformation("MR #{IID} - Creating bot fork and new MR...", oldMr.IID);
        var targetRepository = await versionControl.GetRepository(ctx.Server.EndPoint, oldMr.ProjectId.ToString(), string.Empty, ctx.Server.Token);
        await workflowEngine.EnsureRepositoryForkedAsync(ctx.Server, targetRepository);

        var botForkRepository = await versionControl.GetRepository(ctx.Server.EndPoint, oldMr.ProjectId.ToString(), ctx.Server.UserName, ctx.Server.Token);
        var pushPath = versionControl.GetPushPath(ctx.Server, botForkRepository);
        await workflowEngine.PushAndFinalizeAsync(ctx, pushPath);

        await CreateNewMergeRequestAsync(ctx.Server, targetRepository, oldMr, targetBranch, ctx.PushBranch, aiOutput);
    }

    private async Task CreateNewMergeRequestAsync(Server server, Repository targetRepository, MergeRequestSearchResult oldMr, string targetBranch, string botBranchName, string aiOutput)
    {
        var ownerLogin = targetRepository.Owner?.Login ?? throw new InvalidOperationException("Repository owner is null");
        var repoName = targetRepository.Name ?? throw new InvalidOperationException("Repository name is null");

        var title = $"[Bot Fix] {oldMr.Title} (Replacement for #{oldMr.IID})";
        var body = $@"
This merge request was automatically generated by Agent Bot to replace #{oldMr.IID}.

## Changes
This merge request contains automated fixes generated by the Agent Bot.

## AI CLI Context
```
{aiOutput}
```

Please review carefully before merging.";

        await versionControl.CreatePullRequest(server.EndPoint, ownerLogin, repoName, $"{server.UserName}:{botBranchName}", targetBranch, title, body, server.Token);

        if (server.Provider == "GitLab")
        {
            await ManageGitLabAssignmentsAsync(server, oldMr, botBranchName);
        }
    }

    private async Task ManageGitLabAssignmentsAsync(Server server, MergeRequestSearchResult oldMr, string botBranchName)
    {
        try
        {
            var userUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/user";
            var user = await httpWrapper.SendHttpAndGetJson<GitLabUser>(userUrl, HttpMethod.Get, server.Token);

            var updateOldMrUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{oldMr.ProjectId}/merge_requests/{oldMr.IID}?assignee_ids=";
            await httpWrapper.SendHttpAndGetJson<object>(updateOldMrUrl, HttpMethod.Put, server.Token);

            var mrUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{oldMr.ProjectId}/merge_requests?state=opened&source_branch={botBranchName}";
            var mrs = await httpWrapper.SendHttpAndGetJson<List<GitLabMergeRequestDto>>(mrUrl, HttpMethod.Get, server.Token);
            var newMr = mrs.FirstOrDefault();

            if (newMr != null)
            {
                var updateNewMrUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{oldMr.ProjectId}/merge_requests/{newMr.Iid}?assignee_ids={user.Id}";
                await httpWrapper.SendHttpAndGetJson<object>(updateNewMrUrl, HttpMethod.Put, server.Token);
            }

            await AssignReviewerToNewMrAsync(server, oldMr.ProjectId, botBranchName);
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to manage MR assignments in GitLab"); }
    }

    private async Task AssignReviewerToNewMrAsync(Server server, int projectId, string branchName)
    {
        var reviewerUsername = _options.Reviewer;
        if (string.IsNullOrWhiteSpace(reviewerUsername))
        {
            return;
        }

        try
        {
            var usersUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/users?username={Uri.EscapeDataString(reviewerUsername)}";
            var users = await httpWrapper.SendHttpAndGetJson<List<GitLabUser>>(usersUrl, HttpMethod.Get, server.Token);
            var reviewer = users.FirstOrDefault();
            if (reviewer == null)
            {
                logger.LogWarning("Reviewer '{Reviewer}' not found on {EndPoint}", reviewerUsername, server.EndPoint);
                return;
            }

            var mrUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectId}/merge_requests?state=opened&source_branch={branchName}";
            var mrs = await httpWrapper.SendHttpAndGetJson<List<GitLabMergeRequestDto>>(mrUrl, HttpMethod.Get, server.Token);
            var mr = mrs.FirstOrDefault();

            if (mr != null)
            {
                var updateUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectId}/merge_requests/{mr.Iid}?reviewer_ids[]={reviewer.Id}";
                await httpWrapper.SendHttpAndGetJson<object>(updateUrl, HttpMethod.Put, server.Token);
                logger.LogInformation("Assigned reviewer @{Reviewer} to MR #{IID}", reviewerUsername, mr.Iid);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to assign reviewer @{Reviewer} to MR", reviewerUsername);
        }
    }

    private async Task<string> GetFailureLogsAsync(Server server, int projectId, int pipelineId)
    {
        try
        {
            var jobs = await versionControl.GetPipelineJobs(server.EndPoint, server.Token, projectId, pipelineId);
            var failedJobs = jobs.Where(j => j.Status == "failed").ToList();
            var allLogs = new StringBuilder();
            foreach (var job in failedJobs)
            {
                var log = await versionControl.GetJobLog(server.EndPoint, server.Token, projectId, job.Id);
                if (!string.IsNullOrWhiteSpace(log))
                {
                    allLogs.AppendLine($"\n\n=== Job: {job.Name} (Stage: {job.Stage}) ===");
                    allLogs.AppendLine(log);
                    allLogs.AppendLine("=== End of Job Log ===\n");
                }
            }
            return allLogs.ToString();
        }
        catch (Exception ex) { logger.LogError(ex, "Error getting failure logs for pipeline {PipelineId}", pipelineId); return string.Empty; }
    }
}
