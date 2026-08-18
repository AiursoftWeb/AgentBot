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
        var fixture = CreateFixture(notes, "approve_current_plan", 22);

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
        var fixture = CreateFixture(notes, "approve_current_plan", 23);

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:discussion:plan-v1:through-note-23");
        Assert.IsFalse(fixture.PostedComments[0].Contains("agentbot:plan:v2", StringComparison.Ordinal));
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
        var fixture = CreateFixture(notes, "approve_current_plan", 24);

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
        var fixture = CreateFixture(notes, "respond");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsTrue(outcome.Approved);
        Assert.AreEqual(0, fixture.PostedComments.Count);
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_QuestionGetsNaturalReplyWithoutNewPlan()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 22,
            Body = "What happens to the old URL if the slug changes?",
            Author = new GitLabUser { Username = "issue-owner" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(
            notes,
            "respond",
            responseMarkdown: "The old URL should redirect directly to the current URL. Which retention policy do you prefer?");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "The old URL should redirect");
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:discussion:plan-v1:through-note-22");
        Assert.IsFalse(fixture.PostedComments[0].Contains("Agent Plan v2", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProcessAsync_HandledDiscussionDoesNotRunPlannerAgain()
    {
        var notes = ExistingPlanWith(
            new GitLabNote
            {
                Id = 22,
                Body = "Please explain the compatibility risk.",
                Author = new GitLabUser { Username = "issue-owner" },
                Created_at = Utc(12)
            },
            new GitLabNote
            {
                Id = 23,
                Body = "Here is the compatibility tradeoff.\n\n<!-- agentbot:discussion:plan-v1:through-note-22 -->",
                Author = new GitLabUser { Username = "agent-bot" },
                Created_at = Utc(13)
            });
        var fixture = CreateFixture(notes, "respond");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(0, fixture.PostedComments.Count);
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        fixture.Workspace.Verify(w => w.ResetRepo(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CloneMode>(), It.IsAny<string>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ProcessAsync_ApprovalAfterDiscussionReplyApprovesUnchangedPlan()
    {
        var notes = ExistingPlanWith(
            new GitLabNote
            {
                Id = 21,
                Body = "Can you explain the migration risk?",
                Author = new GitLabUser { Username = "issue-owner" },
                Created_at = Utc(11)
            },
            new GitLabNote
            {
                Id = 22,
                Body = "The migration remains backward compatible.\n\n<!-- agentbot:discussion:plan-v1:through-note-21 -->",
                Author = new GitLabUser { Username = "agent-bot" },
                Created_at = Utc(12)
            },
            new GitLabNote
            {
                Id = 23,
                Body = "Thanks. Approve the current plan and start implementation.",
                Author = new GitLabUser { Username = "issue-owner" },
                Created_at = Utc(13)
            });
        var fixture = CreateFixture(notes, "approve_current_plan", 23, responseMarkdown: string.Empty);

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsTrue(outcome.Approved);
        StringAssert.Contains(outcome.ApprovedPlan!, "Implement the safe change");
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:approved:plan-v1:note-23");
    }

    [TestMethod]
    public async Task ProcessAsync_MaterialDecisionPublishesNewPlan()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 22,
            Body = "Use 180-day retention and then delete the old slug.",
            Author = new GitLabUser { Username = "issue-owner" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(
            notes,
            "publish_plan",
            planMarkdown: "Retain old slugs for 180 days, then delete them.",
            responseMarkdown: "Understood. The plan now uses the selected 180-day policy.");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "Agent Plan v2");
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:plan-content:start");
        StringAssert.Contains(fixture.PostedComments[0], "Retain old slugs for 180 days");
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:plan:v2");
    }

    [TestMethod]
    public async Task ProcessAsync_ChinesePreferenceLocalizesPlanTemplateAndPrompt()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 22,
            Body = "使用 180 天保留期。",
            Author = new GitLabUser { Username = "issue-owner" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(
            notes,
            "publish_plan",
            planMarkdown: "旧链接保留 180 天。",
            responseMarkdown: "已按你的决定更新。",
            replyLanguage: ReplyLanguage.Zh);

        await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        StringAssert.Contains(fixture.PostedComments[0], "## Agent 计划 v2");
        StringAssert.Contains(fixture.PostedComments[0], "等待批准");
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt => prompt.Contains("Simplified Chinese", StringComparison.Ordinal))),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessAsync_InitialBlockingQuestionUsesVersionZeroWatermark()
    {
        var notes = new List<GitLabNote>();
        var fixture = CreateFixture(
            notes,
            "respond",
            responseMarkdown: "Should this behavior apply to draft posts as well?");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:discussion:plan-v0:through-note-0");
        Assert.IsFalse(fixture.PostedComments[0].Contains("Agent Plan v1", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProcessAsync_InitialAuditOnlyRequest_PostsFindingsWithoutPublishingPlan()
    {
        var fixture = CreateFixture(
            [],
            "respond",
            planMarkdown: null,
            responseMarkdown: "Audit findings:\n1. Authentication state is not synchronized.",
            issueTitle: "Audit authentication error handling",
            issueDescription: "Inspect the repository and report findings. Do not modify code.");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "Audit findings:");
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:discussion:plan-v0:through-note-0");
        Assert.IsFalse(fixture.PostedComments[0].Contains("agentbot:plan:v1", StringComparison.Ordinal));
        Assert.IsFalse(fixture.PostedComments[0].Contains("agentbot:approved", StringComparison.Ordinal));
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt =>
                prompt.Contains("Audit authentication error handling", StringComparison.Ordinal) &&
                prompt.Contains("perform that work now", StringComparison.Ordinal) &&
                prompt.Contains("response_markdown", StringComparison.Ordinal))),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessAsync_AuditCorrectionAfterErroneousPlan_PostsFindingsWithoutRevisingPlan()
    {
        var notes = ExistingPlanWith(new GitLabNote
        {
            Id = 22,
            Body = "我不是让你开始开发。。我是让你立刻审计，并且将结论回复！",
            Author = new GitLabUser { Username = "issue-owner" },
            Created_at = Utc(12)
        });
        var fixture = CreateFixture(
            notes,
            "respond",
            planMarkdown: null,
            responseMarkdown: "审计结论：发现一处高危同源存储型 XSS。",
            issueTitle: "安全审计",
            issueDescription: "审计普通注册用户可以升级权限的安全问题，并直接回复结论。");

        var outcome = await fixture.Service.ProcessAsync(fixture.Issue, fixture.Server, fixture.Repository);

        Assert.IsFalse(outcome.Approved);
        Assert.AreEqual(1, fixture.PostedComments.Count);
        StringAssert.Contains(fixture.PostedComments[0], "审计结论：");
        StringAssert.Contains(fixture.PostedComments[0], "agentbot:discussion:plan-v1:through-note-22");
        Assert.IsFalse(fixture.PostedComments[0].Contains("agentbot:plan:v2", StringComparison.Ordinal));
        fixture.Ai.Verify(a => a.InvokePlanningCliAsync(
            It.IsAny<string>(),
            It.Is<string>(prompt =>
                prompt.Contains("我不是让你开始开发", StringComparison.Ordinal) &&
                prompt.Contains("an existing plan or earlier bot response does not turn", StringComparison.Ordinal) &&
                prompt.Contains("do not publish or revise an implementation plan", StringComparison.Ordinal))),
            Times.Once);
    }

    [TestMethod]
    public void BuildPlannerPrompt_AuditOnlyIssue_InstructsWorkerToExecuteAuditAndRespondWithoutPlan()
    {
        var issue = new Issue
        {
            Iid = 49,
            Title = "Audit authentication error handling",
            Description = "Inspect the repository and report security findings. Do not modify code."
        };

        var prompt = IssuePlanningService.BuildPlannerPrompt(issue, null, [], [], ReplyLanguage.En);

        StringAssert.Contains(prompt, "A read-only deliverable is terminal work, not implementation planning");
        StringAssert.Contains(prompt, "perform that work now during this invocation");
        StringAssert.Contains(prompt, "Return the completed findings with respond");
        StringAssert.Contains(prompt, "leave plan_markdown and approval_note_id null");
        StringAssert.Contains(prompt, "do not publish or revise an implementation plan");
    }

    [TestMethod]
    public void BuildPlannerPrompt_IncludesCurrentPlanOnlyOnceAndExcludesOldPlanNote()
    {
        const string canonicalPlan = "CANONICAL-PLAN-ONLY-ONCE";
        var currentPlan = new IssuePlanState(2, 30, canonicalPlan);
        var notes = new List<GitLabNote>
        {
            new()
            {
                Id = 20,
                Body = "OBSOLETE-PLAN-V1",
                Author = new GitLabUser { Username = "agent-bot" },
                Created_at = Utc(10)
            },
            new()
            {
                Id = 30,
                Body = "PLAN-NOTE-SHOULD-NOT-BE-DUPLICATED",
                Author = new GitLabUser { Username = "agent-bot" },
                Created_at = Utc(11)
            },
            new()
            {
                Id = 31,
                Body = "LATEST-HUMAN-QUESTION",
                Author = new GitLabUser { Username = "issue-owner" },
                Created_at = Utc(12)
            }
        };
        var issue = new Issue { Iid = 49, Title = "Feature", Description = "Details" };

        var prompt = IssuePlanningService.BuildPlannerPrompt(
            issue,
            currentPlan,
            notes,
            [notes[2]],
            ReplyLanguage.En);

        Assert.AreEqual(1, CountOccurrences(prompt, canonicalPlan));
        Assert.IsFalse(prompt.Contains("OBSOLETE-PLAN-V1", StringComparison.Ordinal));
        Assert.IsFalse(prompt.Contains("PLAN-NOTE-SHOULD-NOT-BE-DUPLICATED", StringComparison.Ordinal));
        StringAssert.Contains(prompt, "LATEST-HUMAN-QUESTION");
        StringAssert.Contains(prompt, "New human note IDs that require a response in this invocation:\n31");
    }

    [TestMethod]
    public void FindCurrentPlan_NewFormatExtractsOnlyCanonicalPlan()
    {
        var notes = new List<GitLabNote>
        {
            new()
            {
                Id = 30,
                Body = """
                    Thanks, I incorporated the decision.

                    ## Agent Plan v2
                    <!-- agentbot:plan-content:start -->
                    Canonical plan body.
                    <!-- agentbot:plan-content:end -->

                    Approval boilerplate.
                    <!-- agentbot:plan:v2 -->
                    """,
                Author = new GitLabUser { Username = "agent-bot" },
                Created_at = Utc(12)
            }
        };

        var plan = IssuePlanningService.FindCurrentPlan(notes, "agent-bot");

        Assert.IsNotNull(plan);
        Assert.AreEqual(2, plan.Version);
        Assert.AreEqual("Canonical plan body.", plan.Markdown);
    }

    [TestMethod]
    public void ParsedAction_MissingActionReportsUnsupportedResponse()
    {
        var response = new IssuePlannerResponse { Action = null };

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => _ = response.ParsedAction);

        StringAssert.Contains(exception.Message, "unsupported action");
    }

    private static List<GitLabNote> ExistingPlanWith(params GitLabNote[] additionalNotes)
    {
        var notes = new List<GitLabNote>
        {
            new()
            {
                Id = 20,
                Body = "## Agent Plan v1\n\nImplement the safe change and test it.\n\n<!-- agentbot:plan:v1 -->",
                Author = new GitLabUser { Username = "agent-bot" },
                Created_at = Utc(10)
            }
        };
        notes.AddRange(additionalNotes);
        return notes;
    }

    private static Fixture CreateFixture(
        List<GitLabNote> notes,
        string action,
        long? approvalNoteId = null,
        string? planMarkdown = "Implement the safe change and test it.",
        string responseMarkdown = "Ready for approval.",
        ReplyLanguage replyLanguage = ReplyLanguage.En,
        string issueTitle = "Implement feature",
        string issueDescription = "Feature details")
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
            postedComments.Add(Uri.UnescapeDataString(formBody.Split("body=", 2)[1].Replace('+', ' ')));
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
            Reviewer = "trusted-reviewer",
            ReplyLanguage = replyLanguage
        });
        var workspace = new Mock<IAiWorkspaceManager>();
        var command = new Mock<IAiCommandService>();
        var ai = new Mock<AiCliService>(command.Object, options, Mock.Of<ILogger<AiCliService>>());
        var response = JsonSerializer.Serialize(new
        {
            action,
            approval_note_id = approvalNoteId,
            plan_markdown = planMarkdown,
            response_markdown = responseMarkdown
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
            Title = issueTitle,
            Description = issueDescription,
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

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }

    private sealed record Fixture(
        IssuePlanningService Service,
        Issue Issue,
        Server Server,
        Repository Repository,
        Mock<AiCliService> Ai,
        Mock<IAiWorkspaceManager> Workspace,
        List<string> PostedComments);
}
