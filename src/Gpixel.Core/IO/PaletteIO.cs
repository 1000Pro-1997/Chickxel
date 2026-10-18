using System.Globalization;
using System.Text;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>
/// 팔레트 파일: <c>.hex</c>(한 줄에 한 색, <c>RRGGBB</c> 또는 <c>RRGGBBAA</c>) 읽기/쓰기,
/// GIMP <c>.gpl</c> 읽기, 이미지에서 쓰인 색 모으기.
/// </summary>
public static class PaletteIO
{
    /// <summary>확장자(.hex / .gpl)에 맞춰 읽는다.</summary>
    public static List<Color32> Load(string path)
    {
        var text = File.ReadAllText(path);
        return Path.GetExtension(path).Equals(".gpl", StringComparison.OrdinalIgnoreCase)
            ? ParseGpl(text)
            : ParseHex(text);
    }

    /// <summary>.hex 형식으로 저장한다(불투명 색은 6자리, 반투명은 8자리, '#' 없음).</summary>
    public static void SaveHex(string path, IEnumerable<Color32> colors)
        => File.WriteAllText(path, ToHex(colors), new UTF8Encoding(false));

    public static string ToHex(IEnumerable<Color32> colors)
    {
        var sb = new StringBuilder();
        foreach (var c in colors)
            sb.Append(c.ToHex().AsSpan(1)).Append('\n');
        return sb.ToString();
    }

    /// <summary>.hex 텍스트를 읽는다. 각 줄의 첫 토큰(앞의 '#' 허용)이 6/8자리 16진수가 아니면 주석으로 보고 건너뛴다.</summary>
    public static List<Color32> ParseHex(string text)
    {
        var list = new List<Color32>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith("//")) continue;
            var token = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
            var hex = token.TrimStart('#');
            if (hex.Length is 6 or 8 && Color32.TryParseHex(hex, out var c)) list.Add(c);
        }
        return list;
    }

    /// <summary>GIMP 팔레트(.gpl): "R G B [이름]" 줄을 읽는다.</summary>
    public static List<Color32> ParseGpl(string text)
    {
        var list = new List<Color32>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("GIMP", StringComparison.Ordinal)
                || line.StartsWith("Name:", StringComparison.Ordinal) || line.StartsWith("Columns:", StringComparison.Ordinal))
                continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (TryByte(parts[0], out var r) && TryByte(parts[1], out var g) && TryByte(parts[2], out var b))
                list.Add(new Color32(r, g, b));
        }
        return list;
    }

    /// <summary>
    /// 이미지에 쓰인 서로 다른 불투명/반투명 색을 사용 빈도 순으로 모은다(완전 투명 제외).
    /// </summary>
    public static List<Color32> ExtractFromImage(PixelImage image, int maxColors = 256)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var p in image.Pixels)
        {
            if (p.A == 0) continue;
            counts[p.Argb] = counts.GetValueOrDefault(p.Argb) + 1;
        }
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(Math.Max(0, maxColors))
            .Select(kv => new Color32(kv.Key))
            .ToList();
    }

    private static bool TryByte(string s, out byte value)
    {
        value = 0;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return false;
        value = (byte)Math.Clamp(v, 0, 255);
        return true;
    }
}
