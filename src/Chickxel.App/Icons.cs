using Avalonia.Platform;

namespace Chickxel.App;

/// <summary>
/// 도구 막대용 16×16 단색 점 그림 아이콘(<c>Assets/Tools/*.png</c>, Chickxel로 직접 고칠 수 있음).
/// 도구 막대에는 2배(<see cref="Size"/>), 메뉴에는 1배로 최근접 보간해 그린다.
/// </summary>
public static class Icons
{
    /// <summary>도구 막대에 그리는 크기(16px 그림의 2배).</summary>
    public const int Size = 32;

    private static readonly Dictionary<ToolKind, string> Files = new()
    {
        [ToolKind.Pencil] = "pencil",
        [ToolKind.Eraser] = "eraser",
        [ToolKind.Fill] = "fill",
        [ToolKind.Gradient] = "gradient",
        [ToolKind.Picker] = "picker",
        [ToolKind.Shape] = "shape",
        [ToolKind.Select] = "select",
        [ToolKind.EllipseSelect] = "select_ellipse",
        [ToolKind.Lasso] = "lasso",
        [ToolKind.MagicWand] = "wand",
        [ToolKind.Move] = "move",
        [ToolKind.Dodge] = "dodge",
        [ToolKind.Dither] = "dither",
        [ToolKind.ReplaceBrush] = "replace",
        [ToolKind.Pivot] = "pivot",
        [ToolKind.Slice] = "slice",
    };

    private static readonly Dictionary<string, Bitmap> Cache = new();

    public static Bitmap For(ToolKind tool) => Load(Files[tool]);

    /// <summary>파일 브라우저 열고 닫기 아이콘.</summary>
    public static Bitmap Folder => Load("folder");

    /// <summary>AI 채팅(Claude·Codex) 단추 아이콘(주황색 별, 16×16).</summary>
    public static Bitmap Claude => Load("claude");

    /// <summary>도구 막대 아래 저장 단추 아이콘(플로피 디스크).</summary>
    public static Bitmap Save => Load("save");

    /// <summary>도구 막대 아래 "저장한 내용으로 되돌리기" 단추 아이콘(시계 반대 방향 화살표).</summary>
    public static Bitmap Revert => Load("revert");

    private static Bitmap Load(string name)
    {
        if (Cache.TryGetValue(name, out var bmp)) return bmp;
        using var stream = AssetLoader.Open(new Uri($"avares://Chickxel.App/Assets/Tools/{name}.png"));
        bmp = new Bitmap(stream);
        Cache[name] = bmp;
        return bmp;
    }

    private static readonly Uri AppIconUri = new("avares://Chickxel.App/Assets/AppIcon/chickxel-256.png");

    /// <summary>창 아이콘(기본 병아리를 놓은 16×16 점 그림을 16배 키운 256×256).</summary>
    public static WindowIcon AppIcon()
    {
        using var stream = AssetLoader.Open(AppIconUri);
        return new WindowIcon(stream);
    }

    /// <summary>앱 아이콘 PNG 바이트(macOS Dock 아이콘용).</summary>
    public static byte[] AppIconPng()
    {
        using var stream = AssetLoader.Open(AppIconUri);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
