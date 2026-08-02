namespace Aiursoft.AgentBot.Configuration;

public static class ReplyLanguageText
{
    public static string Select(ReplyLanguage language, string english, string chinese) => language switch
    {
        ReplyLanguage.En => english,
        ReplyLanguage.Zh => chinese,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported reply language.")
    };

    public static string PromptInstruction(ReplyLanguage language) => Select(
        language,
        "MANDATORY OUTPUT LANGUAGE: English. Write all user-facing natural-language output in English. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        "MANDATORY OUTPUT LANGUAGE: Simplified Chinese (简体中文). You MUST write all user-facing natural-language output in Simplified Chinese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.");
}
