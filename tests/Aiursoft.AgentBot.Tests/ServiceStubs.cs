using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Services.Abstractions;
using Aiursoft.GitRunner.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiursoft.AgentBot.Tests;

internal sealed class StubVersionControlService : IVersionControlService
{
    public bool SupportsTokenRotation => false;
    public string GetName()
    {
        return null!;
    }

    public Func<string, string, string, string, Task<bool>>? RepoExistsHandler { get; set; }
    public Task<bool> RepoExists(string endPoint, string orgName, string repoName, string patToken)
    {
        return RepoExistsHandler?.Invoke(endPoint, orgName, repoName, patToken) ?? Task.FromResult(false);
    }

    public Func<string, string, string, string, Task<Repository>>? GetRepositoryHandler { get; set; }
    public Task<Repository> GetRepository(string endPoint, string org, string repo, string patToken)
    {
        return GetRepositoryHandler?.Invoke(endPoint, org, repo, patToken) ?? Task.FromResult(default(Repository)!);
    }

    public List<(string endPoint, string org, string repo, string patToken)> ForkRepoCalls { get; } = new();
    public Task ForkRepo(string endPoint, string org, string repo, string patToken)
    {
        ForkRepoCalls.Add((endPoint, org, repo, patToken));
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<Repository> GetMyStars(string endPoint, string userName, string patToken)
    {
        return default!;
    }

    public IAsyncEnumerable<Issue> GetAssignedIssues(string endPoint, string userName, string patToken)
    {
        return default!;
    }

    public Func<string, int, int, string, Task<bool>>? HasOpenPullRequestForIssueHandler { get; set; }
    public Task<bool> HasOpenPullRequestForIssue(string endPoint, int projectId, int issueId, string patToken)
    {
        return HasOpenPullRequestForIssueHandler?.Invoke(endPoint, projectId, issueId, patToken) ?? Task.FromResult(false);
    }

    public Func<string, string, string, string, string, Task<IEnumerable<PullRequest>>>? GetPullRequestsHandler { get; set; }
    public Task<IEnumerable<PullRequest>> GetPullRequests(string endPoint, string org, string repo, string head, string patToken)
    {
        return GetPullRequestsHandler?.Invoke(endPoint, org, repo, head, patToken) ?? Task.FromResult(default(IEnumerable<PullRequest>)!);
    }

    public List<(string endPoint, string org, string repo, string head, string baseBranch, string title, string body, string patToken)> CreatePullRequestCalls { get; } = new();
    public Task CreatePullRequest(string endPoint, string org, string repo, string head, string baseBranch, string title, string body, string patToken)
    {
        CreatePullRequestCalls.Add((endPoint, org, repo, head, baseBranch, title, body, patToken));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<MergeRequestSearchResult>> GetOpenMergeRequests(string endPoint, string userName, string patToken)
    {
        return Task.FromResult(default(IReadOnlyCollection<MergeRequestSearchResult>)!);
    }

    public Func<string, string, string, int, int, Task<DetailedMergeRequest>>? GetMergeRequestDetailsHandler { get; set; }
    public Task<DetailedMergeRequest> GetMergeRequestDetails(string endPoint, string userName, string patToken, int projectId, int mergeRequestId)
    {
        return GetMergeRequestDetailsHandler?.Invoke(endPoint, userName, patToken, projectId, mergeRequestId) ?? Task.FromResult(default(DetailedMergeRequest)!);
    }

    public Task MergeRequest(string endPoint, string patToken, int projectId, int mergeRequestId)
    {
        return Task.CompletedTask;
    }

    public Func<string, string, int, int, Task<IReadOnlyCollection<PipelineJob>>>? GetPipelineJobsHandler { get; set; }
    public Task<IReadOnlyCollection<PipelineJob>> GetPipelineJobs(string endPoint, string patToken, int projectId, int pipelineId)
    {
        return GetPipelineJobsHandler?.Invoke(endPoint, patToken, projectId, pipelineId) ?? Task.FromResult(default(IReadOnlyCollection<PipelineJob>)!);
    }

    public Func<string, string, int, int, Task<string>>? GetJobLogHandler { get; set; }
    public Task<string> GetJobLog(string endPoint, string patToken, int projectId, int jobId)
    {
        return GetJobLogHandler?.Invoke(endPoint, patToken, projectId, jobId) ?? Task.FromResult<string>(null!);
    }

    public Func<Server, Repository, string>? GetPushPathHandler { get; set; }
    public string GetPushPath(Server connectionConfiguration, Repository repo)
    {
        return GetPushPathHandler?.Invoke(connectionConfiguration, repo) ?? default!;
    }

    public Task<string> RotateToken(string endPoint, string oldToken)
    {
        return Task.FromResult<string>(null!);
    }

}

internal sealed class StubWorkspaceManager : IAiWorkspaceManager
{
    public Func<string, string, string, CloneMode, string, Task>? ResetRepoHandler { get; set; }
    public List<(string path, string branch, string url, CloneMode mode, string auth)> ResetRepoCalls { get; } = new();
    public Task ResetRepo(string path, string branch, string url, CloneMode mode, string auth)
    {
        ResetRepoCalls.Add((path, branch, url, mode, auth));
        return ResetRepoHandler?.Invoke(path, branch, url, mode, auth) ?? Task.CompletedTask;
    }

    public Func<string, string, string, Task<bool>>? CommitToBranchHandler { get; set; }
    public List<(string path, string message, string branch)> CommitToBranchCalls { get; } = new();
    public Task<bool> CommitToBranch(string path, string message, string branch)
    {
        CommitToBranchCalls.Add((path, message, branch));
        return CommitToBranchHandler?.Invoke(path, message, branch) ?? Task.FromResult(false);
    }

    public Func<string, Task<bool>>? PendingCommitHandler { get; set; }
    public Task<bool> PendingCommit(string path)
    {
        return PendingCommitHandler?.Invoke(path) ?? Task.FromResult(false);
    }

    public List<(string path, string branch, string url, bool force)> PushCalls { get; } = new();
    public Task Push(string path, string branch, string url, bool force)
    {
        PushCalls.Add((path, branch, url, force));
        return Task.CompletedTask;
    }

    public Task SetUserConfig(string path, string name, string email)
    {
        return Task.CompletedTask;
    }

}

internal sealed class StubCommandService : IAiCommandService
{
    public Func<string, string, string, TimeSpan, bool, IDictionary<string, string?>?, Task<(int exitCode, string output, string error)>>? RunCommandAsyncHandler { get; set; }
    public List<(string bin, string arg, string path, TimeSpan timeout, bool useShell, IDictionary<string, string?>? environmentVariables)> RunCommandAsyncCalls { get; } = new();
    public Task<(int exitCode, string output, string error)> RunCommandAsync(string bin, string arg, string path, TimeSpan timeout, bool useShell = false, IDictionary<string, string?>? environmentVariables = null)
    {
        RunCommandAsyncCalls.Add((bin, arg, path, timeout, useShell, environmentVariables));
        return RunCommandAsyncHandler?.Invoke(bin, arg, path, timeout, useShell, environmentVariables) ?? Task.FromResult(default((int exitCode, string output, string error)));
    }

}

internal sealed class StubAiCliService(IAiCommandService commands, IOptions<AgentBotOptions> options, ILogger<AiCliService> logger)
    : AiCliService(commands, options, logger)
{
    public Func<string, string, bool, Task<(bool Success, string Output, string Error)>>? InvokeAiCliAsyncHandler { get; set; }
    public List<(string workPath, string taskDescription, bool hideGitFolder)> InvokeAiCliAsyncCalls { get; } = new();
    public override Task<(bool Success, string Output, string Error)> InvokeAiCliAsync(string workPath, string taskDescription, bool hideGitFolder)
    {
        InvokeAiCliAsyncCalls.Add((workPath, taskDescription, hideGitFolder));
        return InvokeAiCliAsyncHandler?.Invoke(workPath, taskDescription, hideGitFolder) ?? Task.FromResult(default((bool Success, string Output, string Error)));
    }

    public Func<string, string, Task<(bool Success, string Output, string Error)>>? InvokePlanningCliAsyncHandler { get; set; }
    public List<(string workPath, string taskDescription)> InvokePlanningCliAsyncCalls { get; } = new();
    public override Task<(bool Success, string Output, string Error)> InvokePlanningCliAsync(string workPath, string taskDescription)
    {
        InvokePlanningCliAsyncCalls.Add((workPath, taskDescription));
        return InvokePlanningCliAsyncHandler?.Invoke(workPath, taskDescription) ?? Task.FromResult(default((bool Success, string Output, string Error)));
    }

}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public Func<HttpClient>? Create { get; set; }
    public HttpClient CreateClient(string name) => Create!();
}
