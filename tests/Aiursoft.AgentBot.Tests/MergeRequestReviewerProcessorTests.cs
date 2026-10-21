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

[TestClass]
public class MergeRequestReviewerProcessorTests
{
    private StubVersionControlService _versionControlMock = null!;
    private StubWorkspaceManager _workspaceManagerMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private StubHttpClientFactory _httpClientFactoryMock = null!;
    private StubAiCliService _aiCliServiceMock = null!;
    private StubCommandService _commandServiceMock = null!;
    private ILogger<MergeRequestReviewerProcessor> _loggerMock = null!;
    private IOptions<AgentBotOptions> _options = null!;
    private List<GitLabMergeRequestDto> _gitLabMrList = new();
    private List<GitLabCommit> _commitsList = new();
    private List<GitLabDiscussion> _discussionsList = new();
    private readonly List<string> _postedNotes = [];

    [TestInitialize]
    public void SetUp()
    {
        var options = new AgentBotOptions
        {
            WorkspaceFolder = Path.Combine(Path.GetTempPath(), "AgentBotReviewTests"),
            ForkWaitDelayMs = 0,
            ReplyLanguage = ReplyLanguage.Zh
        };
        _options = Options.Create(options);

        _commandServiceMock = new StubCommandService
        {
            RunCommandAsyncHandler = (_, arg, _, _, _, _) => Task.FromResult((0,
                arg == "rev-parse HEAD" ? (_gitLabMrList.FirstOrDefault()?.Sha is { Length: > 0 } sha ? sha : _commitsList.FirstOrDefault()?.Id ?? "") : "", ""))
        };
        _versionControlMock = new StubVersionControlService();
        _workspaceManagerMock = new StubWorkspaceManager();
        _workspaceManagerMock.ResetRepoHandler = (path, _, _, _, _) =>
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        };
        _aiCliServiceMock = new StubAiCliService(
            _commandServiceMock,
            _options,
            NullLogger<AiCliService>.Instance);
        _loggerMock = NullLogger<MergeRequestReviewerProcessor>.Instance;

