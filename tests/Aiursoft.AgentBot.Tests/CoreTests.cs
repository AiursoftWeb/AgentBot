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
    }
}
