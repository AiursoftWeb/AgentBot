using Aiursoft.GitRunner.Models;
using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Models;
using Aiursoft.AgentBot.Services.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiursoft.AgentBot.Services;

/// <summary>
/// A declarative engine for executing bot workflows.
/// Provides high-level building blocks for issue resolution and MR maintenance.
/// </summary>
public class BotWorkflowEngine(
    IVersionControlService versionControl,
    IAiWorkspaceManager workspaceManager,
    AiCliService aiCliService,
    IAiCommandService commandService,
    IOptions<AgentBotOptions> options,
    ILogger<BotWorkflowEngine> logger)
{
    private readonly AgentBotOptions _options = options.Value;

    public async Task<WorkflowContext> ExecuteAsync(WorkflowContext context,
        Func<WorkflowContext, Task>? finalizeAsync = null)
    {
        try
        {
            logger.LogInformation("Starting workflow for workspace {WorkspaceName}. Focus: {CommitMessage}",
                context.WorkspaceName, context.CommitMessage);
            await GetRepository(context);
            await PrepareWorkspace(context);
            if (context.NeedResolveConflicts)
            {
                await TriggerMergeAsync(context);
            }

            await RunAi(context);

            if (context.SkipCommit)
            {
                if (finalizeAsync != null)
                {
                    await finalizeAsync(context);
                }
                logger.LogInformation("SkipCommit is true for {WorkspaceName}. Skipping commit and push.",
                    context.WorkspaceName);
                context.Result = ProcessResult.Succeeded("Successfully processed without commit");
                return context;
            }

            if (await HasChanges(context))
            {
                await CommitChanges(context);
                if (finalizeAsync != null)
                {
                    await finalizeAsync(context);
                }
                else
                {
                    var pushPath = versionControl.GetPushPath(context.Server, context.Repository!);
                    await PushAndFinalizeAsync(context, pushPath);
                }
            }
            else
            {
                logger.LogInformation("No changes detected for {WorkspaceName}. Skipping finalize.",
                    context.WorkspaceName);
                context.Result = ProcessResult.Skipped("No changes made");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow failed for {WorkspaceName}", context.WorkspaceName);
            context.Result = ProcessResult.Failed(ex.Message, ex);
        }

        return context;
    }

    private async Task GetRepository(WorkflowContext context)
    {
        logger.LogInformation("Fetching repository details for project {ProjectId}...", context.ProjectId);
        context.Repository = await versionControl.GetRepository(
            context.Server.EndPoint,
            context.ProjectId,
            string.Empty,
            context.Server.Token);
    }

    private async Task PrepareWorkspace(WorkflowContext context)
    {
        var repoName = context.Repository?.Name ?? "unknown";
        context.WorkspacePath = Path.Combine(_options.WorkspaceFolder,
            $"{context.ProjectId}-{repoName}-{context.WorkspaceName}");

        logger.LogInformation("Cloning repository to {WorkPath}...", context.WorkspacePath);

        await workspaceManager.ResetRepo(
            context.WorkspacePath,
            context.SourceBranch,
            context.Repository?.CloneUrl ?? throw new InvalidOperationException("Repository clone URL is null"),
            CloneMode.Full,
            $"{context.Server.UserName}:{context.Server.Token}");

        await workspaceManager.SetUserConfig(context.WorkspacePath, context.Server.DisplayName,
            context.Server.UserEmail);
    }

    private async Task TriggerMergeAsync(WorkflowContext context)
    {
        logger.LogInformation("Proactively merging {TargetBranch} into {SourceBranch} to trigger conflicts...",
            context.TargetBranch, context.SourceBranch);

        // 1. 获取最新代码
        await commandService.RunCommandAsync("git", $"fetch origin {context.TargetBranch}", context.WorkspacePath,
            TimeSpan.FromSeconds(30));

        // 2. 确保是 Merge 行为，不是 Rebase
        await commandService.RunCommandAsync("git", "config pull.rebase false", context.WorkspacePath,
            TimeSpan.FromSeconds(10));

        // 3. [关键优化] 开启 diff3，让 AI 看到"冲突前的原样"，大幅提升合并逻辑判断力
        await commandService.RunCommandAsync("git", "config merge.conflictstyle diff3", context.WorkspacePath,
            TimeSpan.FromSeconds(10));

        // 4. 执行合并
        // 注意：这里我们允许非0退出码，因为冲突就是非0
        var (exitCode, output, _) = await commandService.RunCommandAsync("git", $"merge origin/{context.TargetBranch}",
            context.WorkspacePath, TimeSpan.FromSeconds(30));

        if (exitCode != 0)
        {
            // 5. [新增] 确认是否真的产生了冲突文件
            // --diff-filter=U 专门列出 Unmerged (有冲突) 的文件
            var (_, conflictFiles, _) = await commandService.RunCommandAsync("git", "diff --name-only --diff-filter=U",
                context.WorkspacePath, TimeSpan.FromSeconds(10));

            if (!string.IsNullOrWhiteSpace(conflictFiles))
            {
                logger.LogInformation("Merge conflict triggered successfully. Conflicted files:\n{Files}",
                    conflictFiles);
                // 你甚至可以将 conflictFiles 塞入 context，让 prompt 知道具体修哪些文件
            }
            else
            {
                logger.LogWarning("Merge failed but no conflicted files were found. Output: {Output}", output);
                logger.LogCritical("Unexpected merge failure without conflicts. Please investigate!!");
                // 这里可能需要抛出异常，因为如果没有冲突标记，AI 进去也不知道修什么，可能会瞎改
            }
        }
        else
        {
            logger.LogInformation("Merge was successful without conflicts.");
        }
    }

    private async Task RunAi(WorkflowContext context)
    {
        logger.LogInformation("Invoking AI CLI...");
        var (success, output, error) =
            await aiCliService.InvokeAiCliAsync(context.WorkspacePath, context.Prompt, context.HideGitFolder);
        context.AiOutput = output;
        if (!success)
        {
            logger.LogError("AI CLI failed to complete successfully. Output: {Output}. Error: {Error}", output, error);
            throw new InvalidOperationException("AI CLI failed to complete successfully.");
        }

        // AI CLI may take a while to finish and flush files.
        await Task.Delay(1000);
    }

    private async Task<bool> HasChanges(WorkflowContext context)
    {
        var hasPendingChanges = await workspaceManager.PendingCommit(context.WorkspacePath);
        var isAheadOfOrigin = await IsAheadOfOrigin(context.WorkspacePath, context.SourceBranch);
        return hasPendingChanges || isAheadOfOrigin;
    }

    private async Task CommitChanges(WorkflowContext context)
    {
        if (await workspaceManager.PendingCommit(context.WorkspacePath))
        {
            logger.LogInformation("Committing changes to branch {Branch}...", context.PushBranch);
            var saved = await workspaceManager.CommitToBranch(context.WorkspacePath, context.CommitMessage,
                context.PushBranch);
            if (!saved)
            {
                throw new InvalidOperationException("Failed to commit changes");
            }
        }
    }

    public async Task PushAndFinalizeAsync(WorkflowContext context, string pushPath)
    {
        logger.LogInformation("Pushing changes to {PushPath}...", pushPath);
        await workspaceManager.Push(context.WorkspacePath, context.PushBranch, pushPath, force: true);
        context.Result = ProcessResult.Succeeded("Successfully pushed changes");
    }

    private async Task<bool> IsAheadOfOrigin(string workPath, string branchName)
    {
        try
        {
            var (exitCode, output, _) = await commandService.RunCommandAsync(
                bin: "git",
                arg: $"rev-list --count HEAD ^origin/{branchName}",
                path: workPath,
                timeout: TimeSpan.FromSeconds(10));

            return exitCode == 0 && int.TryParse(output.Trim(), out var commitCount) && commitCount > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureRepositoryForkedAsync(Server server, Repository repository)
    {
        var ownerLogin = repository.Owner?.Login ?? throw new InvalidOperationException("Repository owner is null");
        var repoName = repository.Name ?? throw new InvalidOperationException("Repository name is null");

        if (!await versionControl.RepoExists(server.EndPoint, server.UserName, repoName, server.Token))
        {
            logger.LogInformation("Forking repository {Org}/{Repo}...", ownerLogin, repoName);
            await versionControl.ForkRepo(server.EndPoint, ownerLogin, repoName, server.Token);

            await Task.Delay(_options.ForkWaitDelayMs);
            while (!await versionControl.RepoExists(server.EndPoint, server.UserName, repoName, server.Token))
            {
                logger.LogInformation("Waiting for fork to complete...");
                await Task.Delay(_options.ForkWaitDelayMs);
            }
        }
    }
}
