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

    [TestInitialize]
    public void SetUp()
    {
        var options = new AgentBotOptions
        {
            WorkspaceFolder = Path.Combine(Path.GetTempPath(), "AgentBotReviewTests"),
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
    public async Task ProcessReviewRequestsAsync_NeedsReview_CallsAiAndPostsComment()
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
            new GitLabCommit { Message = "latest commit", Created_at = DateTime.UtcNow }
        };

        _discussionsList = new List<GitLabDiscussion>(); // No previous bot review

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
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessReviewRequestsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        _aiCliServiceMock.Verify(g => g.InvokeAiCliAsync(It.IsAny<string>(), It.Is<string>(s => s.Contains("code reviewer")), It.IsAny<bool>()), Times.Once);
    }
}
