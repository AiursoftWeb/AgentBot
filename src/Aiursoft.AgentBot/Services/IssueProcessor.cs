using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiursoft.AgentBot.Services;

/// <summary>
/// Encapsulates the business logic for processing GitLab issues.
/// </summary>
public class IssueProcessor(
    IVersionControlService versionControl,
    BotWorkflowEngine workflowEngine,
    IssuePlanningService issuePlanningService,
    HttpWrapper httpWrapper,
    IOptions<AgentBotOptions> options,
    ILogger<IssueProcessor> logger)
{
    private readonly AgentBotOptions _options = options.Value;
    public async Task<ProcessResult> ProcessAsync(Issue issue, Server server)
    {
        try
        {
            ValidateIssue(issue);
            logger.LogInformation("Analyzing Issue #{IssueId}: {Title} on {EndPoint} (Project ID: {ProjectId})...",
                issue.Iid, issue.Title, server.EndPoint, issue.ProjectId);

            // Fetch repository details as early as possible for better logging and context.
            var repository = await versionControl.GetRepository(
                server.EndPoint,
                issue.ProjectId.ToString(),
                string.Empty,
                server.Token);

            logger.LogInformation("Working on repository: {RepoName} ({RepoUrl})",
                repository.Name, repository.CloneUrl);

            if (await versionControl.HasOpenPullRequestForIssue(server.EndPoint, issue.ProjectId, issue.Iid, server.Token))
            {
                logger.LogInformation("Issue #{IssueId} already has an open PR/MR. Skipping to avoid duplicate work.", issue.Iid);
                return ProcessResult.Skipped("Issue already has an open PR/MR");
            }

            if (!await IsIssueOpenAsync(server, issue))
            {
                logger.LogInformation("Issue #{IssueId} is already closed. Skipping.", issue.Iid);
                return ProcessResult.Skipped("Issue is already closed");
            }

            var defaultBranch = repository.DefaultBranch ?? "master"; // Fallback to master if null

            var notes = await GetIssueNotesAsync(server, issue);
            var comments = BuildIssueComments(notes);
            string? approvedPlan = null;
            if (ShouldUsePlanningMode(issue, server))
            {
                var planningOutcome = await issuePlanningService.ProcessAsync(issue, server, repository);
                if (!planningOutcome.Approved)
                {
                    logger.LogInformation(
                        "Issue #{IssueId} remains in planning mode: {Message}", issue.Iid, planningOutcome.Message);
                    return ProcessResult.Skipped(planningOutcome.Message);
                }

                approvedPlan = planningOutcome.ApprovedPlan;
                logger.LogInformation(
                    "Issue #{IssueId} has an approved plan. Starting a separate implementation workflow.", issue.Iid);
            }

            if (server.Provider == "GitLab")
            {
                var ownNotes = notes.Where(note => !note.System &&
                    string.Equals(note.Author.Username, server.UserName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (ownNotes.Any(note => note.Body.Contains(ImplementationMarker(2), StringComparison.Ordinal)))
                {
                    logger.LogWarning("Issue #{IssueId} has exhausted its two automatic implementation attempts", issue.Iid);
                    return ProcessResult.Skipped("Automatic implementation limit reached; human intervention required");
                }
                var round = ownNotes.Any(note => note.Body.Contains(ImplementationMarker(1), StringComparison.Ordinal)) ? 2 : 1;
                // Persist before AI execution. Failures, no-op runs and process restarts must consume the attempt.
                await PostImplementationNoteAsync(server, issue,
                    $"Starting automatic implementation attempt {round}/2. Failed or unchanged runs count toward this limit. " +
                    "After two attempts, human intervention is required; a new scan does not reset the limit.\n\n" +
                    ImplementationMarker(round));
            }

            logger.LogInformation("Issue #{IssueId} needs attention. Bot will create a fork and a new MR.", issue.Iid);

            var context = new WorkflowContext
            {
                Server = server,
                ProjectId = issue.ProjectId.ToString(),
                SourceBranch = defaultBranch, // Use repository's default branch
                TargetBranch = defaultBranch,
                WorkspaceName = $"issue-{issue.Iid}",
                Prompt = BuildTaskDescription(issue, comments, approvedPlan),
                CommitMessage = $"Fix for issue #{issue.Iid}: {issue.Title}\n\nAutomatically generated by Agent Bot.",
                PushBranch = $"fix-issue-{issue.Iid}",
                HideGitFolder = true
            };

            await workflowEngine.ExecuteAsync(context, async ctx =>
            {
                await workflowEngine.EnsureRepositoryForkedAsync(server, ctx.Repository!);
                var pushPath = versionControl.GetPushPath(server, ctx.Repository!);
                try
                {
                    await workflowEngine.PushAndFinalizeAsync(ctx, pushPath);
                }
                catch (Exception ex) when (server.Provider == "GitLab")
                {
                    var repoName = ctx.Repository!.Name ?? throw new InvalidOperationException("Repository name is null");
                    logger.LogWarning(ex, "Push failed for issue #{IssueId}. Deleting broken fork {UserName}/{RepoName} so it will be re-forked on next cycle.",
                        issue.Iid, server.UserName, repoName);
                    try
                    {
                        var projectPath = Uri.EscapeDataString($"{server.UserName}/{repoName}");
                        var deleteUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectPath}";
                        await httpWrapper.SendHttpAndGetJson<object>(deleteUrl, HttpMethod.Delete, server.Token);
                        logger.LogInformation("Successfully deleted broken fork: {UserName}/{RepoName}", server.UserName, repoName);
                    }
                    catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                    {
                        logger.LogWarning("Received 405 Method Not Allowed. The project might be moved. Trying to delete by ID...");
                        try
                        {
                            var projectPath = Uri.EscapeDataString($"{server.UserName}/{repoName}");
                            var getUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectPath}";
                            var projectDetails = await httpWrapper.SendHttpAndGetJson<GitLabProjectDto>(getUrl, HttpMethod.Get, server.Token);
                            if (projectDetails.Id > 0)
                            {
                                var deleteByIdUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectDetails.Id}?permanently_remove=true&full_path={Uri.EscapeDataString(projectDetails.PathWithNamespace)}";
                                await httpWrapper.SendHttpAndGetJson<object>(deleteByIdUrl, HttpMethod.Delete, server.Token);
                                logger.LogInformation("Successfully deleted broken fork by ID: {ProjectId} with full path {FullPath}", projectDetails.Id, projectDetails.PathWithNamespace);
                            }
                        }
                        catch (Exception ex2)
                        {
                            logger.LogError(ex2, "Failed to delete broken fork by ID {UserName}/{RepoName}", server.UserName, repoName);
                        }
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogError(deleteEx, "Failed to delete broken fork {UserName}/{RepoName}", server.UserName, repoName);
                    }
                    throw;
                }

                if (await IsIssueOpenAsync(server, issue))
                {
                    await CreatePullRequestIfNeededAsync(server, ctx.Repository!, issue, ctx.PushBranch, ctx.AiOutput);
                }
                else
                {
                    logger.LogWarning("Issue #{IssueId} has been closed while processing. Skipping pull request creation.", issue.Iid);
                }
            });

            if (context.Result.Message == "Skipped: No changes made")
            {
                context.Result = ProcessResult.Failed("AI execution produced no repository changes; no merge request was created");
            }
            if (server.Provider == "GitLab" && !context.Result.Success)
            {
                await PostImplementationNoteAsync(server, issue,
                    "The automatic implementation attempt did not produce a merge request. " +
                    "The worker failed or made no repository changes. The attempt remains counted toward the two-attempt limit. " +
                    "A successful CLI exit alone does not mean that this issue was fixed.");
            }
            return context.Result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing issue #{IssueIid}", issue.Iid);
            return ProcessResult.Failed($"Error processing issue #{issue.Iid}", ex);
        }
    }

    private static string ImplementationMarker(int round) => $"<!-- agentbot:issue-implementation:v1:round-{round} -->";

    private async Task PostImplementationNoteAsync(Server server, Issue issue, string body)
    {
        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{issue.ProjectId}/issues/{issue.Iid}/notes";
        await httpWrapper.SendHttp(url, HttpMethod.Post, server.Token, new { body });
    }

    private async Task<List<GitLabNote>> GetIssueNotesAsync(Server server, Issue issue)
    {
        if (server.Provider != "GitLab") return [];
        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{issue.ProjectId}/issues/{issue.Iid}/notes?sort=asc&order_by=created_at";
        // Do not run AI when history is unavailable: an empty fallback would grant a fresh allowance.
        return await GitLabPagination.GetAllAsync<GitLabNote>(httpWrapper, url, server.Token);
    }

    private static string BuildIssueComments(IEnumerable<GitLabNote> notes)
    {
        var comments = notes.Where(note => !note.System)
            .Select(note => $"Comment by @{note.Author.Username} at {note.Created_at:yyyy-MM-dd HH:mm:ss}:\n{note.Body}")
            .ToList();
        return comments.Count > 0 ? "\n\n## Issue Comments\n" + string.Join("\n\n---\n\n", comments) : string.Empty;
    }

    private async Task<bool> IsIssueOpenAsync(Server server, Issue issue)
    {
        if (server.Provider != "GitLab")
        {
            // For other providers, we don't have a direct way to check yet via IVersionControlService.
            // We assume it's still open to avoid breaking existing functionality for other providers.
            return true;
        }

        try
        {
            var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{issue.ProjectId}/issues/{issue.Iid}";
            var issueDetails = await httpWrapper.SendHttpAndGetJson<GitLabIssueDto>(url, HttpMethod.Get, server.Token);
            return string.Equals(issueDetails.State, "opened", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check if issue #{IssueId} is still open. Assuming it is.", issue.Iid);
            return true;
        }
    }

    private void ValidateIssue(Issue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.Title))
            throw new ArgumentException($"Issue {issue.Id} has no title", nameof(issue));
        if (issue.ProjectId <= 0)
            throw new ArgumentException($"Issue {issue.Id} has invalid project ID", nameof(issue));
    }

    private bool ShouldUsePlanningMode(Issue issue, Server server)
    {
        if (!_options.PlanningModeEnabled || _options.Engine is not (AiEngine.Codex or AiEngine.Copilot) || server.Provider != "GitLab")
        {
            return false;
        }

        // PipelineProcessor creates this operational issue itself. Requiring the bot to approve its own
        // plan would deadlock unattended pipeline recovery.
        return !string.Equals(issue.Title, "主分支的编译管道失败", StringComparison.Ordinal);
    }

    private string BuildTaskDescription(Issue issue, string comments, string? approvedPlan)
    {
        var planSection = string.IsNullOrWhiteSpace(approvedPlan)
            ? string.Empty
            : $"""

               ## Locked Approved Plan
               {approvedPlan}

               Implement only this approved plan. Do not expand scope based on unapproved comments. If the plan cannot
               be implemented safely without a material scope change, stop and report the blocker instead of improvising.
               """;

        return $"Issue #{issue.Iid}: {issue.Title}\n\n{issue.Description ?? "No description provided."}{comments}{planSection}\n\n{ReplyLanguageText.PromptInstruction(_options.ReplyLanguage)}\n\nPlease analyze this issue and make the necessary code changes to resolve it.\n\nDon't forget to bump the version for updated nuget package projects after necessary changes, while do NOT add a version tag for projects doesn't publish nuget packages!{AiPromptHelper.GetEfMigrationGuidelines()}";
    }

    private async Task CreatePullRequestIfNeededAsync(Server server, Repository repository, Issue issue, string branchName, string aiOutput)
    {
        var ownerLogin = repository.Owner?.Login ?? throw new InvalidOperationException("Repository owner is null");
        var repoName = repository.Name ?? throw new InvalidOperationException("Repository name is null");
        var defaultBranch = repository.DefaultBranch ?? throw new InvalidOperationException("Repository default branch is null");

        var existingPullRequestsByBot = (await versionControl.GetPullRequests(
                server.EndPoint,
                ownerLogin,
                repoName,
                $"{server.UserName}:{branchName}",
                server.Token))
            .Where(p => string.Equals(p.User?.Login, server.UserName, StringComparison.OrdinalIgnoreCase));

        if (existingPullRequestsByBot.All(p => p.State != "open"))
        {
            logger.LogInformation("Creating pull request for issue #{IssueId}...", issue.Iid);

            var title = ReplyLanguageText.Select(
                _options.ReplyLanguage,
                $"Fix for issue #{issue.Iid}: {issue.Title}",
                $"修复 Issue #{issue.Iid}：{issue.Title}",
                $"Ispravak za prijavu #{issue.Iid}: {issue.Title}");
            var body = ReplyLanguageText.Select(
                _options.ReplyLanguage,
                $@"
Automatically generated by Agent Bot to fix issue #{issue.Iid}.

Closes #{issue.Iid}

## Issue
{issue.Title}

{issue.Description ?? "No description provided."}

## Changes
This pull request contains automated fixes generated by the Agent Bot.

## AI CLI Context
```
{aiOutput}
```

Please review carefully before merging.",
                $@"
由 Agent Bot 自动生成，用于修复 Issue #{issue.Iid}。

Closes #{issue.Iid}

## Issue
{issue.Title}

{issue.Description ?? "未提供描述。"}

## 修改内容
此 Merge Request 包含 Agent Bot 自动生成的修复。

## AI CLI 上下文
```
{aiOutput}
```

合并前请仔细检查。",
                $@"
Agent Bot automatski je izradio ovaj zahtjev za spajanje radi ispravka prijave #{issue.Iid}.

Closes #{issue.Iid}

## Prijava
{issue.Title}

{issue.Description ?? "Opis nije naveden."}

## Promjene
Zahtjev sadrži automatske ispravke koje je izradio Agent Bot.

## Kontekst AI CLI-ja
```
{aiOutput}
```

Pažljivo pregledajte promjene prije spajanja.");

            try
            {
                await versionControl.CreatePullRequest(
                    server.EndPoint,
                    ownerLogin,
                    repoName,
                    $"{server.UserName}:{branchName}",
                    defaultBranch,
                    title,
                    body,
                    server.Token);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                // If the fork is too old or detached, GitLab returns 422 Unprocessable Entity ("Source project is not a fork of the target project").
                // We directly delete the remote forked project here, so it can be re-forked and retried in the next run.
                logger.LogWarning(ex, "Failed to create pull request for issue #{IssueId}. The fork might be too old or detached. Deleting the remote fork...", issue.Iid);

                if (server.Provider == "GitLab")
                {
                    try
                    {
                        var projectPath = Uri.EscapeDataString($"{server.UserName}/{repoName}");
                        var deleteUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectPath}";
                        await httpWrapper.SendHttpAndGetJson<object>(deleteUrl, HttpMethod.Delete, server.Token);
                        logger.LogInformation("Successfully deleted the remote fork: {ProjectPath}", projectPath);
                    }
                    catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                    {
                        logger.LogWarning("Received 405 Method Not Allowed. The project might be moved. Trying to delete by ID...");
                        try
                        {
                            var projectPath = Uri.EscapeDataString($"{server.UserName}/{repoName}");
                            var getUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectPath}";
                            var projectDetails = await httpWrapper.SendHttpAndGetJson<GitLabProjectDto>(getUrl, HttpMethod.Get, server.Token);
                            if (projectDetails.Id > 0)
                            {
                                var deleteByIdUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectDetails.Id}?permanently_remove=true&full_path={Uri.EscapeDataString(projectDetails.PathWithNamespace)}";
                                await httpWrapper.SendHttpAndGetJson<object>(deleteByIdUrl, HttpMethod.Delete, server.Token);
                                logger.LogInformation("Successfully deleted the remote fork by ID: {ProjectId} with full path {FullPath}", projectDetails.Id, projectDetails.PathWithNamespace);
                            }
                        }
                        catch (Exception ex2)
                        {
                            logger.LogError(ex2, "Failed to delete the remote fork by ID after pull request creation failed.");
                        }
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogError(deleteEx, "Failed to delete the remote fork after pull request creation failed.");
                    }
                }

                throw;
            }

            if (server.Provider == "GitLab")
            {
                await AssignBotToGitLabMr(server, issue, branchName);
                await AssignReviewerToGitLabMr(server, issue.ProjectId, branchName, issue);
            }
        }
    }

    private async Task AssignBotToGitLabMr(Server server, Issue issue, string branchName)
    {
        try
        {
            var userUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/user";
            var user = await httpWrapper.SendHttpAndGetJson<GitLabUser>(userUrl, HttpMethod.Get, server.Token);

            var mrUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{issue.ProjectId}/merge_requests?state=opened&source_branch={branchName}";
            var mrs = await httpWrapper.SendHttpAndGetJson<List<GitLabMergeRequestDto>>(mrUrl, HttpMethod.Get, server.Token);
            var mr = mrs.FirstOrDefault();

            if (mr != null)
            {
                var updateUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{issue.ProjectId}/merge_requests/{mr.Iid}?assignee_id={user.Id}";
                await httpWrapper.SendHttpAndGetJson<object>(updateUrl, HttpMethod.Put, server.Token);
                logger.LogInformation("Assigned MR #{IID} to bot user {UserName}", mr.Iid, server.UserName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to assign MR to bot for issue #{IssueId}", issue.Iid);
        }
    }

    private async Task AssignReviewerToGitLabMr(Server server, int projectId, string branchName, Issue? issue = null)
    {
        var reviewerUsername = _options.Reviewer;
        if (string.IsNullOrWhiteSpace(reviewerUsername))
        {
            if (issue?.Author?.Login is { } login)
            {
                reviewerUsername = login;
            }
            else
            {
                return;
            }
        }

        try
        {
            // Look up reviewer user ID
            var usersUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/users?username={Uri.EscapeDataString(reviewerUsername)}";
            var users = await httpWrapper.SendHttpAndGetJson<List<GitLabUser>>(usersUrl, HttpMethod.Get, server.Token);
            var reviewer = users.FirstOrDefault();
            if (reviewer == null)
            {
                logger.LogWarning("Reviewer '{Reviewer}' not found on {EndPoint}", reviewerUsername, server.EndPoint);
                return;
            }

            // Find the MR created for this branch
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
            logger.LogError(ex, "Failed to assign reviewer @{Reviewer} to MR for project {ProjectId}", reviewerUsername, projectId);
        }
    }
}
