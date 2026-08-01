using Aiursoft.NugetNinja.GitServerBase.Services;

namespace Aiursoft.AgentBot.Services;

public static class GitLabPagination
{
    private const int PageSize = 100;
    private const int MaximumPages = 1000;

    public static async Task<List<T>> GetAllAsync<T>(HttpWrapper httpWrapper, string url, string token)
    {
        var results = new List<T>();
        var separator = url.Contains('?') ? '&' : '?';

        for (var page = 1; page <= MaximumPages; page++)
        {
            var pageUrl = $"{url}{separator}per_page={PageSize}&page={page}";
            var items = await httpWrapper.SendHttpAndGetJson<List<T>>(pageUrl, HttpMethod.Get, token);
            results.AddRange(items);

            if (items.Count < PageSize)
            {
                return results;
            }
        }

        throw new InvalidOperationException(
            $"GitLab pagination exceeded the safety limit of {MaximumPages} pages for {url}.");
    }
}
