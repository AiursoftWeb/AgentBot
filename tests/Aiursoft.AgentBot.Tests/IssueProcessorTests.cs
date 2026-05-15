using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Models;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Services.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using System.Net;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class IssueProcessorTests
{
    private Mock<IVersionControlService> _versionControlMock = null!;
    private Mock<IAiWorkspaceManager> _workspaceManagerMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private Mock<AiCliService> _aiCliServiceMock = null!;
    private Mock<IAiCommandService> _commandServiceMock = null!;
    private Mock<ILogger<IssueProcessor>> _loggerMock = null!;
    private Mock<ILogger<BotWorkflowEngine>> _workflowLoggerMock = null!;
    private IOptions<AgentBotOptions> _options = null!;
    private IssueProcessor _issueProcessor = null!;

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
        _workflowLoggerMock = new Mock<ILogger<BotWorkflowEngine>>();

        _aiCliServiceMock = new Mock<AiCliService>(
            _commandServiceMock.Object,
            _options,
            new Mock<ILogger<AiCliService>>().Object);

        _loggerMock = new Mock<ILogger<IssueProcessor>>();
    }

    [TestMethod]
    public async Task ProcessAsync_WithComments_IncludesCommentsInPrompt()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };

        var issue = new Issue
        {
            Iid = 1,
            Title = "Test Issue",
            Description = "Fix the bug",
            ProjectId = 101,
            Id = 1
        };

        var repository = new Repository
        {
            Name = "repo",
            DefaultBranch = "main",
            CloneUrl = "https://gitlab.com/owner/repo.git",
            Owner = new User { Login = "owner" }
        };

        var notes = new List<GitLabNote>
        {
            new GitLabNote
            {
                Body = "First comment",
                Author = new GitLabUser { Username = "user1" },
                Created_at = new DateTime(2023, 1, 1, 10, 0, 0),
                System = false
            },
            new GitLabNote
            {
                Body = "System note",
                Author = new GitLabUser { Username = "system" },
                System = true
            },
             new GitLabNote
            {
                Body = "Second comment",
                Author = new GitLabUser { Username = "user2" },
                Created_at = new DateTime(2023, 1, 1, 12, 0, 0),
                System = false
            }
        };

        var issueDetails = new GitLabIssueDto { Iid = 1, State = "opened", Title = "Test Issue" };

        var handler = new FakeHttpMessageHandler(async (req) =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/issues/1/notes"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(notes))
                };
            }
            if (url.EndsWith("/issues/1")) // Check if issue is open
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(issueDetails))
                };
            }
            if (url.EndsWith("/user"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new GitLabUser { Id = 123, Username = "bot-user" }))
                };
            }
            if (url.Contains("/merge_requests")) // Check MRs for assignment
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        _httpWrapper = new HttpWrapper(new Mock<ILogger<HttpWrapper>>().Object, new HttpClient(handler));

        _versionControlMock.Setup(v => v.GetRepository(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(repository);

        _versionControlMock.Setup(v => v.HasOpenPullRequestForIssue(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        _versionControlMock.Setup(v => v.RepoExists(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        _versionControlMock.Setup(v => v.GetPushPath(It.IsAny<Server>(), It.IsAny<Repository>()))
            .Returns("https://gitlab.com/owner/repo.git");

        _versionControlMock.Setup(v => v.GetPullRequests(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<PullRequest>());

        _aiCliServiceMock.Setup(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync((true, "AI output", ""));

        _workspaceManagerMock.Setup(w => w.PendingCommit(It.IsAny<string>())).ReturnsAsync(false); // No changes to verify simple flow

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock.Object,
            _workspaceManagerMock.Object,
            _aiCliServiceMock.Object,
            _commandServiceMock.Object,
            _options,
            _workflowLoggerMock.Object);

        _issueProcessor = new IssueProcessor(_versionControlMock.Object, workflowEngine, _httpWrapper, _options, _loggerMock.Object);

        // Act
        await _issueProcessor.ProcessAsync(issue, server);

        // Assert
        _aiCliServiceMock.Verify(g => g.InvokeAiCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt =>
                prompt.Contains("Comment by @user1") &&
                prompt.Contains("First comment") &&
                prompt.Contains("Comment by @user2") &&
                prompt.Contains("Second comment") &&
                !prompt.Contains("System note")), // System notes should be filtered
            It.IsAny<bool>()), Times.Once);
    }
}
