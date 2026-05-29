using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Models;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Models.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using System.Net;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class PipelineProcessorTests
{
    private Mock<IVersionControlService> _versionControlMock = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private HttpWrapper _httpWrapper = null!;
    private Mock<ILogger<PipelineProcessor>> _loggerMock = null!;
    private List<GitLabProjectDto> _starredProjects = new();
    private List<GitLabPipelineDto> _pipelines = new();
    private List<GitLabIssueDto> _existingIssues = new();
    private GitLabUser _botUser = new();
    private List<GitLabMergeRequestDto> _relatedMRs = new();
    private HttpRequestMessage? _capturedPostRequest;
    private HttpRequestMessage? _capturedIssueCloseRequest;
    private HttpRequestMessage? _capturedMrCloseRequest;

    [TestInitialize]
    public void SetUp()
    {
        _versionControlMock = new Mock<IVersionControlService>();
        _loggerMock = new Mock<ILogger<PipelineProcessor>>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        var handler = new FakeHttpMessageHandler((req) =>
        {
            var url = req.RequestUri!.ToString();
            if (req.Method == HttpMethod.Get && url.Contains("projects?starred=true"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_starredProjects))
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("/pipelines") && url.Contains("ref="))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_pipelines))
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("/issues") && url.Contains("search="))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_existingIssues))
                });
            }
            if (req.Method == HttpMethod.Get && url.EndsWith("/api/v4/user"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_botUser))
                });
            }
            if (req.Method == HttpMethod.Post && url.Contains("/issues"))
            {
                _capturedPostRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{}")
                });
            }
            if (req.Method == HttpMethod.Put && url.Contains("/issues/") && !url.Contains("related_merge_requests"))
            {
                _capturedIssueCloseRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
            if (req.Method == HttpMethod.Get && url.Contains("related_merge_requests"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(_relatedMRs))
                });
            }
            if (req.Method == HttpMethod.Put && url.Contains("/merge_requests/"))
            {
                _capturedMrCloseRequest = req;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(handler);
        _httpWrapper = new HttpWrapper(new Mock<ILogger<HttpWrapper>>().Object, client);
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
    }

    [TestMethod]
    public async Task ProcessStarredProjectsAsync_FailingPipeline_CreatesIssue()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };

        _starredProjects = new List<GitLabProjectDto>
        {
            new GitLabProjectDto { Id = 101, Name = "Project1", DefaultBranch = "main" }
        };

        _pipelines = new List<GitLabPipelineDto>
        {
            new GitLabPipelineDto { Id = 555, Status = "failed" }
        };

        _existingIssues = new List<GitLabIssueDto>();
        _botUser = new GitLabUser { Id = 123, Username = "bot-user" };

        _versionControlMock
            .Setup(v => v.GetPipelineJobs(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<PipelineJob> { new PipelineJob { Id = 1, Name = "test", Status = "failed" } });

        _versionControlMock
            .Setup(v => v.GetJobLog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync("Build error log");

        var processor = new PipelineProcessor(
            _versionControlMock.Object,
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessStarredProjectsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(_capturedPostRequest);

        var body = await _capturedPostRequest.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.AreEqual("主分支的编译管道失败", root.GetProperty("title").GetString());
        StringAssert.Contains(root.GetProperty("description").GetString(), "Build error log");
        Assert.AreEqual(123, root.GetProperty("assignee_ids")[0].GetInt32());
    }

    [TestMethod]
    public async Task ProcessStarredProjectsAsync_IssueAlreadyExists_DoesNotCreateNewOne()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };

        _starredProjects = new List<GitLabProjectDto>
        {
            new GitLabProjectDto { Id = 101, Name = "Project1", DefaultBranch = "main" }
        };

        _pipelines = new List<GitLabPipelineDto>
        {
            new GitLabPipelineDto { Id = 555, Status = "failed" }
        };

        _existingIssues = new List<GitLabIssueDto>
        {
            new GitLabIssueDto { Iid = 1, Title = "主分支的编译管道失败", State = "opened" }
        };

        var processor = new PipelineProcessor(
            _versionControlMock.Object,
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessStarredProjectsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNull(_capturedPostRequest);
    }

    [TestMethod]
    public async Task ProcessStarredProjectsAsync_SucceededPipeline_ClosesExistingIssue()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };

        _starredProjects = new List<GitLabProjectDto>
        {
            new GitLabProjectDto { Id = 101, Name = "Project1", DefaultBranch = "main" }
        };

        _pipelines = new List<GitLabPipelineDto>
        {
            new GitLabPipelineDto { Id = 555, Status = "success" }
        };

        _existingIssues = new List<GitLabIssueDto>
        {
            new GitLabIssueDto { Iid = 1, Title = "主分支的编译管道失败", State = "opened" }
        };

        _relatedMRs = new List<GitLabMergeRequestDto>();

        var processor = new PipelineProcessor(
            _versionControlMock.Object,
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessStarredProjectsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(_capturedIssueCloseRequest);
        Assert.IsTrue(_capturedIssueCloseRequest!.RequestUri!.ToString().Contains("/issues/1"));
        Assert.IsNull(_capturedMrCloseRequest);
    }

    [TestMethod]
    public async Task ProcessStarredProjectsAsync_SucceededPipeline_ClosesIssueAndRelatedMR()
    {
        // Arrange
        var server = new Server
        {
            Provider = "GitLab",
            UserName = "bot-user",
            Token = "token",
            EndPoint = "https://gitlab.com"
        };

        _starredProjects = new List<GitLabProjectDto>
        {
            new GitLabProjectDto { Id = 101, Name = "Project1", DefaultBranch = "main" }
        };

        _pipelines = new List<GitLabPipelineDto>
        {
            new GitLabPipelineDto { Id = 555, Status = "success" }
        };

        _existingIssues = new List<GitLabIssueDto>
        {
            new GitLabIssueDto { Iid = 1, Title = "主分支的编译管道失败", State = "opened" }
        };

        _relatedMRs = new List<GitLabMergeRequestDto>
        {
            new GitLabMergeRequestDto { Iid = 42, Title = "Fix MR", SourceBranch = "fix", TargetBranch = "main" }
        };

        _capturedIssueCloseRequest = null;
        _capturedMrCloseRequest = null;

        var processor = new PipelineProcessor(
            _versionControlMock.Object,
            _httpWrapper,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);

        // Act
        var result = await processor.ProcessStarredProjectsAsync(server);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(_capturedIssueCloseRequest);
        Assert.IsTrue(_capturedIssueCloseRequest!.RequestUri!.ToString().Contains("/issues/1"));
        Assert.IsNotNull(_capturedMrCloseRequest);
        Assert.IsTrue(_capturedMrCloseRequest!.RequestUri!.ToString().Contains("/merge_requests/42"));
    }
}
