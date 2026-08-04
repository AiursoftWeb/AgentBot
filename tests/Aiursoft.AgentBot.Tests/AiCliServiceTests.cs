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
    public void RejectRemovedEngine_WithLegacyGeminiConfiguration_FailsClearly()
    {
        var exception = Assert.ThrowsExactly<NotSupportedException>(
            () => AgentBotOptions.RejectRemovedEngine("gEmInI"));

        StringAssert.Contains(exception.Message, "no longer supported");
        StringAssert.Contains(exception.Message, "Claude or Codex");
    }

    [TestMethod]
    public void RejectRemovedEngine_WithSupportedEngine_DoesNotThrow()
    {
        AgentBotOptions.RejectRemovedEngine("Claude");
        AgentBotOptions.RejectRemovedEngine("Codex");
    }

    [TestMethod]
    [DataRow("minimal", CodexReasoningEffort.Minimal)]
    [DataRow(" LOW ", CodexReasoningEffort.Low)]
    [DataRow("medium", CodexReasoningEffort.Medium)]
    [DataRow("High", CodexReasoningEffort.High)]
    [DataRow("xhigh", CodexReasoningEffort.XHigh)]
    public void ParseReasoningEffort_WithSupportedValue_ReturnsNormalizedValue(
        string configuredValue,
        CodexReasoningEffort expected)
    {
        Assert.AreEqual(expected, AgentBotOptions.ParseReasoningEffort(configuredValue));
    }

    [TestMethod]
    public void ParseReasoningEffort_WithEmptyValue_ReturnsNull()
    {
        Assert.IsNull(AgentBotOptions.ParseReasoningEffort(null));
        Assert.IsNull(AgentBotOptions.ParseReasoningEffort("  "));
    }

    [TestMethod]
    public void ParseReasoningEffort_WithUnsupportedValue_FailsClearly()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => AgentBotOptions.ParseReasoningEffort("ultra"));

        StringAssert.Contains(exception.Message, "AgentBot__ReasoningEffort");
        StringAssert.Contains(exception.Message, "minimal, low, medium, high, or xhigh");
    }

    [TestMethod]
    public async Task InvokeAiCliAsync_WithCodex_UsesYoloModeAndFileLogin()
    {
        var (arg, environmentVariables) = await InvokeCodexAsync(model: null);

        StringAssert.Contains(
            arg,
            "codex exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check --ephemeral --color never -c cli_auth_credentials_store=file");
        StringAssert.Contains(arg, " - < '/tmp/agentbot-");
        Assert.IsFalse(arg.Contains(" --model ", StringComparison.Ordinal));
        Assert.IsFalse(arg.Contains("model_reasoning_effort", StringComparison.Ordinal));
        Assert.IsNull(environmentVariables);
    }

    [TestMethod]
    public async Task InvokeAiCliAsync_WithCodexReasoningEffort_PassesConfigOverride()
    {
        var (arg, environmentVariables) = await InvokeCodexAsync(
            model: "gpt-5.6-sol",
            reasoningEffort: CodexReasoningEffort.High);

        StringAssert.Contains(arg, " --model gpt-5.6-sol");
        StringAssert.Contains(arg, " -c 'model_reasoning_effort=\\\"high\\\"'");
        Assert.IsNull(environmentVariables);
    }

    [TestMethod]
    public async Task InvokeAiCliAsync_WithCodexModel_PassesModelArgument()
    {
        var (arg, environmentVariables) = await InvokeCodexAsync(model: "custom-codex-model");

        StringAssert.Contains(arg, " --model custom-codex-model - < '/tmp/agentbot-");
        Assert.IsNull(environmentVariables);
    }

    [TestMethod]
    public async Task InvokePlanningCliAsync_WithCodex_UsesOuterContainerIsolation()
    {
        var (arg, environmentVariables) = await InvokeCodexAsync(model: null, planningOnly: true);

        StringAssert.Contains(arg, "codex exec --dangerously-bypass-approvals-and-sandbox --ignore-user-config");
        Assert.IsFalse(arg.Contains("--sandbox read-only", StringComparison.Ordinal));
        StringAssert.Contains(arg, " --ephemeral ");
        Assert.IsNull(environmentVariables);
    }

    [TestMethod]
    public async Task InvokePlanningCliAsync_WithReasoningEffort_PassesOverrideDespiteIgnoredUserConfig()
    {
        var (arg, _) = await InvokeCodexAsync(
            model: "gpt-5.6-sol",
            planningOnly: true,
            reasoningEffort: CodexReasoningEffort.XHigh);

        StringAssert.Contains(arg, " --ignore-user-config ");
        StringAssert.Contains(arg, " -c 'model_reasoning_effort=\\\"xhigh\\\"'");
    }

    [TestMethod]
    public async Task InvokeAiCliAsync_WithClaudeReasoningEffort_FailsClearly()
    {
        var workPath = Path.Combine(Path.GetTempPath(), $"AgentBotAiCliTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workPath);

        try
        {
            var service = new AiCliService(
                Mock.Of<IAiCommandService>(),
                Options.Create(new AgentBotOptions
                {
                    Engine = AiEngine.Claude,
                    ReasoningEffort = CodexReasoningEffort.High
                }),
                Mock.Of<ILogger<AiCliService>>());

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => service.InvokeAiCliAsync(workPath, "Implement the requested change.", hideGitFolder: false));

            StringAssert.Contains(exception.Message, "AgentBot__ReasoningEffort");
            StringAssert.Contains(exception.Message, "Codex");
        }
        finally
        {
            Directory.Delete(workPath, recursive: true);
        }
    }

    private static async Task<(string Arg, IDictionary<string, string?>? EnvironmentVariables)> InvokeCodexAsync(
        string? model,
        bool planningOnly = false,
        CodexReasoningEffort? reasoningEffort = null)
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
                ReasoningEffort = reasoningEffort,
                ApiKey = "must-not-be-forwarded"
            });
            var service = new AiCliService(
                commandService.Object,
                options,
                Mock.Of<ILogger<AiCliService>>());

            var result = planningOnly
                ? await service.InvokePlanningCliAsync(workPath, "Plan the requested change.")
                : await service.InvokeAiCliAsync(workPath, "Implement the requested change.", hideGitFolder: false);

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
