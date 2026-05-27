using Aiursoft.AgentBot.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Aiursoft.AgentBot.Services;

/// <summary>
/// Handles checking for failing pipelines on starred projects and creating issues.
/// </summary>
public class PipelineProcessor(
    IVersionControlService versionControl,
    HttpWrapper httpWrapper,
    IHttpClientFactory httpClientFactory,
    ILogger<PipelineProcessor> logger)
{
    public async Task<ProcessResult> ProcessStarredProjectsAsync(Server server)
    {
        if (server.Provider != "GitLab")
        {
            logger.LogInformation("Pipeline monitoring is currently only supported for GitLab. Skipping server {EndPoint}", server.EndPoint);
            return ProcessResult.Succeeded("Skipped non-GitLab server");
        }

        try
        {
            var starredProjects = await GetStarredProjectsAsync(server);
            if (starredProjects.Count == 0)
            {
                logger.LogInformation("No starred projects found.");
                return ProcessResult.Succeeded("No starred projects");
            }

            logger.LogInformation("Found {Count} starred projects. Checking pipelines...", starredProjects.Count);

            foreach (var project in starredProjects)
            {
                await CheckProjectPipelineAsync(server, project);
            }

            return ProcessResult.Succeeded($"Checked pipelines for {starredProjects.Count} projects");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing pipelines for starred projects");
            return ProcessResult.Failed("Error processing pipelines", ex);
        }
    }

    private async Task<List<GitLabProjectDto>> GetStarredProjectsAsync(Server server)
    {
        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects?starred=true&per_page=100&membership=true";
        return await httpWrapper.SendHttpAndGetJson<List<GitLabProjectDto>>(url, HttpMethod.Get, server.Token);
    }

    private async Task CheckProjectPipelineAsync(Server server, GitLabProjectDto project)
    {
        try
        {
            logger.LogInformation("Checking pipeline for project {ProjectName} (ID: {ProjectId})...", project.Name, project.Id);

            var pipelinesUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{project.Id}/pipelines?ref={project.DefaultBranch}&per_page=1";
            var pipelines = await httpWrapper.SendHttpAndGetJson<List<GitLabPipelineDto>>(pipelinesUrl, HttpMethod.Get, server.Token);

            var latestPipeline = pipelines.FirstOrDefault();
            if (latestPipeline != null && latestPipeline.Status == "failed")
            {
                logger.LogWarning("Pipeline failed for {ProjectName} on branch {Branch}. Checking if issue already exists...", project.Name, project.DefaultBranch);

                var issueTitle = "主分支的编译管道失败";
                if (await IssueAlreadyExistsAsync(server, project.Id, issueTitle))
                {
                    logger.LogInformation("Issue already exists and is assigned to bot. Skipping.");
                    return;
                }

                logger.LogInformation("Creating issue for failed pipeline in {ProjectName}...", project.Name);
                var logs = await GetFailureLogsAsync(server, project.Id, latestPipeline.Id);
                await CreateIssueAsync(server, project.Id, issueTitle, logs);
            }
            else
            {
                logger.LogInformation("Pipeline for {ProjectName} is {Status}.", project.Name, latestPipeline?.Status ?? "not found");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check pipeline for project {ProjectName}", project.Name);
        }
    }

    private async Task<bool> IssueAlreadyExistsAsync(Server server, int projectId, string title)
    {
        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectId}/issues?state=opened&search={Uri.EscapeDataString(title)}&assignee_username={server.UserName}";
        var issues = await httpWrapper.SendHttpAndGetJson<List<GitLabIssueDto>>(url, HttpMethod.Get, server.Token);
        return issues.Any(i => i.Title == title);
    }

    private async Task CreateIssueAsync(Server server, int projectId, string title, string description)
    {
        var userUrl = $"{server.EndPoint.TrimEnd('/')}/api/v4/user";
        var user = await httpWrapper.SendHttpAndGetJson<GitLabUser>(userUrl, HttpMethod.Get, server.Token);

        var url = $"{server.EndPoint.TrimEnd('/')}/api/v4/projects/{projectId}/issues";
        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.Token);

        var response = await client.PostAsJsonAsync(url, new
        {
            title,
            description,
            assignee_ids = new[] { user.Id }
        });

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Successfully created and assigned issue in project {ProjectId}", projectId);
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            logger.LogError("Failed to create issue in project {ProjectId}: {Error}", projectId, error);
        }
    }

    private async Task<string> GetFailureLogsAsync(Server server, int projectId, int pipelineId)
    {
        try
        {
            var jobs = await versionControl.GetPipelineJobs(server.EndPoint, server.Token, projectId, pipelineId);
            var failedJobs = jobs.Where(j => j.Status == "failed").ToList();
            var allLogs = new StringBuilder();
            allLogs.AppendLine("主分支的编译管道失败。");
            allLogs.AppendLine($"Pipeline ID: {pipelineId}");
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting failure logs for pipeline {PipelineId}", pipelineId);
            return "Failed to fetch logs.";
        }
    }
}
