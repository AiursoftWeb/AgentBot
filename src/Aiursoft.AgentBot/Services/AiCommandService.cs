using Aiursoft.CSTools.Services;
using Aiursoft.AgentBot.Services.Abstractions;

namespace Aiursoft.AgentBot.Services;

public class AiCommandService(CommandService commandService) : IAiCommandService
{
    public Task<(int exitCode, string output, string error)> RunCommandAsync(
        string bin,
        string arg,
        string path,
        TimeSpan timeout,
        bool useShell = false,
        IDictionary<string, string?>? environmentVariables = null)
    {
        return commandService.RunCommandAsync(bin, arg, path, timeout, useShell, environmentVariables);
    }
}
