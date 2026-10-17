using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

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
        StringAssert.Contains(exception.Message, "Claude, Codex, or Copilot");
    }

    [TestMethod]
    public void RejectRemovedEngine_WithSupportedEngine_DoesNotThrow()
    {
        AgentBotOptions.RejectRemovedEngine("Claude");
        AgentBotOptions.RejectRemovedEngine("Codex");
        AgentBotOptions.RejectRemovedEngine("Copilot");
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
                new StubCommandService(),
                Options.Create(new AgentBotOptions
                {
                    Engine = AiEngine.Claude,
                    ReasoningEffort = CodexReasoningEffort.High
                }),
                NullLogger<AiCliService>.Instance);

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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvokeCopilot_WithGateway_UsesVirtualModelAndPreservesTask(bool planningOnly)
    {
        var workPath = Path.Combine(Path.GetTempPath(), $"AgentBotCopilotTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workPath, ".git"));
        const string task = "Inspect `literal` and $(literal), quotes \" and ' and a newline.\nReturn JSON.";
        try
        {
            var command = new StubCommandService();
            string? taskFile = null;
            command.RunCommandAsyncHandler = async (_, arg, path, timeout, _, environment) =>
            {
                Assert.AreEqual(workPath, path);
                Assert.AreEqual(TimeSpan.FromMinutes(7), timeout);
                StringAssert.Contains(arg, "copilot --yolo --no-ask-user --silent --no-color --effort=xhigh < '");
                Assert.IsFalse(arg.Contains("--model", StringComparison.Ordinal));
                Assert.AreEqual("aiursoft-codex:latest", environment!["COPILOT_MODEL"]);
                Assert.AreEqual("https://gateway.example/v1", environment["COPILOT_PROVIDER_BASE_URL"]);
                Assert.AreEqual("test-key", environment["COPILOT_PROVIDER_API_KEY"]);
                Assert.IsFalse(arg.Contains("test-key", StringComparison.Ordinal));
                Assert.AreEqual(planningOnly, Directory.Exists(Path.Combine(workPath, ".git")));
                taskFile = arg.Split(" < '", 2)[1].Split('\'')[0];
                Assert.AreEqual(task, await File.ReadAllTextAsync(taskFile));
                return (0, "{\"action\":\"respond\"}", string.Empty);
            };
            var service = new AiCliService(command, Options.Create(new AgentBotOptions
            {
                Engine = AiEngine.Copilot,
                ApiEndpoint = "https://gateway.example/v1",
                ApiKey = "test-key",
                Model = "aiursoft-codex:latest",
                ReasoningEffort = CodexReasoningEffort.XHigh,
                AiTimeout = TimeSpan.FromMinutes(7)
            }), NullLogger<AiCliService>.Instance);
            var result = planningOnly
                ? await service.InvokePlanningCliAsync(workPath, task)
                : await service.InvokeAiCliAsync(workPath, task, hideGitFolder: true);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("{\"action\":\"respond\"}", result.Output);
            Assert.IsTrue(Directory.Exists(Path.Combine(workPath, ".git")));
            Assert.IsNotNull(taskFile);
            Assert.IsFalse(File.Exists(taskFile));
        }
        finally
        {
            Directory.Delete(workPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvokeCopilot_Failure_RestoresGitAndReportsError()
    {
        var workPath = Path.Combine(Path.GetTempPath(), $"AgentBotCopilotTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(workPath, ".git"));
        try
        {
            var command = new StubCommandService
            {
                RunCommandAsyncHandler = (_, _, _, _, _, _) => Task.FromResult((1, "", "Authentication failed"))
            };
            var service = new AiCliService(command, Options.Create(new AgentBotOptions
            {
                Engine = AiEngine.Copilot
            }), NullLogger<AiCliService>.Instance);
            var result = await service.InvokeAiCliAsync(workPath, "Inspect the repo.", hideGitFolder: true);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("Authentication failed", result.Error);
            Assert.IsTrue(Directory.Exists(Path.Combine(workPath, ".git")));
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

            var commandService = new StubCommandService();
            commandService.RunCommandAsyncHandler = (_, arg, _, _, _, environmentVariables) =>
            {
                capturedArg = arg;
                capturedEnvironmentVariables = environmentVariables;
                return Task.FromResult((0, "done", string.Empty));
            };

            var options = Options.Create(new AgentBotOptions
            {
                Engine = AiEngine.Codex,
                Model = model,
                ReasoningEffort = reasoningEffort,
                ApiKey = "must-not-be-forwarded"
            });
            var service = new AiCliService(
                commandService,
                options,
                NullLogger<AiCliService>.Instance);

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
