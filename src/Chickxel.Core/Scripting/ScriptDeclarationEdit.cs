using Acornima;
using Acornima.Ast;

namespace Chickxel.Core.Scripting;

/// <summary>원문의 <c>effect({ … })</c> 선언을 글자 그대로 고친다(효과 만들기에서 정보 칸을 바꿀 때 — 나머지 원문은 손대지 않음).</summary>
public static class ScriptDeclarationEdit
{
    /// <summary>
    /// 선언의 <c>previewChick</c>을 <paramref name="chickId"/>로 바꾼다. 없으면 <c>name</c> 다음에 더한다(여러 줄 선언이면 같은 들여쓰기의 새 줄로).
    /// 원문을 해석하지 못하거나 선언 객체를 찾지 못하면 null.
    /// </summary>
    public static string? SetPreviewChick(string source, string chickId)
    {
        if (Declaration(source) is not { } declaration) return null;
        string literal = "\"" + chickId + "\"";
        var properties = declaration.Properties.OfType<ObjectProperty>().ToList();
        if (properties.FirstOrDefault(p => KeyName(p) == "previewChick") is { } existing)
            return string.Concat(source.AsSpan(0, existing.Value.Start), literal, source.AsSpan(existing.Value.End));
        if (properties.FirstOrDefault(p => KeyName(p) == "name") is not { } name) return null;

        int lineStart = source.LastIndexOf('\n', Math.Max(0, name.Start - 1)) + 1;
        bool ownLine = lineStart > declaration.Start && string.IsNullOrWhiteSpace(source[lineStart..name.Start]);
        int comma = name.End;
        while (comma < source.Length && source[comma] is ' ' or '\t') comma++;
        if (comma < source.Length && source[comma] == ',')
        {
            string insert = ownLine ? "\n" + source[lineStart..name.Start] + "previewChick: " + literal + "," : " previewChick: " + literal + ",";
            return source.Insert(comma + 1, insert);
        }
        return source.Insert(name.End, ", previewChick: " + literal);
    }

    private static ObjectExpression? Declaration(string source)
    {
        Script script;
        try
        {
            script = new Parser().ParseScript(source, "effect.js", strict: true);
        }
        catch (ParseErrorException)
        {
            return null;
        }
        foreach (var statement in script.Body)
        {
            if (statement is ExpressionStatement { Expression: CallExpression { Callee: Identifier { Name: "effect" } } call }
                && call.Arguments.Count == 1 && call.Arguments[0] is ObjectExpression declaration)
                return declaration;
        }
        return null;
    }

    private static string? KeyName(ObjectProperty property) => property switch
    {
        { Computed: false, Key: Identifier id } => id.Name,
        { Computed: false, Key: StringLiteral literal } => literal.Value,
        _ => null,
    };
}
