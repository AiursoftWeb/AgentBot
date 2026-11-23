namespace Aiursoft.AgentBot.Configuration;

public static class ReplyLanguageText
{
    public static string Select(ReplyLanguage language, string english, string chinese, string? croatian = null) => language switch
    {
        ReplyLanguage.Zh or ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => chinese,
        ReplyLanguage.Hr when croatian != null => croatian,
        _ => english
    };

    public static string PromptInstruction(ReplyLanguage language) => language switch
    {
        ReplyLanguage.En => "MANDATORY OUTPUT LANGUAGE: English. Write all user-facing natural-language output in English. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.EnGb => "MANDATORY OUTPUT LANGUAGE: British English. Write all user-facing natural-language output in British English. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Zh => "MANDATORY OUTPUT LANGUAGE: Simplified Chinese (简体中文). You MUST write all user-facing natural-language output in Simplified Chinese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.ZhTw => "MANDATORY OUTPUT LANGUAGE: Traditional Chinese (繁體中文 (台灣)). You MUST write all user-facing natural-language output in Traditional Chinese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.ZhHk => "MANDATORY OUTPUT LANGUAGE: Traditional Chinese (繁體中文 (香港)). You MUST write all user-facing natural-language output in Traditional Chinese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Ja => "MANDATORY OUTPUT LANGUAGE: Japanese (日本語). Write all user-facing natural-language output in Japanese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Ko => "MANDATORY OUTPUT LANGUAGE: Korean (한국어). Write all user-facing natural-language output in Korean. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Vi => "MANDATORY OUTPUT LANGUAGE: Vietnamese (Tiếng Việt). Write all user-facing natural-language output in Vietnamese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Th => "MANDATORY OUTPUT LANGUAGE: Thai (ภาษาไทย). Write all user-facing natural-language output in Thai. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.De => "MANDATORY OUTPUT LANGUAGE: German (Deutsch). Write all user-facing natural-language output in German. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Fr => "MANDATORY OUTPUT LANGUAGE: French (Français). Write all user-facing natural-language output in French. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Es => "MANDATORY OUTPUT LANGUAGE: Spanish (Español). Write all user-facing natural-language output in Spanish. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Ru => "MANDATORY OUTPUT LANGUAGE: Russian (Русский). Write all user-facing natural-language output in Russian. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.It => "MANDATORY OUTPUT LANGUAGE: Italian (Italiano). Write all user-facing natural-language output in Italian. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Pt => "MANDATORY OUTPUT LANGUAGE: Portuguese (Português). Write all user-facing natural-language output in Portuguese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.PtBr => "MANDATORY OUTPUT LANGUAGE: Brazilian Portuguese (Português do Brasil). Write all user-facing natural-language output in Brazilian Portuguese. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Ar => "MANDATORY OUTPUT LANGUAGE: Arabic (العربية). Write all user-facing natural-language output in Arabic. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Nl => "MANDATORY OUTPUT LANGUAGE: Dutch (Nederlands). Write all user-facing natural-language output in Dutch. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Sv => "MANDATORY OUTPUT LANGUAGE: Swedish (Svenska). Write all user-facing natural-language output in Swedish. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Pl => "MANDATORY OUTPUT LANGUAGE: Polish (Polski). Write all user-facing natural-language output in Polish. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Tr => "MANDATORY OUTPUT LANGUAGE: Turkish (Türkçe). Write all user-facing natural-language output in Turkish. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Ro => "MANDATORY OUTPUT LANGUAGE: Romanian (Română). Write all user-facing natural-language output in Romanian. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Da => "MANDATORY OUTPUT LANGUAGE: Danish (Dansk). Write all user-facing natural-language output in Danish. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Uk => "MANDATORY OUTPUT LANGUAGE: Ukrainian (Українська). Write all user-facing natural-language output in Ukrainian. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Id => "MANDATORY OUTPUT LANGUAGE: Indonesian (Bahasa Indonesia). Write all user-facing natural-language output in Indonesian. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Fi => "MANDATORY OUTPUT LANGUAGE: Finnish (Suomi). Write all user-facing natural-language output in Finnish. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Hi => "MANDATORY OUTPUT LANGUAGE: Hindi (हिन्दी). Write all user-facing natural-language output in Hindi. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.El => "MANDATORY OUTPUT LANGUAGE: Greek (Ελληνικά). Write all user-facing natural-language output in Greek. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        ReplyLanguage.Hr => "MANDATORY OUTPUT LANGUAGE: Croatian (Hrvatski). Write all user-facing natural-language output in Croatian. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written.",
        _ => "MANDATORY OUTPUT LANGUAGE: English. Write all user-facing natural-language output in English. Preserve code, identifiers, paths, commands, logs, and quoted text exactly as written."
    };

