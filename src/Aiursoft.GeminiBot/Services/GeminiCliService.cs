using Aiursoft.GeminiBot.Configuration;
using Aiursoft.GeminiBot.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiursoft.GeminiBot.Services;

public class GeminiCliService(
    IGeminiCommandService commandService,
    IOptions<GeminiBotOptions> options,
    ILogger<GeminiCliService> logger)
{
    private readonly GeminiBotOptions _options = options.Value;

    public virtual async Task<(bool Success, string Output, string Error)> InvokeGeminiCliAsync(string workPath, string taskDescription, bool hideGitFolder)
    {
        string? tempFile = null;
        var gitPath = Path.Combine(workPath, ".git");
        var gitBackupPath = workPath + "-hidden-git";

        try
        {
            // Write task to temp file
            tempFile = Path.Combine(workPath, ".gemini-task.txt");
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

            var (command, envVars) = BuildCommandAndEnv();

            var (code, output, error) = await commandService.RunCommandAsync(
                bin: "/bin/bash",
                arg: $"-c \"{command}\"",
                path: workPath,
                timeout: _options.GeminiTimeout,
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

    private (string Command, IDictionary<string, string?>? EnvVars) BuildCommandAndEnv()
    {
        // Resolve API key with backward compatibility
#pragma warning disable CS0618
        var apiKey = _options.ApiKey ?? _options.GeminiApiKey;
#pragma warning restore CS0618

        var modelArg = !string.IsNullOrWhiteSpace(_options.Model)
            ? $" --model {_options.Model}"
            : "";

        return _options.Engine switch
        {
            AiEngine.Gemini => (
                $"gemini --yolo{modelArg} < .gemini-task.txt",
                BuildEnv("GEMINI_API_KEY", apiKey)),

            AiEngine.Claude => (
                $"claude --dangerously-skip-permissions --print{modelArg} < .gemini-task.txt",
                BuildClaudeEnv(apiKey)),

            _ => throw new ArgumentOutOfRangeException(nameof(_options.Engine), $"Unsupported AI engine: {_options.Engine}")
        };
    }

    private static IDictionary<string, string?>? BuildEnv(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return new Dictionary<string, string?> { [key] = value };
    }

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
