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
    private MergeRequestDiscussionService _discussionService = null!;
    private Mock<AiCliService> _aiCliServiceMock = null!;
    private Mock<IAiCommandService> _commandServiceMock = null!;
    private Mock<ILogger<MergeRequestProcessor>> _loggerMock = null!;
    private IOptions<AgentBotOptions> _options = null!;
    private List<GitLabMergeRequestDto> _gitLabMrList = new();
    private List<GitLabDiscussion> _gitLabDiscussions = new();
    private readonly List<string> _postedNotes = [];
    private readonly List<string> _postedNoteUrls = [];
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
                    Content = new StringContent(JsonSerializer.Serialize(_gitLabDiscussions))
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
            if (req.Method == HttpMethod.Post && url.Contains("merge_requests") && url.Contains("notes"))
            {
                var json = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var body = JsonDocument.Parse(json).RootElement.GetProperty("body").GetString()!;
                _postedNotes.Add(body);
                _postedNoteUrls.Add(url);
                if (_gitLabDiscussions.Count > 0)
                {
                    _gitLabDiscussions[0].Notes = _gitLabDiscussions[0].Notes.Append(new GitLabNote
                    {
                        Id = _gitLabDiscussions.SelectMany(discussion => discussion.Notes).Max(note => note.Id) + 1,
                        Body = body,
                        Author = new GitLabUser { Username = "bot-user" },
                        Created_at = DateTime.UtcNow
                    }).ToList();
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
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
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        _discussionService = new MergeRequestDiscussionService(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            httpClientFactory.Object,
            _options,
            new Mock<ILogger<MergeRequestDiscussionService>>().Object);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_HumanDismissesFinding_RepliesOnceWithoutChangingCode()
    {
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };
        _gitLabMrList =
        [
            new GitLabMergeRequestDto
            {
                Iid = 1,
                Title = "Bot-owned MR",
                ProjectId = 101,
                SourceProjectId = 101,
                SourceBranch = "feature",
                TargetBranch = "main",
                Author = new GitLabUser { Username = "bot-user" }
            }
        ];
        _gitLabDiscussions =
        [
            new GitLabDiscussion
            {
                Id = "discussion-abc",
                Notes =
                [
                    new GitLabNote
                    {
                        Id = 10,
                        Body = "This edge case should be handled.",
                        Author = new GitLabUser { Username = "bot-user" },
                        Created_at = DateTime.UtcNow.AddMinutes(-2)
                    },
                    new GitLabNote
                    {
                        Id = 11,
                        Body = "不考虑这个情况。",
                        Author = new GitLabUser { Username = "owner" },
                        Created_at = DateTime.UtcNow.AddMinutes(-1)
                    }
                ]
            }
        ];

        _versionControlMock
            .Setup(service => service.GetRepository(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new Repository
            {
                Name = "repo",
                CloneUrl = "https://gitlab.com/bot-user/repo.git",
                DefaultBranch = "main"
            });
        _versionControlMock
            .Setup(service => service.GetMergeRequestDetails(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new DetailedMergeRequest());
        _aiCliServiceMock
            .Setup(service => service.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((true,
                "{\"action\":\"dismiss_finding\",\"addressed_note_ids\":[11],\"response_markdown\":\"明白，这个场景不纳入本次修改。\",\"implementation_brief\":null}",
                string.Empty));

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
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock.Object);

        var firstResult = await processor.ProcessMergeRequestsAsync(server);
        var secondResult = await processor.ProcessMergeRequestsAsync(server);

        Assert.IsTrue(firstResult.Success);
        Assert.IsTrue(secondResult.Success, secondResult.Error?.ToString() ?? secondResult.Message);
        Assert.HasCount(1, _postedNotes);
        StringAssert.Contains(_postedNotes[0], "agentbot:mr-discussion:v1:through-note-11");
        StringAssert.EndsWith(_postedNoteUrls[0], "/discussions/discussion-abc/notes");
        _aiCliServiceMock.Verify(service => service.InvokePlanningCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt => prompt.Contains("不考虑这个情况", StringComparison.Ordinal))), Times.Once);
        _aiCliServiceMock.Verify(service => service.InvokeAiCliAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        _workspaceManagerMock.Verify(manager => manager.CommitToBranch(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_ExplicitImplementationRequest_UsesAcceptedBrief()
    {
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };
        _gitLabMrList =
        [
            new GitLabMergeRequestDto
            {
                Iid = 1,
                Title = "Bot-owned MR",
                ProjectId = 101,
                SourceProjectId = 101,
                SourceBranch = "feature",
                TargetBranch = "main",
                Author = new GitLabUser { Username = "bot-user" }
            }
        ];
        _gitLabDiscussions =
        [
            new GitLabDiscussion
            {
                Notes =
                [
                    new GitLabNote
                    {
                        Id = 20,
                        Body = "Should the timeout be configurable?",
                        Author = new GitLabUser { Username = "bot-user" },
                        Created_at = DateTime.UtcNow.AddMinutes(-2)
                    },
                    new GitLabNote
                    {
                        Id = 21,
                        Body = "Yes, add a BOT_TIMEOUT_SECONDS setting.",
                        Author = new GitLabUser { Username = "owner" },
                        Created_at = DateTime.UtcNow.AddMinutes(-1)
                    }
                ]
            }
        ];
        _versionControlMock
            .Setup(service => service.GetRepository(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new Repository
            {
                Name = "repo",
                CloneUrl = "https://gitlab.com/bot-user/repo.git",
                DefaultBranch = "main"
            });
        _versionControlMock
            .Setup(service => service.GetMergeRequestDetails(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new DetailedMergeRequest());
        _aiCliServiceMock
            .Setup(service => service.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((true,
                "{\"action\":\"implement_feedback\",\"addressed_note_ids\":[21],\"response_markdown\":\"好的，我会增加该配置。\",\"implementation_brief\":\"Add BOT_TIMEOUT_SECONDS with validation and tests.\"}",
                string.Empty));
        _aiCliServiceMock
            .Setup(service => service.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((true, "Implemented", string.Empty));
        _workspaceManagerMock.Setup(manager => manager.PendingCommit(It.IsAny<string>())).ReturnsAsync(true);
        _workspaceManagerMock
            .Setup(manager => manager.CommitToBranch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _commandServiceMock
            .Setup(service => service.RunCommandAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "1", string.Empty));

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
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock.Object);

        var result = await processor.ProcessMergeRequestsAsync(server);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, _postedNotes);
        _aiCliServiceMock.Verify(service => service.InvokeAiCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt => prompt.Contains(
                "Add BOT_TIMEOUT_SECONDS with validation and tests.", StringComparison.Ordinal)),
            It.IsAny<bool>()), Times.Once);
        _workspaceManagerMock.Verify(manager => manager.CommitToBranch(
            It.IsAny<string>(), It.IsAny<string>(), "feature"), Times.Once);
    }

    [TestMethod]
    public async Task ProcessMergeRequestsAsync_OthersMr_ForksAndCreatesNewMr()
    {
        // Arrange
        _options.Value.ReplyLanguage = ReplyLanguage.Zh;
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
            _discussionService,
            _httpWrapper,
            _options,
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
            It.Is<string>(title => title.Contains("[Bot 修复]", StringComparison.Ordinal)),
            It.Is<string>(body => body.Contains("## 修改内容", StringComparison.Ordinal)),
            It.IsAny<string>()), Times.Once);
        _aiCliServiceMock.Verify(g => g.InvokeAiCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt => prompt.Contains("Simplified Chinese", StringComparison.Ordinal)),
            It.IsAny<bool>()), Times.Once);
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
            _discussionService,
            _httpWrapper,
            _options,
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
            SourceProjectId = 102,
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

        var sourceRepository = new Repository
        {
            CloneUrl = "https://gitlab.com/bot-user/repo.git",
            Name = "repo",
            Owner = new User { Login = "bot-user" }
        };
        var targetRepository = new Repository
        {
            CloneUrl = "https://gitlab.com/target-group/repo.git",
            Name = "repo",
            Owner = new User { Login = "target-group" }
        };

        _versionControlMock
            .Setup(v => v.GetMergeRequestDetails(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(detailedMr);

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), "101", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(targetRepository);

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), "102", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(sourceRepository);

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

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                "git",
                "fetch --no-tags https://gitlab.com/target-group/repo.git +refs/heads/main:refs/remotes/agentbot-target/main",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "", ""));

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                "git",
                "merge refs/remotes/agentbot-target/main",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((1, "CONFLICT (content): Merge conflict in file.txt", ""));

        _commandServiceMock
            .SetupSequence(c => c.RunCommandAsync(
                "git",
                "diff --name-only --diff-filter=U",
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<bool>(),
                It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "file.txt", ""))
            .ReturnsAsync((0, "", ""));

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                "git", "rev-parse -q --verify MERGE_HEAD", It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((1, "", ""));

        _commandServiceMock
            .Setup(c => c.RunCommandAsync(
                "git", "merge-base --is-ancestor refs/remotes/agentbot-target/main HEAD", It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "", ""));

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
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        _commandServiceMock.Verify(c => c.RunCommandAsync("git", "fetch --no-tags https://gitlab.com/target-group/repo.git +refs/heads/main:refs/remotes/agentbot-target/main", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()), Times.Once);
        _commandServiceMock.Verify(c => c.RunCommandAsync("git", "merge refs/remotes/agentbot-target/main", It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()), Times.Once);
        _workspaceManagerMock.Verify(w => w.Push(It.IsAny<string>(), "conflict-branch", It.IsAny<string>(), true), Times.Once);
    }

    [TestMethod]
    public async Task ConflictResolution_WithUnmergedFiles_DoesNotPush()
    {
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com",
            DisplayName = "Bot",
            UserEmail = "bot@aiursoft.com"
        };
        var repository = new Repository
        {
            CloneUrl = "https://gitlab.com/bot-user/repo.git",
            Name = "repo",
            Owner = new User { Login = "bot-user" }
        };
        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), "102", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);
        _workspaceManagerMock.Setup(w => w.PendingCommit(It.IsAny<string>())).ReturnsAsync(true);
        _workspaceManagerMock.Setup(w => w.CommitToBranch(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _aiCliServiceMock.Setup(a => a.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((true, "attempted resolution", ""));
        _commandServiceMock
            .Setup(c => c.RunCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "", ""));
        _commandServiceMock
            .Setup(c => c.RunCommandAsync("git", "merge refs/remotes/agentbot-target/main", It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((1, "CONFLICT", ""));
        _commandServiceMock
            .Setup(c => c.RunCommandAsync("git", "diff --name-only --diff-filter=U", It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, string?>>()))
            .ReturnsAsync((0, "still-conflicted.cs", ""));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object, _workspaceManagerMock.Object, _aiCliServiceMock.Object,
            _commandServiceMock.Object, _options, new Mock<ILogger<BotWorkflowEngine>>().Object);

        var result = await workflowEngine.ExecuteAsync(new WorkflowContext
        {
            Server = server,
            ProjectId = "102",
            SourceBranch = "conflict-branch",
            TargetBranch = "main",
            TargetRepositoryCloneUrl = "https://gitlab.com/target-group/repo.git",
            WorkspaceName = "conflict-validation",
            Prompt = "Resolve conflicts",
            CommitMessage = "Resolve conflicts",
            PushBranch = "conflict-branch",
            NeedResolveConflicts = true
        });

        Assert.IsFalse(result.Result.Success);
        _workspaceManagerMock.Verify(w => w.Push(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>()), Times.Never);
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

        // Mock AI engine failure.
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
            _discussionService,
            _httpWrapper,
            _options,
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