    public static string PlanHeader(ReplyLanguage language, int version) => language switch
    {
        ReplyLanguage.Zh => $"Agent 计划 v{version}",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => $"Agent 計劃 v{version}",
        ReplyLanguage.Hr => $"Plan agenta v{version}",
        ReplyLanguage.Ja => $"エージェント計画 v{version}",
        ReplyLanguage.Ko => $"에이전트 계획 v{version}",
        ReplyLanguage.De => $"Agent-Plan v{version}",
        ReplyLanguage.Fr => $"Plan de l'agent v{version}",
        ReplyLanguage.Es => $"Plan del agente v{version}",
        ReplyLanguage.Ru => $"План агента v{version}",
        ReplyLanguage.It => $"Piano dell'agente v{version}",
        ReplyLanguage.Pt or ReplyLanguage.PtBr => $"Plano do agente v{version}",
        ReplyLanguage.Pl => $"Plan agenta v{version}",
        ReplyLanguage.Tr => $"Ajan Planı v{version}",
        ReplyLanguage.Nl => $"Agentplan v{version}",
        ReplyLanguage.Sv => $"Agentplan v{version}",
        ReplyLanguage.Da => $"Agentplan v{version}",
        ReplyLanguage.Fi => $"Agenttisuunnitelma v{version}",
        ReplyLanguage.Uk => $"План агента v{version}",
        ReplyLanguage.Ro => $"Planul agentului v{version}",
        ReplyLanguage.El => $"Σχέδιο πράκτορα v{version}",
        ReplyLanguage.Ar => $"خطة الوكيل v{version}",
        ReplyLanguage.Hi => $"एजेंट योजना v{version}",
        ReplyLanguage.Th => $"แผนของเอเจนต์ v{version}",
        ReplyLanguage.Vi => $"Kế hoạch Agent v{version}",
        ReplyLanguage.Id => $"Rencana Agen v{version}",
        _ => $"Agent Plan v{version}"
    };

    public static string ApprovedLabel(ReplyLanguage language) => language switch
    {
        ReplyLanguage.Zh or ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => "已批准",
        ReplyLanguage.Hr => "odobren",
        ReplyLanguage.Ja => "承認済み",
        ReplyLanguage.Ko => "승인됨",
        ReplyLanguage.De => "genehmigt",
        ReplyLanguage.Fr => "approuvé",
        ReplyLanguage.Es => "aprobado",
        ReplyLanguage.Ru => "утвержден",
        ReplyLanguage.It => "approvato",
        ReplyLanguage.Pt or ReplyLanguage.PtBr => "aprovado",
        ReplyLanguage.Pl => "zatwierdzony",
        ReplyLanguage.Tr => "onaylandı",
        ReplyLanguage.Nl => "goedgekeurd",
        ReplyLanguage.Sv or ReplyLanguage.Da => "godkendt",
        ReplyLanguage.Fi => "hyväksytty",
        ReplyLanguage.Uk => "схвалено",
        ReplyLanguage.Ro => "aprobat",
        ReplyLanguage.El => "εγκρίθηκε",
        ReplyLanguage.Ar => "تمت الموافقة",
        ReplyLanguage.Hi => "स्वीकृत",
        ReplyLanguage.Th => "ได้รับการอนุมัติแล้ว",
        ReplyLanguage.Vi => "đã duyệt",
        ReplyLanguage.Id => "disetujui",
        _ => "approved"
    };

