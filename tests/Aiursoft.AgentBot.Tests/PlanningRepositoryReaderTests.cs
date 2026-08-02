using Aiursoft.AgentBot.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class PlanningRepositoryReaderTests
{
    [TestMethod]
    public async Task ReadAsync_ReadsTextAndSkipsBinaryAndSensitiveFiles()
    {
        var root = CreateDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "recognizable module information");
        await File.WriteAllBytesAsync(Path.Combine(root, "image.bin"), [1, 0, 2]);
        await File.WriteAllTextAsync(Path.Combine(root, ".env"), "SECRET=value");

        var snapshot = await CreateReader().ReadAsync(root);

        Assert.AreEqual("recognizable module information", snapshot.TextFiles["README.md"]);
        Assert.IsFalse(snapshot.TextFiles.ContainsKey("image.bin"));
        Assert.IsFalse(snapshot.TextFiles.ContainsKey(".env"));
    }

    [TestMethod]
    public void ResolveSafeFile_RejectsAbsoluteAndParentPaths()
    {
        var root = CreateDirectory();
        File.WriteAllText(Path.Combine(root, "safe.txt"), "safe");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanningRepositoryReader.ResolveSafeFile(root, "/etc/passwd"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanningRepositoryReader.ResolveSafeFile(root, "../outside.txt"));
    }

    [TestMethod]
    public void ResolveSafeFile_RejectsSymbolicLinkEscape()
    {
        var root = CreateDirectory();
        var outside = Path.Combine(Path.GetTempPath(), $"agentbot-outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "outside");
        var link = Path.Combine(root, "escape.txt");
        File.CreateSymbolicLink(link, outside);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            PlanningRepositoryReader.ResolveSafeFile(root, "escape.txt"));
    }

    [TestMethod]
    public async Task ReadAsync_MissingRepositoryReportsStructuredReason()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"agentbot-missing-{Guid.NewGuid():N}");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CreateReader().ReadAsync(missing));

        StringAssert.Contains(exception.Message, "[repository_missing]");
    }

    private static PlanningRepositoryReader CreateReader() =>
        new(Mock.Of<ILogger<PlanningRepositoryReader>>());

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentbot-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
