using System.Net;
using System.Text.Json;
using Aiursoft.AgentBot.Services;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class GitLabPaginationTests
{
    [TestMethod]
    public async Task GetAllAsync_MoreThanOnePage_ReturnsEveryItem()
    {
        var requestedPages = new List<int>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            var query = req.RequestUri!.Query;
            var page = int.Parse(query.Split("&page=", 2)[1].Split('&')[0]);
            requestedPages.Add(page);
            var start = (page - 1) * 100;
            var count = page == 1 ? 100 : 36;
            var items = Enumerable.Range(start, count).ToList();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(items))
            });
        });
        var wrapper = new HttpWrapper(Mock.Of<ILogger<HttpWrapper>>(), new HttpClient(handler));

        var result = await GitLabPagination.GetAllAsync<int>(
            wrapper,
            "https://gitlab.example.com/api/v4/projects/1/merge_requests/181/discussions",
            "token");

        Assert.AreEqual(136, result.Count);
        CollectionAssert.AreEqual(new[] { 1, 2 }, requestedPages);
        Assert.AreEqual(135, result[^1]);
    }

    [TestMethod]
    public async Task GetAllAsync_ExistingQuery_PreservesIt()
    {
        Uri? requestedUri = null;
        var handler = new FakeHttpMessageHandler(req =>
        {
            requestedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        });
        var wrapper = new HttpWrapper(Mock.Of<ILogger<HttpWrapper>>(), new HttpClient(handler));

        await GitLabPagination.GetAllAsync<int>(wrapper, "https://gitlab.example.com/notes?sort=asc", "token");

        Assert.IsNotNull(requestedUri);
        StringAssert.Contains(requestedUri.Query, "sort=asc&per_page=100&page=1");
    }
}
