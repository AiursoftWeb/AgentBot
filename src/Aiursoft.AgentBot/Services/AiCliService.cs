using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiursoft.AgentBot.Services;

public class AiCliService(
    IAiCommandService commandService,
    IOptions<AgentBotOptions> options,
    ILogger<AiCliService> logger)
{
    private readonly AgentBotOptions _options = options.Value;

    public virtual async Task<(bool Success, string Output, string Error)> InvokeAiCliAsync(string workPath, string taskDescription, bool hideGitFolder)
    {
        return await InvokeAiCliInternalAsync(workPath, taskDescription, hideGitFolder, planningOnly: false);
    }

    public virtual async Task<(bool Success, string Output, string Error)> InvokePlanningCliAsync(
        string workPath,
        string taskDescription)
    {
        if (_options.Engine != AiEngine.Codex)
        {
            throw new NotSupportedException(
                $"Planning mode is currently supported only for Codex, not {_options.Engine}.");
        }

        return await InvokeAiCliInternalAsync(workPath, taskDescription, hideGitFolder: false, planningOnly: true);
    }

    private async Task<(bool Success, string Output, string Error)> InvokeAiCliInternalAsync(
        string workPath,
        string taskDescription,
        bool hideGitFolder,
        bool planningOnly)
    {
        string? tempFile = null;
        var gitPath = Path.Combine(workPath, ".git");
        var gitBackupPath = workPath + "-hidden-git";

        try
        {
            // Write task to temp file
            tempFile = Path.Combine(Path.GetTempPath(), $"agentbot-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tempFile, taskDescription);

            // Hide .git directory to prevent AI from manipulating git (if requested)
            if (hideGitFolder && Directory.Exists(gitPath))
            {
                logger.LogInformation("Hiding .git directory to prevent AI from manipulating git...");
                Directory.Move(gitPath, gitBackupPath);
            }
            else if (!hideGitFolder)
            {
                logger.LogInformation(".git directory is accessible for viewing history");
            }

            logger.LogInformation("Running AI engine ({Engine}) in {WorkPath}", _options.Engine, workPath);

            var (command, envVars) = BuildCommandAndEnv(planningOnly, tempFile);

            var (code, output, error) = await commandService.RunCommandAsync(
                bin: "/bin/bash",
                arg: $"-c \"{command}\"",
                path: workPath,
                timeout: _options.AiTimeout,
                environmentVariables: envVars);

            if (code != 0)
            {
                logger.LogError("AI CLI failed with exit code {Code}. Output: {Output}. Error: {Error}", code, output, error);
                return (false, output, error);
            }

            logger.LogInformation("AI CLI completed successfully. It says: {Output}", output);
            return (true, output, error);
        }
        finally
        {
            // Restore .git directory
            if (Directory.Exists(gitBackupPath))
            {
                try
                {
                    logger.LogInformation("Restoring .git directory...");
                    if (Directory.Exists(gitPath))
                    {
                        Directory.Delete(gitPath, recursive: true);
                    }
                    Directory.Move(gitBackupPath, gitPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to restore .git directory from backup!");
                }
            }

            // Clean up temp file
            if (tempFile != null && File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete temporary file: {FilePath}", tempFile);
                }
            }
        }
    }

    private (string Command, IDictionary<string, string?>? EnvVars) BuildCommandAndEnv(
        bool planningOnly,
        string taskFile)
    {
        var apiKey = _options.ApiKey;

        var modelArg = !string.IsNullOrWhiteSpace(_options.Model)
            ? $" --model {_options.Model}"
            : "";

        return _options.Engine switch
        {
            AiEngine.Claude => (
                $"claude --dangerously-skip-permissions --print{modelArg} < {ShellQuote(taskFile)}",
                BuildClaudeEnv(apiKey)),

            AiEngine.Codex => (
                planningOnly
                    ? $"codex exec --dangerously-bypass-approvals-and-sandbox --ignore-user-config --skip-git-repo-check --ephemeral --color never -c cli_auth_credentials_store=file{modelArg} - < {ShellQuote(taskFile)}"
                    : $"codex exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check --ephemeral --color never -c cli_auth_credentials_store=file{modelArg} - < {ShellQuote(taskFile)}",
                null),

            _ => throw new ArgumentOutOfRangeException(nameof(_options.Engine), $"Unsupported AI engine: {_options.Engine}")
        };
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private IDictionary<string, string?>? BuildClaudeEnv(string? apiKey)
    {
        var vars = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(apiKey))
            vars["ANTHROPIC_API_KEY"] = apiKey;
        if (!string.IsNullOrWhiteSpace(_options.ApiEndpoint))
            vars["ANTHROPIC_BASE_URL"] = _options.ApiEndpoint;
        return vars.Count > 0 ? vars : null;
    }
}
