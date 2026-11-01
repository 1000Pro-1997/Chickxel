namespace Chickxel.Core.Scripting;

/// <summary>앱에 들어 있는 스크립트 효과 틀(<c>Scripting/Templates/*.js</c>). 기본 효과를 옮긴 공식 원문은 <see cref="OfficialEffects"/>.</summary>
public static class ScriptTemplates
{
    /// <summary>효과 만들기 "새 스크립트"의 처음 원문: 설정 하나와 픽셀마다 도는 반복문만 든 짧은 뼈대.</summary>
    public static string Starter { get; } = Read("starter.js");

    private static string Read(string name)
    {
        using var stream = typeof(ScriptTemplates).Assembly.GetManifestResourceStream("Chickxel.Scripting." + name)
                           ?? throw new InvalidOperationException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