    public static string PlanApprovalNotice(ReplyLanguage language, string username, long noteId) => language switch
    {
        ReplyLanguage.Zh => $"已识别 @{username} 在评论（note {noteId}）中的批准指令。独立的实施 Worker 现在将执行已批准的计划。",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => $"已識別 @{username} 在評論（note {noteId}）中的批准指令。獨立的實施 Worker 現在將執行已批准的計劃。",
        ReplyLanguage.Hr => $"Odobrenje je prepoznato u komentaru korisnika @{username} (komentar {noteId}). Zaseban proces sada će provesti odobreni plan.",
        ReplyLanguage.Ja => $"@{username} さんのコメント（note {noteId}）から承認指示を確認しました。別の実装ワーカーが承認済み計画を実行します。",
        ReplyLanguage.Ko => $"@{username}님의 댓글(note {noteId})에서 승인 지시를 확인했습니다. 별도의 구현 워커가 승인된 계획을 실행합니다.",
        ReplyLanguage.De => $"Die Genehmigung wurde im Kommentar von @{username} (Note {noteId}) erkannt. Ein separater Worker führt nun den genehmigten Plan aus.",
        ReplyLanguage.Fr => $"L'approbation a été reconnue dans le commentaire de @{username} (note {noteId}). Un worker d'implémentation va maintenant exécuter le plan approuvé.",
        ReplyLanguage.Es => $"Se reconoció la aprobación en el comentario de @{username} (nota {noteId}). Un worker de implementación separado ejecutará ahora el plan aprobado.",
        ReplyLanguage.Ru => $"Инструкция об утверждении распознана в комментарии @{username} (note {noteId}). Отдельный исполнитель приступает к реализации плана.",
        _ => $"Approval was recognized from @{username}'s comment (note {noteId}). A separate implementation worker will now execute the approved plan."
    };

    public static string PlanNotApprovedNotice(ReplyLanguage language) => language switch
    {
        ReplyLanguage.Zh => "当前计划尚未获批。只有 Issue 作者或已配置的 Reviewer 才能通过明确且无条件的实施指令批准计划。",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => "當前計劃尚未獲批。只有 Issue 作者或已配置的 Reviewer 才能通過明確且無條件的實施指令批准計劃。",
        ReplyLanguage.Hr => "Trenutačni plan nije odobren. Samo autor prijave ili dodijeljeni pregledavatelj može ga odobriti jasnom i bezuvjetnom uputom za početak provedbe.",
        ReplyLanguage.Ja => "現在の計画はまだ承認されていません。Issue の作成者または設定されたレビュアーのみが明確な指示で計画を承認できます。",
        ReplyLanguage.Ko => "현재 계획은 아직 승인되지 않았습니다. 이슈 작성자 또는 지정된 검토자만 명확한 지시로 계획을 승인할 수 있습니다.",
        ReplyLanguage.De => "Der aktuelle Plan wurde nicht genehmigt. Nur der Autor oder zugewiesene Prüfer kann den Plan zur Implementierung freigeben.",
        ReplyLanguage.Fr => "Le plan actuel n'a pas été approuvé. Seul l'auteur de l'issue ou le réviseur désigné peut approuver le plan.",
        ReplyLanguage.Es => "El plan actual no ha sido aprobado. Solo el autor de la incidencia o el revisor asignado puede aprobar el plan.",
        ReplyLanguage.Ru => "Текущий план не утвержден. Только автор задачи или назначенный рецензент могут утвердить план.",
        _ => "The current plan was not approved. Only the issue author or configured reviewer can approve it with an explicit, unconditional instruction to begin implementation."
    };

