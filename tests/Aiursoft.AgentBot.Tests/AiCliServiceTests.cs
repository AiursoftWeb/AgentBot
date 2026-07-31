using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class AiCliServiceTests
{
    [TestMethod]
    public async Task InvokeAiCliAsync_WithCodex_UsesYoloModeAndFileLogin()
    {
        var (arg, environmentVariables) = await InvokeCodexAsync(model: null);

        StringAssert.Contains(
            arg,
            "codex exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check --ephemeral --color never -c cli_auth_credentials_store=file");
        StringAssert.Contains(arg, " - < .ai-task.txt");
        Assert.IsFalse(arg.Contains(" --model ", StringComparison.Ordinal));
        Assert.IsNull(environmentVariables);
    }

    [TestMethod]
    public async Task InvokeAiCliAsync_WithCodexModel_PassesModelArgument()
    {
        var (arg, environmentVariables) = await InvokeCodexAsync(model: "custom-codex-model");

        StringAssert.Contains(arg, " --model custom-codex-model - < .ai-task.txt");
        Assert.IsNull(environmentVariables);
    }

    private static async Task<(string Arg, IDictionary<string, string?>? EnvironmentVariables)> InvokeCodexAsync(
        string? model)
    {
        var workPath = Path.Combine(Path.GetTempPath(), $"AgentBotAiCliTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workPath);

        try
        {
            string? capturedArg = null;
            IDictionary<string, string?>? capturedEnvironmentVariables = null;

            var commandService = new Mock<IAiCommandService>();
            commandService
                .Setup(service => service.RunCommandAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, string?>?>()))
                .Callback<string, string, string, TimeSpan, bool, IDictionary<string, string?>?>(
                    (_, arg, _, _, _, environmentVariables) =>
                    {
                        capturedArg = arg;
                        capturedEnvironmentVariables = environmentVariables;
                    })
                .ReturnsAsync((0, "done", string.Empty));

            var options = Options.Create(new AgentBotOptions
            {
                Engine = AiEngine.Codex,
                Model = model,
                ApiKey = "must-not-be-forwarded"
            });
            var service = new AiCliService(
                commandService.Object,
                options,
                Mock.Of<ILogger<AiCliService>>());

            var result = await service.InvokeAiCliAsync(workPath, "Implement the requested change.", hideGitFolder: false);

            Assert.IsTrue(result.Success);
            Assert.IsNotNull(capturedArg);
            Assert.IsFalse(File.Exists(Path.Combine(workPath, ".ai-task.txt")));
            return (capturedArg, capturedEnvironmentVariables);
        }
        finally
        {
            Directory.Delete(workPath, recursive: true);
        }
    }
}
