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
    [DataRow("en-gb", ReplyLanguage.EnGb)]
    [DataRow("zh", ReplyLanguage.Zh)]
    [DataRow(" ZH ", ReplyLanguage.Zh)]
    [DataRow("zh-cn", ReplyLanguage.Zh)]
    [DataRow("zh-tw", ReplyLanguage.ZhTw)]
    [DataRow("zh-hk", ReplyLanguage.ZhHk)]
    [DataRow("ja", ReplyLanguage.Ja)]
    [DataRow("ko", ReplyLanguage.Ko)]
    [DataRow("vi", ReplyLanguage.Vi)]
    [DataRow("th", ReplyLanguage.Th)]
    [DataRow("de", ReplyLanguage.De)]
    [DataRow("fr", ReplyLanguage.Fr)]
    [DataRow("es", ReplyLanguage.Es)]
    [DataRow("ru", ReplyLanguage.Ru)]
    [DataRow("it", ReplyLanguage.It)]
    [DataRow("pt", ReplyLanguage.Pt)]
    [DataRow("pt-br", ReplyLanguage.PtBr)]
    [DataRow("ar", ReplyLanguage.Ar)]
    [DataRow("nl", ReplyLanguage.Nl)]
    [DataRow("sv", ReplyLanguage.Sv)]
    [DataRow("pl", ReplyLanguage.Pl)]
    [DataRow("tr", ReplyLanguage.Tr)]
    [DataRow("ro", ReplyLanguage.Ro)]
    [DataRow("da", ReplyLanguage.Da)]
    [DataRow("uk", ReplyLanguage.Uk)]
    [DataRow("id", ReplyLanguage.Id)]
    [DataRow("fi", ReplyLanguage.Fi)]
    [DataRow("hi", ReplyLanguage.Hi)]
    [DataRow("el", ReplyLanguage.El)]
    [DataRow("hr", ReplyLanguage.Hr)]
    [DataRow(" hr-HR ", ReplyLanguage.Hr)]
    public void ParseReplyLanguage_NormalizesSupportedValues(string? configured, ReplyLanguage expected)
    {
        Assert.AreEqual(expected, AgentBotOptions.ParseReplyLanguage(configured));
    }

    [TestMethod]
    public void AllTwentyNineLanguages_HavePromptInstruction()
    {
        foreach (ReplyLanguage lang in Enum.GetValues<ReplyLanguage>())
        {
            var prompt = ReplyLanguageText.PromptInstruction(lang);
            Assert.IsFalse(string.IsNullOrWhiteSpace(prompt));
            StringAssert.Contains(prompt, "MANDATORY OUTPUT LANGUAGE");
            StringAssert.Contains(prompt, "Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.");
        }
    }

    [TestMethod]
    public void CroatianPromptKeepsNonProseContentUnchanged()
    {
        var prompt = ReplyLanguageText.PromptInstruction(ReplyLanguage.Hr);
        StringAssert.Contains(prompt, "Croatian (Hrvatski)");
        StringAssert.Contains(prompt, "Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.");
    }

    [TestMethod]
    public void ParseReplyLanguage_RejectsUnsupportedValue()
    {
        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => AgentBotOptions.ParseReplyLanguage("klingon-unknown"));

        StringAssert.Contains(exception.Message, "BOT_REPLY_LANGUAGE");
        StringAssert.Contains(exception.Message, "en");
        StringAssert.Contains(exception.Message, "zh");
    }
}