    public static (string StateLabel, string WaitingText, string InstructionPrefix, string CommandExample) PlanWaitingElements(ReplyLanguage language) => language switch
    {
        ReplyLanguage.Zh => ("当前状态：", "等待批准。", "如需批准，请回复明确无歧义的指令，例如：", "批准当前计划，开始开发。"),
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => ("當前狀態：", "等待批准。", "如需批准，請回覆明確無歧義的指令，例如：", "批准當前計劃，開始開發。"),
        ReplyLanguage.Hr => ("Trenutačno stanje:", "čeka odobrenje.", "Za odobrenje odgovorite jasnom uputom, primjerice:", "Odobravam trenutačni plan. Započnite provedbu."),
        ReplyLanguage.Ja => ("現在の状態:", "承認待ち。", "承認する場合は、明確な指示で返信してください。例:", "現在の計画を承認し、実装を開始してください。"),
        ReplyLanguage.Ko => ("현재 상태:", "승인 대기 중.", "승인하려면 명확한 지침으로 답글을 남겨주세요. 예:", "현재 계획을 승인하고 구현을 시작하세요."),
        ReplyLanguage.De => ("Aktueller Status:", "wartet auf Genehmigung.", "Zur Genehmigung antworten Sie bitte mit einer eindeutigen Anweisung, z. B.:", "Aktuellen Plan genehmigen und mit der Implementierung beginnen."),
        ReplyLanguage.Fr => ("État actuel :", "en attente d'approbation.", "Pour approuver, veuillez répondre avec une consigne claire, par exemple :", "Approuver le plan actuel et commencer l'implémentation."),
        ReplyLanguage.Es => ("Estado actual:", "esperando aprobación.", "Para aprobar, responda con una instrucción clara, por ejemplo:", "Aprobar el plan actual y comenzar la implementación."),
        ReplyLanguage.Ru => ("Текущее состояние:", "ожидает утверждения.", "Для утверждения ответьте четкой инструкцией, например:", "Утвердить текущий план и начать реализацию."),
        _ => ("Current state:", "waiting for approval.", "To approve naturally, reply with an unambiguous instruction such as:", "Approve the current plan and start implementation.")
    };

    public static string DiscussionAddressedNotice(ReplyLanguage language) => language switch
    {
        ReplyLanguage.Zh or ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => "我已处理讨论中的问题。",
        ReplyLanguage.Hr => "Obradio sam točke iz rasprave.",
        ReplyLanguage.Ja => "ディスカッションの指摘事項に対応しました。",
        ReplyLanguage.Ko => "토론에서 제기된 사항을 처리했습니다.",
        ReplyLanguage.De => "Ich habe die Diskussionspunkte bearbeitet.",
        ReplyLanguage.Fr => "J'ai traité les points de discussion.",
        ReplyLanguage.Es => "He abordado los puntos de discusión.",
        ReplyLanguage.Ru => "Я рассмотрел замечания в обсуждении.",
        ReplyLanguage.It => "Ho affrontato i punti della discussione.",
        ReplyLanguage.Pt or ReplyLanguage.PtBr => "Tratei dos pontos da discussão.",
        ReplyLanguage.Pl => "Odpowiedziałem na punkty dyskusji.",
        ReplyLanguage.Tr => "Tartışmadaki konuları ele aldım.",
        ReplyLanguage.Nl => "Ik heb de discussiepunten behandeld.",
        ReplyLanguage.Sv => "Jag har hanterat diskussionspunkterna.",
        ReplyLanguage.Da => "Jeg har adresseret diskussionspunkterne.",
        ReplyLanguage.Fi => "Olen käsitellyt keskustelukohdat.",
        ReplyLanguage.Uk => "Я опрацював пункти обговорення.",
        ReplyLanguage.Ro => "Am abordat punctele de discuție.",
        ReplyLanguage.El => "Αντιμετώπισα τα σημεία της συζήτησης.",
        ReplyLanguage.Ar => "لقد تناولت نقاط المناقشة.",
        ReplyLanguage.Hi => "मैंने चर्चा के बिंदुओं का समाधान कर दिया है।",
        ReplyLanguage.Th => "ฉันได้จัดการประเด็นการสนทนาแล้ว",
        ReplyLanguage.Vi => "Tôi đã giải quyết các điểm thảo luận.",
        ReplyLanguage.Id => "Saya telah menangani poin-poin diskusi.",
        _ => "I have addressed the discussion points."
    };

    public static string IssueMrTitle(ReplyLanguage language, int issueIid, string issueTitle) => language switch
    {
        ReplyLanguage.Zh => $"修复 Issue #{issueIid}：{issueTitle}",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => $"修復 Issue #{issueIid}：{issueTitle}",
        ReplyLanguage.Hr => $"Ispravak za prijavu #{issueIid}: {issueTitle}",
        ReplyLanguage.Ja => $"課題 #{issueIid} の修正: {issueTitle}",
        ReplyLanguage.Ko => $"이슈 #{issueIid} 해결: {issueTitle}",
        ReplyLanguage.De => $"Behebung für Issue #{issueIid}: {issueTitle}",
        ReplyLanguage.Fr => $"Correction pour l'issue #{issueIid} : {issueTitle}",
        ReplyLanguage.Es => $"Corrección para la incidencia #{issueIid}: {issueTitle}",
        ReplyLanguage.Ru => $"Исправление для задачи #{issueIid}: {issueTitle}",
        _ => $"Fix for issue #{issueIid}: {issueTitle}"
    };