        var handler = new FakeHttpMessageHandler((req) =>
        {
            var url = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Get && url.Contains("merge_requests") && url.Contains("scope=reviews_for_me"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_gitLabMrList))
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("commits"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_commitsList))
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("discussions"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_discussionsList))
                });
            }
            if (req.Method == HttpMethod.Post && url.Contains("notes"))
            {
                var json = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var body = JsonDocument.Parse(json).RootElement.GetProperty("body").GetString()!;
                _postedNotes.Add(body);
                _discussionsList.Add(new GitLabDiscussion { Notes = [new GitLabNote
                {
                    Id = 1000 + _postedNotes.Count, Body = body,
                    Author = new GitLabUser { Username = "bot-user" }, Created_at = DateTime.UtcNow
                }] });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(handler);
        _httpWrapper = new HttpWrapper(NullLogger<HttpWrapper>.Instance, client);
        _httpClientFactoryMock = new StubHttpClientFactory();
        _httpClientFactoryMock.Create = () => new HttpClient(handler, disposeHandler: false);
    }

    [TestMethod]
    public async Task ProcessReviewRequestsAsync_NewCommitSha_CallsAiAndPostsMarker()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };

        _gitLabMrList = new List<GitLabMergeRequestDto>
        {
            new GitLabMergeRequestDto
            {
                Iid = 1,
                Title = "Review Me",
                ProjectId = 101,
                SourceBranch = "feature",
                TargetBranch = "main",
                Author = new GitLabUser { Username = "human" }
            }
        };

        _commitsList = new List<GitLabCommit>
        {
            new GitLabCommit
            {
                Id = "0123456789abcdef0123456789abcdef01234567",
                Message = "latest commit",
                Created_at = DateTime.UtcNow
            }
        };

        _discussionsList =
        [
            new GitLabDiscussion
            {
                Notes =
                [
                    new GitLabNote
                    {
                        Id = 9,
                        Body = "Previous review.\n\n<!-- agentbot:mr-review:commit-1111111111111111111111111111111111111111 -->",
                        Author = new GitLabUser { Username = "bot-user" },
                        // Deliberately newer than the commit: SHA, not timestamps, must drive the decision.
                        Created_at = DateTime.UtcNow.AddMinutes(1)
                    }
                ]
            }
        ];

        var repository = new Repository
        {
            CloneUrl = "https://gitlab.com/human/repo.git",
            Name = "repo"
        };

        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(repository);

        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(new DetailedMergeRequest());

        _aiCliServiceMock.InvokeAiCliAsyncHandler = (workPath, _, _) =>
        {
            File.WriteAllText(Path.Combine(workPath, "review.md"), "This is a great MR!");
            return Task.FromResult((true, "AI reviewed", ""));
        };

        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(false); // No changes made by bot

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);

        var processor = new MergeRequestReviewerProcessor(
            _versionControlMock,
            workflowEngine,
            new MergeRequestDiscussionService(
                _versionControlMock,
                _workspaceManagerMock,
                _aiCliServiceMock,
                _httpClientFactoryMock,
                _options,
                NullLogger<MergeRequestDiscussionService>.Instance),
            _httpWrapper,
            _httpClientFactoryMock,
            _options,
            _loggerMock);

        // Act
        var result = await processor.ProcessReviewRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count(call => (call.taskDescription.Contains("code reviewer") && call.taskDescription.Contains("Simplified Chinese"))));
        Assert.HasCount(2, _postedNotes);
        StringAssert.Contains(
            _postedNotes.Last(),
            "<!-- agentbot:mr-review:commit-0123456789abcdef0123456789abcdef01234567 -->");
    }

    [TestMethod]
    public async Task ProcessReviewRequestsAsync_CommitShaAlreadyMarked_DoesNotReviewAgain()
    {
        const string commitSha = "abcdef0123456789abcdef0123456789abcdef01";
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };
        _gitLabMrList =
        [
            new GitLabMergeRequestDto
            {
                Iid = 1,
                Title = "Already reviewed",
                ProjectId = 101,
                SourceProjectId = 101,
                SourceBranch = "feature",
                TargetBranch = "main",
                Author = new GitLabUser { Username = "bot-user" }
            }
        ];
        _commitsList =
        [
            new GitLabCommit
            {
                Id = commitSha,
                Message = "latest commit",
                Created_at = DateTime.UtcNow.AddMinutes(-2)
            }
        ];
        _discussionsList =
        [
            new GitLabDiscussion
            {
                Notes =
                [
                    new GitLabNote
                    {
                        Id = 10,
                        Body = $"Looks good.\n\n<!-- agentbot:mr-review:commit-{commitSha} -->",
                        Author = new GitLabUser { Username = "bot-user" },
                        Created_at = DateTime.UtcNow.AddMinutes(-1)
                    }
                ]
            }
        ];

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);
        var processor = new MergeRequestReviewerProcessor(
            _versionControlMock,
            workflowEngine,
            new MergeRequestDiscussionService(
                _versionControlMock,
                _workspaceManagerMock,
                _aiCliServiceMock,
                _httpClientFactoryMock,
                _options,
                NullLogger<MergeRequestDiscussionService>.Instance),
            _httpWrapper,
            _httpClientFactoryMock,
            _options,
            _loggerMock);

        var result = await processor.ProcessReviewRequestsAsync(server);

        Assert.IsTrue(result.Success);
        Assert.IsEmpty(_postedNotes);
        Assert.AreEqual(0, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count);
    }

    [TestMethod]
    public async Task ProcessReviewRequestsAsync_ImplementationRequestInReviewOnlyRole_DoesNotEdit()
    {
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };
        _gitLabMrList =
        [
            new GitLabMergeRequestDto
            {
                Iid = 1,
                Title = "Discuss review",
                ProjectId = 101,
                SourceProjectId = 101,
                SourceBranch = "feature",
                TargetBranch = "main",
                Author = new GitLabUser { Username = "bot-user" }
            }
        ];
        _commitsList =
        [
            new GitLabCommit { Message = "old commit", Created_at = DateTime.UtcNow.AddMinutes(-3) }
        ];
        _discussionsList =
        [
            new GitLabDiscussion
            {
                Notes =
                [
                    new GitLabNote
                    {
                        Id = 10,
                        Body = "Please handle this edge case.",
                        Author = new GitLabUser { Username = "bot-user" },
                        Created_at = DateTime.UtcNow.AddMinutes(-2)
                    },
                    new GitLabNote
                    {
                        Id = 11,
                        Body = "请直接修改这个问题。",
                        Author = new GitLabUser { Username = "owner" },
                        Created_at = DateTime.UtcNow.AddMinutes(-1)
                    }
                ]
            }
        ];
        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(new Repository
            {
                CloneUrl = "https://gitlab.com/bot-user/repo.git",
                Name = "repo"
            });
        _aiCliServiceMock.InvokePlanningCliAsyncHandler = (_, _) => Task.FromResult((true,
                "{\"action\":\"implement_feedback\",\"addressed_note_ids\":[11],\"response_markdown\":\"我来修改。\",\"implementation_brief\":\"Handle the edge case.\"}",
                string.Empty));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            NullLogger<BotWorkflowEngine>.Instance);
        var processor = new MergeRequestReviewerProcessor(
            _versionControlMock,
            workflowEngine,
            new MergeRequestDiscussionService(
                _versionControlMock,
                _workspaceManagerMock,
                _aiCliServiceMock,
                _httpClientFactoryMock,
                _options,
                NullLogger<MergeRequestDiscussionService>.Instance),
            _httpWrapper,
            _httpClientFactoryMock,
            _options,
            _loggerMock);

        var result = await processor.ProcessReviewRequestsAsync(server);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, _aiCliServiceMock.InvokePlanningCliAsyncCalls.Count(call => (call.taskDescription.Contains("Implementation authority for this invocation: not allowed"))));
        Assert.AreEqual(0, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count);
    }
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task ReviewBudgetSurvivesNewHeadsAndProcessorRestarts(bool writesReview, bool aiSucceeds)
    {
        var server = new Server { Provider = "GitLab", UserName = "bot-user", Token = "token", EndPoint = "https://gitlab.com" };
        _gitLabMrList = [new GitLabMergeRequestDto
        {
            Iid = 1, ProjectId = 101, SourceProjectId = 101, SourceBranch = "feature", TargetBranch = "main",
            Author = new GitLabUser { Username = "bot-user" }
        }];
        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(new Repository
        {
            Name = "repo", CloneUrl = "https://gitlab.com/actual-target/repo.git"
        });
        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(new DetailedMergeRequest());
        _aiCliServiceMock.InvokeAiCliAsyncHandler = (path, prompt, _) =>
        {
            StringAssert.Contains(prompt, "P0/P1");
            StringAssert.Contains(prompt, "explicitly recommend merging");
            StringAssert.Contains(prompt, "refs/remotes/agentbot-target/main");
            if (writesReview) File.WriteAllText(Path.Combine(path, "review.md"), "No major blockers; recommend merging after CI.");
            return Task.FromResult((aiSucceeds, "", ""));
        };
        for (var n = 0; n < 5; n++)
        {
            _gitLabMrList[0].Sha = n.ToString("x40");
            _commitsList = [new GitLabCommit { Id = new string('f', 40), Created_at = DateTime.UtcNow.AddDays(1) }];
            var engine = new BotWorkflowEngine(_versionControlMock, _workspaceManagerMock, _aiCliServiceMock,
                _commandServiceMock, _options, NullLogger<BotWorkflowEngine>.Instance);
            var discussion = new MergeRequestDiscussionService(_versionControlMock, _workspaceManagerMock, _aiCliServiceMock,
                _httpClientFactoryMock, _options, NullLogger<MergeRequestDiscussionService>.Instance);
            var processor = new MergeRequestReviewerProcessor(_versionControlMock, engine, discussion, _httpWrapper,
                _httpClientFactoryMock, _options, _loggerMock);
            await processor.ProcessReviewRequestsAsync(server);
        }
        Assert.HasCount(2, _aiCliServiceMock.InvokeAiCliAsyncCalls);
        Assert.AreEqual(1, _postedNotes.Count(note => note.Contains("agentbot:mr-automation:v1:review-limit")));
        Assert.IsTrue(_commandServiceMock.RunCommandAsyncCalls.Any(call => call.arg.StartsWith("fetch --no-tags https://gitlab.com/actual-target/repo.git")));
        if (writesReview)
        {
            Assert.IsTrue(_postedNotes.Any(note => note.Contains("commit-" + 0.ToString("x40"))));
            Assert.IsFalse(_postedNotes.Any(note => note.Contains("commit-" + new string('f', 40))));
        }
    }

    [TestMethod]
    public async Task ChangedCheckoutHeadDoesNotPublishReviewForWrongCommit()
    {
        var server = new Server { Provider = "GitLab", UserName = "bot-user", EndPoint = "https://gitlab.com", Token = "token" };
        _gitLabMrList = [new GitLabMergeRequestDto { Iid = 1, ProjectId = 101, SourceBranch = "feature", TargetBranch = "main", Sha = new string('a', 40) }];
        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(new Repository { Name = "repo", CloneUrl = "https://gitlab.com/repo.git" });
        _versionControlMock.GetMergeRequestDetailsHandler = (_, _, _, _, _) => Task.FromResult(new DetailedMergeRequest());
        _commandServiceMock.RunCommandAsyncHandler = (_, _, _, _, _, _) => Task.FromResult((0, new string('b', 40), ""));
        var engine = new BotWorkflowEngine(_versionControlMock, _workspaceManagerMock, _aiCliServiceMock, _commandServiceMock, _options, NullLogger<BotWorkflowEngine>.Instance);
        var discussion = new MergeRequestDiscussionService(_versionControlMock, _workspaceManagerMock, _aiCliServiceMock, _httpClientFactoryMock, _options, NullLogger<MergeRequestDiscussionService>.Instance);
        await new MergeRequestReviewerProcessor(_versionControlMock, engine, discussion, _httpWrapper, _httpClientFactoryMock, _options, _loggerMock).ProcessReviewRequestsAsync(server);
        Assert.IsEmpty(_aiCliServiceMock.InvokeAiCliAsyncCalls);
        Assert.IsFalse(_postedNotes.Any(note => note.Contains("agentbot:mr-review:commit-")));
    }

}
