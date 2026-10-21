using System.Text.RegularExpressions;
using Aiursoft.AgentBot.Models;

namespace Aiursoft.AgentBot.Services;

internal static partial class MergeRequestAutomationBudget
{
    internal const int Limit = 2;

    [GeneratedRegex(@"<!--\s*agentbot:mr-automation:v1:(?<kind>review|repair)-round-(?<round>[1-9][0-9]*)\s*-->")]
    private static partial Regex RoundRegex();

    internal static string Marker(string kind, int round) =>
        $"<!-- agentbot:mr-automation:v1:{kind}-round-{round} -->";

    internal static string LimitMarker(string kind) =>
        $"<!-- agentbot:mr-automation:v1:{kind}-limit -->";

    internal static int UsedRounds(IEnumerable<GitLabNote> notes, string bot, string kind,
        IEnumerable<GitLabCommit>? commits = null)
    {
        var own = notes.Where(note => !note.System &&
            string.Equals(note.Author.Username, bot, StringComparison.OrdinalIgnoreCase)).ToList();
        var started = own.SelectMany(note => RoundRegex().Matches(note.Body))
            .Where(match => match.Groups["kind"].Value == kind)
            .Select(match => int.TryParse(match.Groups["round"].Value, out var round) ? round : Limit)
            .DefaultIfEmpty(0).Max();
        var legacyReviews = own.Count(note => note.Body.Contains("<!-- agentbot:mr-review:commit-", StringComparison.Ordinal) &&
                                             !RoundRegex().IsMatch(note.Body));
        // Old looping MRs must not receive a fresh allowance after rollout or a force-push.
        var legacyRepairs = commits?.Count(commit =>
            (commit.Message.StartsWith("Fix pipeline failure for MR #", StringComparison.Ordinal) ||
             commit.Message.StartsWith("Resolve merge conflicts for MR #", StringComparison.Ordinal)) &&
            commit.Message.Contains("Automatically generated fix by Agent Bot.", StringComparison.Ordinal)) ?? 0;
        return Math.Max(started, kind == "review" ? legacyReviews : Math.Max(legacyRepairs, legacyReviews >= Limit ? Limit : 0));
    }

    internal static bool LimitAlreadyReported(IEnumerable<GitLabNote> notes, string bot, string kind) =>
        notes.Any(note => !note.System && string.Equals(note.Author.Username, bot, StringComparison.OrdinalIgnoreCase) &&
                          note.Body.Contains(LimitMarker(kind), StringComparison.Ordinal));
}
