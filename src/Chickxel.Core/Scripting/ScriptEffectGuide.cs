using System.Globalization;
using System.Text;
using Chickxel.Core.Assets;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 스크립트 효과를 만드는 사람·AI에게 주는 안내(Markdown): 지킬 규칙(선언 하나, 제자리 고치기, 결정론, 실행 한도, 설정 종류, 그라데이션을 따르는 색(권장), 빠른 경로, 설명·견본 병아리),
/// 답하는 방식, <see cref="ScriptApiCatalog.ToMarkdown"/>의 API 목록, 그리고 주면 지금 원문.
/// 규칙 글은 지금 화면 언어(<see cref="ScriptGuideStrings"/>), API 목록은 늘 영어다.
/// 효과 만들기의 "규칙" 창(복사해 다른 AI에 붙여 넣음)과 AI 도구(<c>get_script_api</c>, 효과 만들기 AI 칸)가 같이 쓴다.
/// </summary>
public static class ScriptEffectGuide
{
    /// <summary>안내 전체. <paramref name="currentSource"/>가 있으면 끝에 그 원문을 코드 블록으로 붙인다.</summary>
    public static string ToMarkdown(string? currentSource)
    {
        var limits = ScriptLimits.Default;
        var md = new StringBuilder();
        md.Append("# ").AppendLine(ScriptGuideStrings.Guide_Title);
        md.AppendLine();
        md.AppendLine(ScriptGuideStrings.Guide_Intro);
        md.AppendLine();
        md.Append("## ").AppendLine(ScriptGuideStrings.Guide_RulesHeading);
        md.AppendLine();
        Rule(md, ScriptGuideStrings.Guide_RuleOneFile);
        Rule(md, ScriptGuideStrings.FormatGuide_RuleDeclaration(
            nameMax: AssetManifest.MaxNameLength.ToString(CultureInfo.InvariantCulture),
            descriptionMax: ScriptEffectProgram.MaxDescriptionLength.ToString(CultureInfo.InvariantCulture),
            chicks: string.Join(", ", PreviewChicks.Ids.Select(id => $"\"{id}\"")),
            framesMin: ScriptEffectProgram.MinPreviewFrames.ToString(CultureInfo.InvariantCulture),
            framesMax: ScriptEffectProgram.MaxPreviewFrames.ToString(CultureInfo.InvariantCulture),
            framesDefault: AssetPreview.PackageFrames.ToString(CultureInfo.InvariantCulture)));
        Rule(md, ScriptGuideStrings.Guide_RuleRender);
        Rule(md, ScriptGuideStrings.Guide_RuleDeterministic);
        Rule(md, ScriptGuideStrings.FormatGuide_RuleLimits(
            seconds: limits.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture),
            statements: limits.MaxStatements.ToString("N0", CultureInfo.InvariantCulture),
            megabytes: (limits.MemoryBytes >> 20).ToString(CultureInfo.InvariantCulture),
            pixelStatements: limits.StatementsPerPixel.ToString("N0", CultureInfo.InvariantCulture),
            pixelMicroseconds: limits.TimePerPixel.TotalMicroseconds.ToString("0", CultureInfo.InvariantCulture),
            pixelKilobytes: (limits.MemoryPerPixel >> 10).ToString(CultureInfo.InvariantCulture)));
        Rule(md, ScriptGuideStrings.Guide_RuleSettings);
        Rule(md, ScriptGuideStrings.Guide_RuleGradient);
        Rule(md, ScriptGuideStrings.Guide_RuleAnimated);
        Rule(md, ScriptGuideStrings.Guide_RuleFast);
        Rule(md, ScriptGuideStrings.Guide_RuleLanguage);
        md.AppendLine();
        md.Append("## ").AppendLine(ScriptGuideStrings.Guide_AnswerHeading);
        md.AppendLine();
        md.AppendLine(ScriptGuideStrings.Guide_Answer);
        md.AppendLine();
        md.Append(ScriptApiCatalog.ToMarkdown(limits));
        if (currentSource is not null)
        {
            md.AppendLine();
            md.Append("## ").AppendLine(ScriptGuideStrings.Guide_SourceHeading);
            md.AppendLine();
            md.AppendLine(ScriptGuideStrings.Guide_SourceNote);
            md.AppendLine();
            AppendCode(md, ScriptEffectProgram.NormalizeSource(currentSource));
        }
        return md.ToString();
    }

    private static void Rule(StringBuilder md, string text) => md.Append("- ").AppendLine(text);

    /// <summary>
    /// 원문을 <c>js</c> 코드 블록으로 붙인다. 울타리는 원문 안의 가장 긴 백틱 줄보다 길게 해서(최소 3개) 원문에 <c>```</c>가 있어도 블록이 끊기지 않게 한다.
    /// </summary>
    internal static void AppendCode(StringBuilder md, string source)
    {
        int longest = 0, run = 0;
        foreach (char c in source)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        md.Append(fence).AppendLine("js");
        md.Append(source.TrimEnd('\n'));
        md.AppendLine();
        md.AppendLine(fence);
    }
}
