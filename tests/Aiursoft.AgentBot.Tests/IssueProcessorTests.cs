using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Models;
using Aiursoft.AgentBot.Services;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Net;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class IssueProcessorTests
{
    private StubVersionControlService _versionControlMock = null!;
    private StubWorkspaceManager _workspaceManagerMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private StubAiCliService _aiCliServiceMock = null!;
    private StubCommandService _commandServiceMock = null!;
    private ILogger<IssueProcessor> _loggerMock = null!;
    private ILogger<BotWorkflowEngine> _workflowLoggerMock = null!;
    private IOptions<AgentBotOptions> _options = null!;
    private IssueProcessor _issueProcessor = null!;

    [TestInitialize]
    public void SetUp()
    {
        var options = new AgentBotOptions
        {
            WorkspaceFolder = Path.Combine(Path.GetTempPath(), "AgentBotTests"),
            ForkWaitDelayMs = 0,
            PlanningModeEnabled = false,
            ReplyLanguage = ReplyLanguage.Zh
        };
        _options = Options.Create(options);

        _commandServiceMock = new StubCommandService();
        _versionControlMock = new StubVersionControlService();
        _workspaceManagerMock = new StubWorkspaceManager();
        _workflowLoggerMock = NullLogger<BotWorkflowEngine>.Instance;

        _aiCliServiceMock = new StubAiCliService(
            _commandServiceMock,
            _options,
            NullLogger<AiCliService>.Instance);

        _loggerMock = NullLogger<IssueProcessor>.Instance;
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

        _httpWrapper = new HttpWrapper(NullLogger<HttpWrapper>.Instance, new HttpClient(handler));

        _versionControlMock.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(repository);

        _versionControlMock.HasOpenPullRequestForIssueHandler = (_, _, _, _) => Task.FromResult(false);

        _versionControlMock.RepoExistsHandler = (_, _, _, _) => Task.FromResult(true);

        _versionControlMock.GetPushPathHandler = (_, _) => "https://gitlab.com/owner/repo.git";

        _versionControlMock.GetPullRequestsHandler = (_, _, _, _, _) => Task.FromResult<IEnumerable<PullRequest>>(new List<PullRequest>());

        _aiCliServiceMock.InvokeAiCliAsyncHandler = (_, _, _) => Task.FromResult((true, "AI output", ""));

        _workspaceManagerMock.PendingCommitHandler = (_) => Task.FromResult(false); // No changes to verify simple flow

        var workflowEngine = new BotWorkflowEngine(
            _versionControlMock,
            _workspaceManagerMock,
            _aiCliServiceMock,
            _commandServiceMock,
            _options,
            _workflowLoggerMock);

        var planningService = new IssuePlanningService(
            _workspaceManagerMock,
            _aiCliServiceMock,
            _httpWrapper,
            new HttpClient(handler),
            _options,
            NullLogger<IssuePlanningService>.Instance);
        _issueProcessor = new IssueProcessor(
            _versionControlMock,
            workflowEngine,
            planningService,
            _httpWrapper,
            _options,
            _loggerMock);

        // Act
        await _issueProcessor.ProcessAsync(issue, server);

        // Assert
        Assert.AreEqual(1, _aiCliServiceMock.InvokeAiCliAsyncCalls.Count(call => (call.taskDescription.Contains("Comment by @user1") &&
                call.taskDescription.Contains("First comment") &&
                call.taskDescription.Contains("Comment by @user2") &&
                call.taskDescription.Contains("Second comment") &&
                call.taskDescription.Contains("Simplified Chinese") &&
                !call.taskDescription.Contains("System note"))));
    }

    [TestMethod]
    [DataRow(AiEngine.Codex, "respond")]
    [DataRow(AiEngine.Copilot, "respond")]
    [DataRow(AiEngine.Copilot, "publish_plan")]
    public async Task ProcessAsync_PlanningResult_PostsCommentWithoutStartingImplementationWorkflow(AiEngine engine, string action)
    {
        var options = Options.Create(new AgentBotOptions
        {
            Engine = engine,
            PlanningModeEnabled = true,
            WorkspaceFolder = Path.Combine(Path.GetTempPath(), "AgentBotAuditTests")
        });
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "agent-bot",
            Token = "token",
            EndPoint = "https://gitlab.example.com"
        };
        var issue = new Issue
        {
            Id = 41,
            Iid = 41,
            ProjectId = 101,
            Title = "Audit authentication",
            Description = "Inspect the repository and report findings. Do not modify code.",
            Author = new User { Login = "issue-owner" }
        };
        var repository = new Repository
        {
            Name = "repo",
            DefaultBranch = "main",
            CloneUrl = "https://gitlab.example.com/group/repo.git",
            Owner = new User { Login = "group" }
        };
        var postedComments = new List<string>();
        var handler = new FakeHttpMessageHandler(async request =>
        {
            var url = request.RequestUri!.ToString();
            if (request.Method == HttpMethod.Post && url.EndsWith("/issues/41/notes", StringComparison.Ordinal))
            {
                var formBody = await request.Content!.ReadAsStringAsync();
                postedComments.Add(Uri.UnescapeDataString(formBody.Split("body=", 2)[1].Replace('+', ' ')));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }
            if (url.Contains("/issues/41/notes", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]")
                };
            }
            if (url.EndsWith("/issues/41", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new GitLabIssueDto
                    {
                        Iid = 41,
                        State = "opened",
                        Title = issue.Title
                    }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var httpClient = new HttpClient(handler);
        var httpWrapper = new HttpWrapper(NullLogger<HttpWrapper>.Instance, httpClient);
        var workspace = new StubWorkspaceManager();
        var command = new StubCommandService();
        var ai = new StubAiCliService(command, options, NullLogger<AiCliService>.Instance);
        ai.InvokePlanningCliAsyncHandler = (_, _) => Task.FromResult((true, JsonSerializer.Serialize(new
            {
                action,
                approval_note_id = (long?)null,
                plan_markdown = action == "publish_plan" ? "Implement the requested change after approval." : null,
                response_markdown = "Audit findings: no critical issues found."
            }), string.Empty));
        var versionControl = new StubVersionControlService();
        versionControl.GetRepositoryHandler = (_, _, _, _) => Task.FromResult(repository);
        versionControl.HasOpenPullRequestForIssueHandler = (_, _, _, _) => Task.FromResult(false);
        var workflowEngine = new BotWorkflowEngine(
            versionControl,
            workspace,
            ai,
            command,
            options,
            NullLogger<BotWorkflowEngine>.Instance);
        var planningService = new IssuePlanningService(
            workspace,
            ai,
            httpWrapper,
            httpClient,
            options,
            NullLogger<IssuePlanningService>.Instance);
        var processor = new IssueProcessor(
            versionControl,
            workflowEngine,
            planningService,
            httpWrapper,
            options,
            NullLogger<IssueProcessor>.Instance);

        var result = await processor.ProcessAsync(issue, server);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, postedComments.Count);
        StringAssert.Contains(postedComments[0], action == "publish_plan"
            ? "agentbot:plan:v1"
            : "agentbot:discussion:plan-v0:through-note-0");
        Assert.AreEqual(1, ai.InvokePlanningCliAsyncCalls.Count);
        Assert.AreEqual(0, ai.InvokeAiCliAsyncCalls.Count);
        Assert.AreEqual(0, workspace.CommitToBranchCalls.Count);
        Assert.AreEqual(0, workspace.PushCalls.Count);
        Assert.AreEqual(0, versionControl.ForkRepoCalls.Count);
        Assert.AreEqual(0, versionControl.CreatePullRequestCalls.Count);
    }
}
