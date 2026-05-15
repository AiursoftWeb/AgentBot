namespace Aiursoft.AgentBot.Services.Abstractions;

public interface IAiCommandService
{
    Task<(int exitCode, string output, string error)> RunCommandAsync(
        string bin,
        string arg,
        string path,
        TimeSpan timeout,
        bool useShell = false,
        IDictionary<string, string?>? environmentVariables = null);
}
