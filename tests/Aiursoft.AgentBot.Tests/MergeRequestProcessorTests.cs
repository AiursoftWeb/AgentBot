using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
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
    private StubVersionControlService _versionControlMock = null!;
    private StubWorkspaceManager _workspaceManagerMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private MergeRequestDiscussionService _discussionService = null!;
    private StubAiCliService _aiCliServiceMock = null!;
    private StubCommandService _commandServiceMock = null!;
    private ILogger<MergeRequestProcessor> _loggerMock = null!;
    private IOptions<AgentBotOptions> _options = null!;
    private List<GitLabMergeRequestDto> _gitLabMrList = new();
    private List<GitLabDiscussion> _gitLabDiscussions = new();
    private readonly List<string> _postedNotes = [];
    private readonly List<string> _postedNoteUrls = [];
    private bool _failDiscussions;
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

        _commandServiceMock = new StubCommandService();

        _versionControlMock = new StubVersionControlService();

        _workspaceManagerMock = new StubWorkspaceManager();

        _aiCliServiceMock = new StubAiCliService(
            _commandServiceMock,
            _options,
            NullLogger<AiCliService>.Instance);

        _loggerMock = NullLogger<MergeRequestProcessor>.Instance;

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
                if (_failDiscussions) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
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
                if (_gitLabDiscussions.Count == 0) _gitLabDiscussions.Add(new GitLabDiscussion());
                if (_gitLabDiscussions.Count > 0)
                {
                    _gitLabDiscussions[0].Notes = _gitLabDiscussions[0].Notes.Append(new GitLabNote
                    {
                        Id = _gitLabDiscussions.SelectMany(discussion => discussion.Notes).Select(note => note.Id).DefaultIfEmpty(0).Max() + 1,
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
        _httpWrapper = new HttpWrapper(NullLogger<HttpWrapper>.Instance, client);
        var httpClientFactory = new StubHttpClientFactory();
        httpClientFactory.Create = () => new HttpClient(handler, disposeHandler: false);
        _discussionService = new MergeRequestDiscussionService(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            httpClientFactory,
            _options,
            NullLogger<MergeRequestDiscussionService>.Instance);
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

        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(new Repository
            {
                Name = "repo",
                CloneUrl = "https://gitlab.com/bot-user/repo.git",
                DefaultBranch = "main"
            });
        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(new DetailedMergeRequest());
        _aiCliServiceMock.InvokePlanningCliAsyncHandler = (_, _) => Task.FromResult((true,
                "{\"action\":\"dismiss_finding\",\"addressed_note_ids\":[11],\"response_markdown\":\"明白，这个场景不纳入本次修改。\",\"implementation_brief\":null}",
                string.Empty));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);
        var processor = new MergeRequestProcessor(
            _versionControlMock,
            workflowEngine,
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock);

        var firstResult = await processor.ProcessMergeRequestsAsync(server);
        var secondResult = await processor.ProcessMergeRequestsAsync(server);

        Assert.IsTrue(firstResult.Success);
        Assert.IsTrue(secondResult.Success, secondResult.Error?.ToString() ?? secondResult.Message);
        Assert.HasCount(1, _postedNotes);
        StringAssert.Contains(_postedNotes[0], "agentbot:mr-discussion:v1:through-note-11");
        StringAssert.EndsWith(_postedNoteUrls[0], "/discussions/discussion-abc/notes");
        Assert.AreEqual(1, _aiCliServiceMock.InvokePlanningCliAsyncCalls.Count(call => (call.taskDescription.Contains("不考虑这个情况", StringComparison.Ordinal))));
        Assert.AreEqual(0, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count);
        Assert.AreEqual(0, _workspaceManagerMock.CommitToBranchCalls.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProcessMergeRequestsAsync_ExplicitImplementationRequest_UsesAcceptedBrief(bool automaticBudgetExhausted)
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
        if (automaticBudgetExhausted)
            _gitLabDiscussions.Add(new GitLabDiscussion { Notes = [new GitLabNote
            {
                Id = 1, Author = new GitLabUser { Username = "bot-user" },
                Body = MergeRequestAutomationBudget.Marker("repair", 2)
            }] });
        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(new Repository
            {
                Name = "repo",
                CloneUrl = "https://gitlab.com/bot-user/repo.git",
                DefaultBranch = "main"
            });
        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(new DetailedMergeRequest());
        _aiCliServiceMock.InvokePlanningCliAsyncHandler = (_, _) => Task.FromResult((true,
                "{\"action\":\"implement_feedback\",\"addressed_note_ids\":[21],\"response_markdown\":\"好的，我会增加该配置。\",\"implementation_brief\":\"Add BOT_TIMEOUT_SECONDS with validation and tests.\"}",
                string.Empty));
        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((true, "Implemented", string.Empty));
        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(true);
        _workspaceManagerMock.CommitToBranchHandler = (_, _, _) => Task.FromResult(true);
        _commandServiceMock.RunCommandAsyncHandler = (_, _, _, _, _, _) => Task.FromResult((0, "1", string.Empty));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);
        var processor = new MergeRequestProcessor(
            _versionControlMock,
            workflowEngine,
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock);

        var result = await processor.ProcessMergeRequestsAsync(server);

        Assert.IsTrue(result.Success);
        Assert.HasCount(1, _postedNotes);
        Assert.AreEqual(1, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count(call => (call.taskDescription.Contains(
                "Add BOT_TIMEOUT_SECONDS with validation and tests.", StringComparison.Ordinal))));
        const string expectedCommitMessage = """
            Address MR #1 review: Add BOT_TIMEOUT_SECONDS with validation and tests.

            MR: Bot-owned MR
            Resolved review request:
            Add BOT_TIMEOUT_SECONDS with validation and tests.

            Automatically generated fix by Agent Bot.
            """;
        Assert.AreEqual(1, _workspaceManagerMock.CommitToBranchCalls.Count(call => (call.message == expectedCommitMessage) && (call.branch == "feature")));
    }

    [TestMethod]
    public void BuildReviewCommitDetails_MultilineBrief_NormalizesSummaryAndPreservesTrimmedBody()
    {
        var mr = new MergeRequestSearchResult
        {
            IID = 18,
            Title = "Improve generated commit messages"
        };
        var decision = new MergeRequestDiscussionDecision(
            MergeRequestDiscussionAction.ImplementFeedback,
            "Please implement this feedback.",
            "  \r\n\tAdd   review\tcontext to the commit subject.  \r\nInclude the MR title and full brief.  \r\n\r\n",
            21,
            "discussion-abc");

        var (implementationBrief, commitMessage) = MergeRequestProcessor.BuildReviewCommitDetails(mr, decision);

        const string expectedBrief = "Add   review\tcontext to the commit subject.  \r\nInclude the MR title and full brief.";
        var expectedCommitMessage = $"""
            Address MR #18 review: Add review context to the commit subject.

            MR: Improve generated commit messages
            Resolved review request:
            {expectedBrief}

            Automatically generated fix by Agent Bot.
            """;
        Assert.AreEqual(expectedBrief, implementationBrief);
        Assert.AreEqual(expectedCommitMessage, commitMessage);
    }

    [TestMethod]
    public void BuildReviewCommitDetails_WhitespaceBrief_ThrowsClearError()
    {
        var mr = new MergeRequestSearchResult { IID = 18, Title = "Test MR" };
        var decision = new MergeRequestDiscussionDecision(
            MergeRequestDiscussionAction.ImplementFeedback,
            "Please implement this feedback.",
            " \r\n\t ",
            21,
            "discussion-abc");

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => MergeRequestProcessor.BuildReviewCommitDetails(mr, decision));

        Assert.AreEqual("Accepted MR feedback has no implementation brief.", exception.Message);
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

        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(detailedMr);

        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(repository);

        _versionControlMock.GetPipelineJobsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyCollection<PipelineJob>>(new List<PipelineJob> { failedJob });

        _versionControlMock.GetJobLogHandler = (_, _, _, _) => Task.FromResult("Build failed at line 42");

        _versionControlMock.RepoExistsHandler = (_, _, _, _) => Task.FromResult(true);

        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((true, "AI output", ""));

        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(true);

        _workspaceManagerMock.CommitToBranchHandler = (_, _, _) => Task.FromResult(true);

        _commandServiceMock.RunCommandAsyncHandler = (_, _, _, _, _, _) => Task.FromResult((0, "1", ""));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);

        var processor = new MergeRequestProcessor(
            _versionControlMock,
            workflowEngine,
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock);


        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        // Verify new MR creation
        Assert.AreEqual(1, _versionControlMock.CreatePullRequestCalls.Count(call => (call.title.Contains("[Bot 修复]", StringComparison.Ordinal)) && (call.body.Contains("## 修改内容", StringComparison.Ordinal))));
        Assert.AreEqual(1, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count(call => (call.taskDescription.Contains("Simplified Chinese", StringComparison.Ordinal))));
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

        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(detailedMr);

        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(repository);

        _versionControlMock.GetPipelineJobsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyCollection<PipelineJob>>(new List<PipelineJob> { failedJob });

        _versionControlMock.GetJobLogHandler = (_, _, _, _) => Task.FromResult("Build failed");

        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((true, "AI output", ""));

        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(true);

        _workspaceManagerMock.CommitToBranchHandler = (_, _, _) => Task.FromResult(true);

        _commandServiceMock.RunCommandAsyncHandler = (_, _, _, _, _, _) => Task.FromResult((0, "1", ""));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);

        var processor = new MergeRequestProcessor(
            _versionControlMock,
            workflowEngine,
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        // Verify push to original branch
        Assert.AreEqual(1, _workspaceManagerMock.PushCalls.Count(call => (call.force)));

        // Verify NO new MR was created
        Assert.AreEqual(0, _versionControlMock.CreatePullRequestCalls.Count);
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

        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(detailedMr);

        _versionControlMock.GetRepositoryHandler = (_, org, _, _) => Task.FromResult(org switch
        {
            "101" => targetRepository,
            "102" => sourceRepository,
            _ => null!
        });

        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((true, "AI output", ""));

        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(true);

        _workspaceManagerMock.CommitToBranchHandler = (_, _, _) => Task.FromResult(true);

        var conflictedFiles = new Queue<string>(["file.txt", ""]);
        _commandServiceMock.RunCommandAsyncHandler = (bin, arg, _, _, _, _) =>
            Task.FromResult((bin, arg) switch
            {
                (_, "rev-list --count HEAD ^origin/conflict-branch") => (0, "1", ""),
                ("git", "fetch --no-tags https://gitlab.com/target-group/repo.git +refs/heads/main:refs/remotes/agentbot-target/main") => (0, "", ""),
                ("git", "merge refs/remotes/agentbot-target/main") => (1, "CONFLICT (content): Merge conflict in file.txt", ""),
                ("git", "diff --name-only --diff-filter=U") => (0, conflictedFiles.Dequeue(), ""),
                ("git", "rev-parse -q --verify MERGE_HEAD") => (1, "", ""),
                ("git", "merge-base --is-ancestor refs/remotes/agentbot-target/main HEAD") => (0, "", ""),
                _ => default
            });

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);

        var processor = new MergeRequestProcessor(
            _versionControlMock,
            workflowEngine,
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);

        Assert.AreEqual(1, _commandServiceMock.RunCommandAsyncCalls.Count(call => (call.bin == "git") && (call.arg == "fetch --no-tags https://gitlab.com/target-group/repo.git +refs/heads/main:refs/remotes/agentbot-target/main")));
        Assert.AreEqual(1, _commandServiceMock.RunCommandAsyncCalls.Count(call => (call.bin == "git") && (call.arg == "merge refs/remotes/agentbot-target/main")));
        Assert.AreEqual(1, _workspaceManagerMock.PushCalls.Count(call => (call.branch == "conflict-branch") && (call.force)));
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
        _versionControlMock.GetRepositoryHandler = (_, org, _, _) =>
            Task.FromResult(org == "102" ? repository : null!);
        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(true);
        _workspaceManagerMock.CommitToBranchHandler = (_, _, _) => Task.FromResult(true);
        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((true, "attempted resolution", ""));
        _commandServiceMock.RunCommandAsyncHandler = (bin, arg, _, _, _, _) =>
            Task.FromResult((bin, arg) switch
            {
                ("git", "merge refs/remotes/agentbot-target/main") => (1, "CONFLICT", ""),
                ("git", "diff --name-only --diff-filter=U") => (0, "still-conflicted.cs", ""),
                _ => (0, "", "")
            });

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock, _workspaceManagerMock, _aiCliServiceMock,
            _commandServiceMock, _options, NullLogger<BotWorkflowEngine>.Instance);

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
        Assert.AreEqual(0, _workspaceManagerMock.PushCalls.Count);
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

        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(detailedMr);

        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(repository);

        _versionControlMock.GetPipelineJobsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyCollection<PipelineJob>>(new List<PipelineJob>());

        // Mock AI engine failure.
        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((false, "AI failed", "Error message"));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);

        var processor = new MergeRequestProcessor(
            _versionControlMock,
            workflowEngine,
            _discussionService,
            _httpWrapper,
            _options,
            _loggerMock);

        // Act
        var result = await processor.ProcessMergeRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success); // Processor itself succeeds because it handles individual MR failures

        // Verify NO commit and NO push happened
        Assert.AreEqual(0, _workspaceManagerMock.CommitToBranchCalls.Count);
        Assert.AreEqual(0, _workspaceManagerMock.PushCalls.Count);
    }
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task PersistentFailureStopsAfterTwoAutomaticAttempts(bool discussionsUnavailable, bool aiSucceeds)
    {
        _failDiscussions = discussionsUnavailable;
        var server = new Server { Provider = "GitLab", UserName = "bot-user", Token = "token", EndPoint = "https://gitlab.com" };
        _gitLabMrList = [new GitLabMergeRequestDto
        {
            Iid = 1, ProjectId = 101, SourceProjectId = 101, SourceBranch = "feature", TargetBranch = "main",
            Author = new GitLabUser { Username = "bot-user" }
        }];
        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(new Repository { Name = "repo", CloneUrl = "https://gitlab.com/repo.git" });
        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(JsonSerializer.Deserialize<DetailedMergeRequest>("{\"MrPipeline\":{\"Id\":123,\"Status\":\"failed\"}}", _jsonOptions)!);
        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, prompt, _) =>
        {
            StringAssert.Contains(prompt, "Preserve Template symmetry");
            return Task.FromResult((aiSucceeds, "No in-scope code change can fix this external failure.", ""));
        };
        for (var n = 0; n < 5; n++)
        {
            var engine = new BotWorkflowEngine(_versionControlMock, _workspaceManagerMock, _aiCliServiceMock,
                _commandServiceMock, _options, NullLogger<BotWorkflowEngine>.Instance);
            await new MergeRequestProcessor(_versionControlMock, engine, _discussionService, _httpWrapper, _options, _loggerMock).ProcessMergeRequestsAsync(server);
        }
        Assert.AreEqual(discussionsUnavailable ? 0 : 2, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count);
        Assert.AreEqual(discussionsUnavailable ? 0 : 1, _postedNotes.Count(note => note.Contains("agentbot:mr-automation:v1:repair-limit")));
        Assert.IsEmpty(_workspaceManagerMock.PushCalls);
    }

}
