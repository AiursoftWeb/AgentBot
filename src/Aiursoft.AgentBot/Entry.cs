using Aiursoft.AgentBot.Services;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiursoft.AgentBot;

/// <summary>
/// Application entry point and coordinator.
/// Orchestrates the high-level workflow without containing business logic.
/// </summary>
public class Entry(
    IOptions<List<Server>> servers,
    IEnumerable<IVersionControlService> versionControls,
    IssueProcessor issueProcessor,
    MergeRequestProcessor mergeRequestProcessor,
    MergeRequestReviewerProcessor mergeRequestReviewerProcessor,
    PipelineProcessor pipelineProcessor,
    ILogger<Entry> logger)
{
    private readonly List<Server> _servers = servers.Value;

    public async Task RunAsync()
    {
        logger.LogInformation("Starting Agent Bot for issue processing...");

        foreach (var server in _servers)
        {
            logger.LogInformation("Processing server: {ServerProvider}...", server.Provider);

            var serviceProvider = GetVersionControlService(server.Provider);
            await ProcessServerAsync(server, serviceProvider);
        }
    }

    private IVersionControlService GetVersionControlService(string providerName)
    {
        var service = versionControls.FirstOrDefault(v => v.GetName() == providerName);
        if (service == null)
        {
            throw new InvalidOperationException($"No version control service found for provider: {providerName}");
        }
        return service;
    }

    private async Task ProcessServerAsync(Server server, IVersionControlService versionControl)
    {
        // PRIORITY 1: Check starred projects' pipelines
        logger.LogInformation("\n\n================ CHECKING STARRED PROJECTS PIPELINES ================\n");
        try
        {
            await pipelineProcessor.ProcessStarredProjectsAsync(server);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking pipelines for {UserName}", server.UserName);
        }

        logger.LogInformation("\n\n================ CHECKING ISSUES ================\n");

        // PRIORITY 2: Process assigned issues
        var assignedIssues = await versionControl
            .GetAssignedIssues(server.EndPoint, server.UserName, server.Token)
            .ToListAsync();

        logger.LogInformation("Got {IssuesCount} issues assigned to {UserName}", assignedIssues.Count, server.UserName);
        logger.LogInformation("\n\n================================================================\n\n");

        foreach (var issue in assignedIssues)
        {
            await ProcessIssueAsync(issue, server);
        }

        // PRIORITY 3: Check and fix failed merge requests
        logger.LogInformation("\n\n================ CHECKING MERGE REQUESTS ================\n");
        logger.LogInformation("Checking merge requests before processing issues...");

        try
        {
            await mergeRequestProcessor.ProcessMergeRequestsAsync(server);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking merge requests for {UserName}", server.UserName);
        }

        // PRIORITY 4: Review merge requests assigned to bot
        logger.LogInformation("\n\n================ CHECKING REVIEWS ================\n");
        try
        {
            await mergeRequestReviewerProcessor.ProcessReviewRequestsAsync(server);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reviewing merge requests for {UserName}", server.UserName);
        }
    }

    private async Task ProcessIssueAsync(Issue issue, Server server)
    {
        try
        {
            logger.LogInformation("Processing issue: {Issue}...", issue);

            var result = await issueProcessor.ProcessAsync(issue, server);

            if (result.Success)
            {
                logger.LogInformation("Issue #{IssueId} processed: {Message}", issue.Iid, result.Message);
            }
            else
            {
                logger.LogWarning("Issue #{IssueId} processing failed: {Message}", issue.Iid, result.Message);
                if (result.Error != null)
                {
                    logger.LogError(result.Error, "Exception details for issue #{IssueId}", issue.Iid);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception when processing issue: {Issue}", issue);
        }
        finally
        {
            logger.LogInformation("\n\n================================================================\n\n");
        }
    }
}
