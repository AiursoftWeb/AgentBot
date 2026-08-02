using Aiursoft.AgentBot.Models;
using Aiursoft.AgentBot.Configuration;

namespace Aiursoft.AgentBot.Tests;

[TestClass]
public class CoreTests
{
    [TestMethod]
    public void ProcessResult_Succeeded_Works()
    {
        var result = ProcessResult.Succeeded("Test");
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public void ProcessResult_Failed_Works()
    {
        var result = ProcessResult.Failed("Test");
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void AgentBotOptions_HasDefaults()
    {
        var options = new AgentBotOptions();
        Assert.IsNotNull(options.WorkspaceFolder);
        Assert.AreEqual(ReplyLanguage.En, options.ReplyLanguage);
    }

    [TestMethod]
    [DataRow(null, ReplyLanguage.En)]
    [DataRow("", ReplyLanguage.En)]
    [DataRow("en", ReplyLanguage.En)]
    [DataRow(" EN ", ReplyLanguage.En)]
    [DataRow("zh", ReplyLanguage.Zh)]
    [DataRow(" ZH ", ReplyLanguage.Zh)]
    public void ParseReplyLanguage_NormalizesSupportedValues(string? configured, ReplyLanguage expected)
    {
        Assert.AreEqual(expected, AgentBotOptions.ParseReplyLanguage(configured));
    }

    [TestMethod]
    public void ParseReplyLanguage_RejectsUnsupportedValue()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => AgentBotOptions.ParseReplyLanguage("fr"));

        StringAssert.Contains(exception.Message, "BOT_REPLY_LANGUAGE");
        StringAssert.Contains(exception.Message, "en");
        StringAssert.Contains(exception.Message, "zh");
    }
}
