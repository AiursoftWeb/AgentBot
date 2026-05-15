using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Models;
using Aiursoft.AgentBot.Services.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using System.Net;

namespace Aiursoft.AgentBot.Tests;

public class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return handler(request);
    }
}

[TestClass]
public class MergeRequestProcessorTests
{
    private Mock<IVersionControlService> _versionControlMock = null!;
    private Mock<IAiWorkspaceManager> _workspaceManagerMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private Mock<AiCliService> _aiCliServiceMock = null!;
    private Mock<IAiCommandService> _commandServiceMock = null!;
    private Mock<ILogger<MergeRequestProcessor>> _loggerMock = null!;
    private IOptions<AgentBotOptions> _options = null!;
    private List<GitLabMergeRequestDto> _gitLabMrList = new();
    private GitLabUser _botUser = new();
    private List<GitLabMergeRequestDto> _botMrList = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    [TestInitialize]
    public void SetUp()
    {
        var options = new AgentBotOptions
        {
            WorkspaceFolder = Path.Combine(Path.GetTempPath(), "AgentBotTests"),
            ForkWaitDelayMs = 0
        };
        _options = Options.Create(options);

        _commandServiceMock = new Mock<IAiCommandService>();

        _versionControlMock = new Mock<IVersionControlService>();

        _workspaceManagerMock = new Mock<IAiWorkspaceManager>();

        _aiCliServiceMock = new Mock<AiCliService>(
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<AiCliService>>().Object);

        _loggerMock = new Mock<ILogger<MergeRequestProcessor>>();

        // Mock HttpWrapper by mocking HttpClient
        var handler = new FakeHttpMessageHandler((req) =>
        {
            var url = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Get && url.Contains("merge_requests") && url.Contains("scope=assigned_to_me"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_gitLabMrList))
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("merge_requests") && url.Contains("discussions"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("merge_requests") && url.Contains("commits"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                });
            }
            if (req.Method == HttpMethod.Get && url.EndsWith("/api/v4/user"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_botUser))
                });
            }
            if (req.Method == HttpMethod.Put && url.Contains("merge_requests/") && url.Contains("assignee_ids="))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("source_branch=fix-mr-1"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_botMrList))
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(handler);
        _httpWrapper = new HttpWrapper(new Mock<ILogger<HttpWrapper>>().Object, client);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_OthersMr_ForksAndCreatesNewMr()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };

        var gitLabMr = new GitLabMergeRequestDto
        {
            Iid = 1,
            Title = "Test MR",
            ProjectId = 101,
            SourceProjectId = 102,
            SourceBranch = "feature",
            TargetBranch = "main",
            Author = new GitLabUser { Username = "other-user", Id = 999 }
        };
        _gitLabMrList = new List<GitLabMergeRequestDto> { gitLabMr };
        _botUser = new GitLabUser { Id = 123, Username = "bot-user" };
        _botMrList = new List<GitLabMergeRequestDto> { new GitLabMergeRequestDto { Iid = 2, Title = "Replacement MR" } };

        var detailedMr = JsonSerializer.Deserialize<DetailedMergeRequest>(@"
        {
            ""HasConflicts"": false,
            ""MrPipeline"": { ""Status"": ""failed"", ""Id"": 555, ""WebUrl"": ""http://gitlab.com/pipeline/555"" }
        }", _jsonOptions)!;

        var repository = new Repository
        {
            CloneUrl = "https://gitlab.com/other-user/repo.git",
            Name = "repo",
            Owner = new User { Login = "other-user" }
        };

        var failedJob = JsonSerializer.Deserialize<PipelineJob>(@"
        {
            ""Id"": 1,
            ""Name"": ""test"",
            ""Status"": ""failed"",
            ""Stage"": ""test""
        }", _jsonOptions)!;

        _versionControlMock
            .Setup(v => v.GetMergeRequestDetails(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(detailedMr);

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);

        _versionControlMock
            .Setup(v => v.GetPipelineJobs(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<PipelineJob> { failedJob });

        _versionControlMock
            .Setup(v => v.GetJobLog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync("Build failed at line 42");

        _versionControlMock
            .Setup(v => v.RepoExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _aiCliServiceMock
            .Setup(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((true, "AI output", ""));

        _workspaceManagerMock
            .Setup(w => w.PendingCommit(It.IsAny<string>()))
            .ReturnsAsync(true);

        _workspaceManagerMock
            .Setup(w => w.CommitToBranch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "1", ""));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);

        var processor = new MergeRequestProcessor(
            _versionControlMock.Object,
            workflowEngine,
            _httpWrapper,
            _loggerMock.Object);


        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        // Verify new MR creation
        _versionControlMock.Verify(v => v.CreatePullRequest(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_OwnMr_PushesToOriginalBranch()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };

        var gitLabMr = new GitLabMergeRequestDto
        {
            Iid = 1,
            Title = "Test MR",
            ProjectId = 101,
            SourceProjectId = 101, // Same project
            SourceBranch = "fix-bug",
            TargetBranch = "main",
            Author = new GitLabUser { Username = "bot-user", Id = 123 }
        };
        _gitLabMrList = new List<GitLabMergeRequestDto> { gitLabMr };

        var detailedMr = JsonSerializer.Deserialize<DetailedMergeRequest>(@"
        {
            ""HasConflicts"": false,
            ""MrPipeline"": { ""Status"": ""failed"", ""Id"": 555, ""WebUrl"": ""http://gitlab.com/pipeline/555"" }
        }", _jsonOptions)!;

        var repository = new Repository
        {
            CloneUrl = "https://gitlab.com/bot-user/repo.git",
            Name = "repo",
            Owner = new User { Login = "bot-user" }
        };

        var failedJob = JsonSerializer.Deserialize<PipelineJob>(@"
        {
            ""Id"": 1,
            ""Name"": ""test"",
            ""Status"": ""failed"",
            ""Stage"": ""test""
        }", _jsonOptions)!;

        _versionControlMock
            .Setup(v => v.GetMergeRequestDetails(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(detailedMr);

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);

        _versionControlMock
            .Setup(v => v.GetPipelineJobs(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<PipelineJob> { failedJob });

        _versionControlMock
            .Setup(v => v.GetJobLog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync("Build failed");

        _aiCliServiceMock
            .Setup(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((true, "AI output", ""));

        _workspaceManagerMock
            .Setup(w => w.PendingCommit(It.IsAny<string>()))
            .ReturnsAsync(true);

        _workspaceManagerMock
            .Setup(w => w.CommitToBranch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "1", ""));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);

        var processor = new MergeRequestProcessor(
            _versionControlMock.Object,
            workflowEngine,
            _httpWrapper,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        // Verify push to original branch
        _workspaceManagerMock.Verify(w => w.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), true), Times.Once);

        // Verify NO new MR was created
        _versionControlMock.Verify(v => v.CreatePullRequest(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_WithConflicts_TriggersMerge()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };

        var gitLabMr = new GitLabMergeRequestDto
        {
            Iid = 1,
            Title = "Conflict MR",
            ProjectId = 101,
            SourceProjectId = 101,
            SourceBranch = "conflict-branch",
            TargetBranch = "main",
            Author = new GitLabUser { Username = "bot-user", Id = 123 }
        };
        _gitLabMrList = new List<GitLabMergeRequestDto> { gitLabMr };

        var detailedMr = JsonSerializer.Deserialize<DetailedMergeRequest>(@"
        {
            ""HasConflicts"": true,
            ""MrPipeline"": { ""Status"": ""success"", ""Id"": 555, ""WebUrl"": ""http://gitlab.com/pipeline/555"" }
        }", _jsonOptions)!;

        var repository = new Repository
        {
            CloneUrl = "https://gitlab.com/bot-user/repo.git",
            Name = "repo",
            Owner = new User { Login = "bot-user" }
        };

        _versionControlMock
            .Setup(v => v.GetMergeRequestDetails(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(detailedMr);

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);

        _aiCliServiceMock
            .Setup(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((true, "AI output", ""));

        _workspaceManagerMock
            .Setup(w => w.PendingCommit(It.IsAny<string>()))
            .ReturnsAsync(true);

        _workspaceManagerMock
            .Setup(w => w.CommitToBranch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                It.IsAny<string>(),
                "rev-list --count HEAD ^origin/conflict-branch",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "1", ""));

        // Expect git fetch and git merge
        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                "git",
                "fetch origin main",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "", ""));

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                "git",
                "merge origin/main",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((1, "CONFLICT (content): Merge conflict in file.txt", ""));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);

        var processor = new MergeRequestProcessor(
            _versionControlMock.Object,
            workflowEngine,
            _httpWrapper,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        // Verify git fetch and git merge were called
        _commandServiceMock.Verify(c => c.RunCommandAsync("git", "fetch origin main", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()), Times.Once);
        _commandServiceMock.Verify(c => c.RunCommandAsync("git", "merge origin/main", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_AiFails_DoesNotCommit()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };

        var gitLabMr = new GitLabMergeRequestDto
        {
            Iid = 1,
            Title = "Test MR",
            ProjectId = 101,
            SourceProjectId = 101,
            SourceBranch = "fix-bug",
            TargetBranch = "main",
            Author = new GitLabUser { Username = "bot-user", Id = 123 }
        };
        _gitLabMrList = new List<GitLabMergeRequestDto> { gitLabMr };

        var detailedMr = JsonSerializer.Deserialize<DetailedMergeRequest>(@"
        {
            ""HasConflicts"": false,
            ""MrPipeline"": { ""Status"": ""failed"", ""Id"": 555, ""WebUrl"": ""http://gitlab.com/pipeline/555"" }
        }", _jsonOptions)!;

        var repository = new Repository
        {
            CloneUrl = "https://gitlab.com/bot-user/repo.git",
            Name = "repo",
            Owner = new User { Login = "bot-user" }
        };

        _versionControlMock
            .Setup(v => v.GetMergeRequestDetails(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(detailedMr);

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);

        _versionControlMock
            .Setup(v => v.GetPipelineJobs(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<PipelineJob>());

        // MOCK GEMINI FAILURE
        _aiCliServiceMock
            .Setup(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((false, "AI failed", "Error message"));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);

        var processor = new MergeRequestProcessor(
            _versionControlMock.Object,
            workflowEngine,
            _httpWrapper,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success); // Processor itself succeeds because it handles individual MR failures

        // Verify NO commit and NO push happened
        _workspaceManagerMock.Verify(w => w.CommitToBranch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _workspaceManagerMock.Verify(w => w.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
