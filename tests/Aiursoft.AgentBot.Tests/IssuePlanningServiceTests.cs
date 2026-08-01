using System.Net;
using System.Text.Json;
using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Models;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Services.Abstractions;
using Aiursoft.GitRunner.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class IssuePlanningServiceTests
{
    [TestMethod]
    public async Task ProcessAsync_AuthorizedAuthorApproval_TransitionsToImplementation()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 22,
            Body = "批准当前计划，开始开发！",
            Author = new GitLabUser { Username = "issue-owner" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(notes, "approval_candidate", 22);

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsTrue(outcome.Approved);
        StringAssert.Contains(outcome.ApprovedPlan!, "Agent Plan v1");
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:approved:plan-v1:note-22");
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessAsync_UnauthorizedApproval_DoesNotTransition()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 23,
            Body = "Approve the plan and start implementation.",
            Author = new GitLabUser { Username = "random-user" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(notes, "approval_candidate", 23);

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:plan:v2");
        Assert.IsFalse(fixture.PostedComments[0].Contains("agentbot:approved", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProcessAsync_OnlyBotSpokeAfterPlan_DoesNotRunPlannerOrApprove()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 24,
            Body = "Approve the plan and start implementation.",
            Author = new GitLabUser { Username = "agent-bot" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(notes, "approval_candidate", 24);

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(0, fixture.PostedComments.Count);
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Workspace.Verify(w => w.ResetRepo(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CloneMode>(), It.IsAny<string>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_ExistingApprovalMarker_ResumesWithoutPlanner()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 25,
            Body = "<!-- agentbot:approved:plan-v1:note-22 -->",
            Author = new GitLabUser { Username = "agent-bot" },
            Created_at = Utc(13)
        });
        var fixture = CreateFixture(notes, "continue_discussion", null);

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsTrue(outcome.Approved);
        Assert.AreEqual(0, fixture.PostedComments.Count);
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static List<GitLabNote> ExistingPlanWith(GitLabNote additionalNote) =>
    [
        new GitLabNote
        {
            Id = 20,
            Body = "## Agent Plan v1\n\nImplement the safe change and test it.\n\n<!-- agentbot:plan:v1 -->",
            Author = new GitLabUser { Username = "agent-bot" },
            Created_at = Utc(10)
        },
        additionalNote
    ];

    private static Fixture CreateFixture(List<GitLabNote> notes, string decision, long? approvalNoteId)
    {
        var postedComments = new List<string>();
        var handler = new FakeHttpMessageHandler(async req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(notes))
                };
            }

            var formBody = await req.Content!.ReadAsStringAsync();
            postedComments.Add(Uri.UnescapeDataString(formBody.Split("body=", 2, StringSplitOptions.None)[1].Replace('+', ' ')));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new GitLabNote()))
            };
        });
        var httpClient = new HttpClient(handler);
        var httpWrapper = new HttpWrapper(Mock.Of<ILogger<HttpWrapper>>(), httpClient);
        var options = Options.Create(new AgentBotOptions
        {
            Engine = AiEngine.Codex,
            WorkspaceFolder = Path.Combine(Path.GetTempPath(), "AgentBotPlanningTests"),
            Reviewer = "trusted-reviewer"
        });
        var workspace = new Mock<IAiWorkspaceManager>();
        var command = new Mock<IAiCommandService>();
        var ai = new Mock<AiCliService>(command.Object, options, Mock.Of<ILogger<AiCliService>>());
        var response = JsonSerializer.Serialize(new
        {
            decision,
            approval_note_id = approvalNoteId,
            plan_markdown = "Implement the safe change and test it.",
            response_markdown = "Ready for approval."
        });
        ai.Setup(a => a.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((true, response, string.Empty));

        var service = new IssuePlanningService(
            workspace.Object,
            ai.Object,
            httpWrapper,
            httpClient,
            options,
            Mock.Of<ILogger<IssuePlanningService>>());
        var issue = new Issue
        {
            Id = 49,
            Iid = 49,
            ProjectId = 101,
            Title = "Implement feature",
            Description = "Feature details",
            Author = new User { Login = "issue-owner" }
        };
        var server = new Server
        {
            Provider = "GitLab",
            EndPoint = "https://gitlab.example.com",
            UserName = "agent-bot",
            Token = "token"
        };
        var repository = new Repository
        {
            Name = "repo",
            DefaultBranch = "master",
            CloneUrl = "https://gitlab.example.com/group/repo.git"
        };
        return new Fixture(service, issue, server, repository, ai, workspace, postedComments);
    }

    private static DateTime Utc(int hour) => new(2026, 8, 1, hour, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        IssuePlanningService Service,
        Issue Issue,
        Server Server,
        Repository Repository,
        Mock<AiCliService> Ai,
        Mock<IAiWorkspaceManager> Workspace,
        List<string> PostedComments);
}