    public static string IssueMrBody(ReplyLanguage language, int issueIid, string issueTitle) => language switch
    {
        ReplyLanguage.Zh => $"""

由 Agent Bot 自动生成以修复 Issue #{issueIid}。

Closes #{issueIid}

## Issue
{issueTitle}
""",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => $"""

由 Agent Bot 自動生成以修復 Issue #{issueIid}。

Closes #{issueIid}

## Issue
{issueTitle}
""",
        ReplyLanguage.Hr => $"""

Automatski generirao Agent Bot radi ispravka prijave #{issueIid}.

Closes #{issueIid}

## Prijava
{issueTitle}
""",
        ReplyLanguage.Ja => $"""

課題 #{issueIid} を修正するために Agent Bot によって自動生成されました。

Closes #{issueIid}

## 課題
{issueTitle}
""",
        ReplyLanguage.Ko => $"""

이슈 #{issueIid}를 해결하기 위해 Agent Bot에 의해 자동 생성되었습니다.

Closes #{issueIid}

## 이슈
{issueTitle}
""",
        _ => $"""

Automatically generated by Agent Bot to fix issue #{issueIid}.

Closes #{issueIid}

## Issue
{issueTitle}
"""
    };

    public static string MrReplacementTitle(ReplyLanguage language, string oldMrTitle, int oldMrIid) => language switch
    {
        ReplyLanguage.Zh => $"[Bot 修复] {oldMrTitle}（替代 #{oldMrIid}）",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => $"[Bot 修復] {oldMrTitle}（替代 #{oldMrIid}）",
        ReplyLanguage.Hr => $"[Ispravak bota] {oldMrTitle} (zamjena za #{oldMrIid})",
        ReplyLanguage.Ja => $"[Bot 修正] {oldMrTitle} (#{oldMrIid} の代替)",
        ReplyLanguage.Ko => $"[Bot 수정] {oldMrTitle} (#{oldMrIid} 대체)",
        ReplyLanguage.De => $"[Bot-Fix] {oldMrTitle} (Ersatz für #{oldMrIid})",
        ReplyLanguage.Fr => $"[Bot Fix] {oldMrTitle} (Remplacement pour #{oldMrIid})",
        ReplyLanguage.Es => $"[Bot Fix] {oldMrTitle} (Reemplazo para #{oldMrIid})",
        ReplyLanguage.Ru => $"[Bot Исправление] {oldMrTitle} (Замена для #{oldMrIid})",
        _ => $"[Bot Fix] {oldMrTitle} (Replacement for #{oldMrIid})"
    };

    public static string MrReplacementBody(ReplyLanguage language, int oldMrIid, string aiOutput) => language switch
    {
        ReplyLanguage.Zh => $"""

该合并请求由 Agent Bot 自动生成，用于替换 #{oldMrIid}。

## 变更
此合并请求包含 Agent Bot 生成的自动化修复。

## AI CLI Context
```
{aiOutput}
```
""",
        ReplyLanguage.ZhTw or ReplyLanguage.ZhHk => $"""

該合併請求由 Agent Bot 自動生成，用於替換 #{oldMrIid}。

## 變更
此合併請求包含 Agent Bot 生成的自動化修復。

## AI CLI Context
```
{aiOutput}
```
""",
        ReplyLanguage.Hr => $"""

Ovaj zahtjev za spajanje automatski je generirao Agent Bot kako bi zamijenio #{oldMrIid}.

## Promjene
Ovaj zahtjev za spajanje sadrži automatizirane ispravke koje je generirao Agent Bot.

## AI CLI Context
```
{aiOutput}
```
""",
        _ => $"""

This merge request was automatically generated by Agent Bot to replace #{oldMrIid}.

## Changes
This merge request contains automated fixes generated by the Agent Bot.

## AI CLI Context
```
{aiOutput}
```
"""
    };
}
