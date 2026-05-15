using Aiursoft.GitRunner.Models;

namespace Aiursoft.AgentBot.Services.Abstractions;

public interface IAiWorkspaceManager
{
    Task ResetRepo(string path, string branch, string url, CloneMode mode, string auth);
    Task<bool> CommitToBranch(string path, string message, string branch);
    Task<bool> PendingCommit(string path);
    Task Push(string path, string branch, string url, bool force);
    Task SetUserConfig(string path, string name, string email);
}
