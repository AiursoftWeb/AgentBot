using Aiursoft.GitRunner;
using Aiursoft.GitRunner.Models;
using Aiursoft.AgentBot.Services.Abstractions;

namespace Aiursoft.AgentBot.Services;

public class AiWorkspaceManager(WorkspaceManager workspaceManager) : IAiWorkspaceManager
{
    public Task ResetRepo(string path, string branch, string url, CloneMode mode, string auth)
    {
        return workspaceManager.ResetRepo(path, branch, url, mode, auth);
    }

    public Task<bool> CommitToBranch(string path, string message, string branch)
    {
        // Fix for issue #20: Escape double quotes to prevent git commit failure
        var escapedMessage = message.Replace("\"", "\\\"");
        return workspaceManager.CommitToBranch(path, escapedMessage, branch);
    }

    public Task<bool> PendingCommit(string path)
    {
        return workspaceManager.PendingCommit(path);
    }

    public Task Push(string path, string branch, string url, bool force)
    {
        return workspaceManager.Push(path, branch, url, force);
    }

    public Task SetUserConfig(string path, string name, string email)
    {
        return workspaceManager.SetUserConfig(path, name, email);
    }
}
