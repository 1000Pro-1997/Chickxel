using System.Text;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>저장한 적 없는 프로젝트를 처음 저장할 때 저장 대화상자에 미리 넣을 파일 이름.</summary>
public static class ProjectFileName
{
    /// <summary>임시 제목이 없을 때(또는 제목에서 파일 이름으로 쓸 글자가 남지 않을 때)의 이름.</summary>
    public const string DefaultBaseName = "sprite";

    /// <summary>어느 OS에서도 파일 이름에 못 쓰는 문자(제어 문자는 따로).</summary>
    private static readonly char[] BadFileChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>Windows가 확장자와 관계없이 파일 이름으로 받지 않는 장치 이름.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 임시 제목을 파일 이름 바탕(확장자 없음)으로: 파일 이름에 못 쓰는 문자(<c>&lt;&gt;:"/\|?*</c>, 제어 문자, 이 OS가 막는 문자)는 빼고
    /// 앞뒤 공백·끝 점을 없앤다. 남는 글자가 없거나 Windows 장치 이름(CON, NUL …)이면 <see cref="DefaultBaseName"/>.
    /// </summary>
    public static string BaseNameFor(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return DefaultBaseName;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var ch in title)
            if (!char.IsControl(ch) && Array.IndexOf(BadFileChars, ch) < 0 && Array.IndexOf(invalid, ch) < 0) sb.Append(ch);
        var name = sb.ToString().Trim().TrimEnd('.', ' ');
        if (name.Length == 0) return DefaultBaseName;
        int dot = name.IndexOf('.');
        return ReservedNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd()) ? DefaultBaseName : name;
    }

    /// <summary>
    /// 쓰이지 않은 첫 이름: <c>{바탕}{확장자}</c>, 있으면 <c>{바탕}_1{확장자}</c>, <c>{바탕}_2{확장자}</c> ….
    /// <paramref name="exists"/>는 그 이름(확장자 포함)이 이미 있는지.
    /// </summary>
    public static string FirstFree(string baseName, string extension, Func<string, bool> exists)
    {
        var name = baseName + extension;
        for (int n = 1; exists(name); n++) name = $"{baseName}_{n}{extension}";
        return name;
    }

    /// <summary>
    /// 저장 대화상자에 넣을 프로젝트 파일 이름: 임시 제목(<see cref="BaseNameFor"/>, 없으면 "sprite")에 <see cref="SpriteProject.FileExtension"/>을 붙이고,
    /// 대화상자가 열릴 폴더 <paramref name="folder"/>에 같은 이름(파일·폴더)이 있으면 <c>_1</c>, <c>_2</c> …를 붙인다(<see cref="FirstFree"/>).
    /// 폴더가 없거나 모르면 겹침을 보지 않는다.
    /// </summary>
    public static string Suggest(string? title, string? folder)
    {
        var baseName = BaseNameFor(title);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return baseName + SpriteProject.FileExtension;
        string dir = folder;
        return FirstFree(baseName, SpriteProject.FileExtension, name =>
        {
            var path = Path.Combine(dir, name);
            return File.Exists(path) || Directory.Exists(path);
        });
    }
}
