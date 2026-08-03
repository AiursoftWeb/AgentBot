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

[TestClass]
public class MergeRequestReviewerProcessorTests
{
    private Mock<IVersionControlService> _versionControlMock = null!;
    private Mock<IAiWorkspaceManager> _workspaceManagerMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private Mock<AiCliService> _aiCliServiceMock = null!;
    private Mock<IAiCommandService> _commandServiceMock = null!;
    private Mock<ILogger<MergeRequestReviewerProcessor>> _loggerMock = null!;
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

        _commandServiceMock = new Mock<IAiCommandService>();
        _versionControlMock = new Mock<IVersionControlService>();
        _workspaceManagerMock = new Mock<IAiWorkspaceManager>();
        _workspaceManagerMock
            .Setup(manager => manager.ResetRepo(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Aiursoft.GitRunner.Models.CloneMode>(),
                It.IsAny<string>()))
            .Callback<string, string, string, Aiursoft.GitRunner.Models.CloneMode, string>(
                (path, _, _, _, _) => Directory.CreateDirectory(path));
        _aiCliServiceMock = new Mock<AiCliService>(
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<AiCliService>>().Object);
        _loggerMock = new Mock<ILogger<MergeRequestReviewerProcessor>>();

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
                _postedNotes.Add(JsonDocument.Parse(json).RootElement.GetProperty("body").GetString()!);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(handler);
        _httpWrapper = new HttpWrapper(new Mock<ILogger<HttpWrapper>>().Object, client);
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
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

        _versionControlMock
            .Setup(v => v.GetRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);

        _versionControlMock
            .Setup(v => v.GetMergeRequestDetails(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new DetailedMergeRequest());

        _aiCliServiceMock
            .Setup(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, bool>((path, _, _) =>
            {
                File.WriteAllText(Path.Combine(path, "review.md"), "This is a great MR!");
            })
            .ReturnsAsync((true, "AI reviewed", ""));

        _workspaceManagerMock
            .Setup(w => w.PendingCommit(It.IsAny<string>()))
            .ReturnsAsync(false); // No changes made by bot

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);

        var processor = new MergeRequestReviewerProcessor(
            _versionControlMock.Object,
            workflowEngine,
            new MergeRequestDiscussionService(
                _versionControlMock.Object,
                _workspaceManagerMock.Object,
                _aiCliServiceMock.Object,
                _httpClientFactoryMock.Object,
                _options,
                new Mock<ILogger<MergeRequestDiscussionService>>().Object),
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _options,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessReviewRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        _aiCliServiceMock.Verify(g => g.InvokeAiCliAsync(
            It.IsAny<string>(),
            It.Is<string>(s => s.Contains("code reviewer") && s.Contains("Simplified Chinese")),
            It.IsAny<bool>()), Times.Once);
        Assert.HasCount(1, _postedNotes);
        StringAssert.Contains(
            _postedNotes[0],
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
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);
        var processor = new MergeRequestReviewerProcessor(
            _versionControlMock.Object,
            workflowEngine,
            new MergeRequestDiscussionService(
                _versionControlMock.Object,
                _workspaceManagerMock.Object,
                _aiCliServiceMock.Object,
                _httpClientFactoryMock.Object,
                _options,
                new Mock<ILogger<MergeRequestDiscussionService>>().Object),
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _options,
            _loggerMock.Object);

        var result = await processor.ProcessReviewRequestsAsync(server);

        Assert.IsTrue(result.Success);
        Assert.IsEmpty(_postedNotes);
        _aiCliServiceMock.Verify(service => service.InvokeAiCliAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
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
        _versionControlMock
            .Setup(service => service.GetRepository(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new Repository
            {
                CloneUrl = "https://gitlab.com/bot-user/repo.git",
                Name = "repo"
            });
        _aiCliServiceMock
            .Setup(service => service.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((true,
                "{\"action\":\"implement_feedback\",\"addressed_note_ids\":[11],\"response_markdown\":\"我来修改。\",\"implementation_brief\":\"Handle the edge case.\"}",
                string.Empty));

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<BotWorkflowEngine>>().Object);
        var processor = new MergeRequestReviewerProcessor(
            _versionControlMock.Object,
            workflowEngine,
            new MergeRequestDiscussionService(
                _versionControlMock.Object,
                _workspaceManagerMock.Object,
                _aiCliServiceMock.Object,
                _httpClientFactoryMock.Object,
                _options,
                new Mock<ILogger<MergeRequestDiscussionService>>().Object),
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _options,
            _loggerMock.Object);

        var result = await processor.ProcessReviewRequestsAsync(server);

        Assert.IsTrue(result.Success);
        _aiCliServiceMock.Verify(service => service.InvokePlanningCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt => prompt.Contains("Implementation authority for this invocation: not allowed"))),
            Times.Once);
        _aiCliServiceMock.Verify(service => service.InvokeAiCliAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
