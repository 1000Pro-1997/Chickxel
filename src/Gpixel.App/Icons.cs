namespace Gpixel.App;

/// <summary>도구 막대용 8×8 점 그림 아이콘(3배 확대해 24×24 비트맵으로 만든다).</summary>
public static class Icons
{
    public const int Scale = 3;
    public const int Size = 8 * Scale;

    private static readonly Dictionary<ToolKind, string[]> Patterns = new()
    {
        [ToolKind.Pencil] =
        [
            "......#.",
            ".....#a#",
            "....#a#.",
            "...#a#..",
            "..#a#...",
            ".#a#....",
            "#w#.....",
            "##......",
        ],
        [ToolKind.Eraser] =
        [
            "........",
            "...####.",
            "..#aaaa#",
            ".#aaaa#.",
            "#wwww#..",
            "#www#...",
            ".###....",
            "........",
        ],
        [ToolKind.Fill] =
        [
            "..#.....",
            ".#w#....",
            "#www#...",
            ".#www#..",
            "..#www#a",
            "...#w#.a",
            "....#..a",
            ".......a",
        ],
        [ToolKind.Picker] =
        [
            "......##",
            ".....###",
            "....###.",
            "...#a#..",
            "..#a#...",
            ".#a#....",
            "#a#.....",
            "#.......",
        ],
        [ToolKind.Line] =
        [
            "........",
            "......##",
            ".....##.",
            "....##..",
            "...##...",
            "..##....",
            ".##.....",
            "........",
        ],
        [ToolKind.Rectangle] =
        [
            "........",
            ".######.",
            ".#....#.",
            ".#....#.",
            ".#....#.",
            ".#....#.",
            ".######.",
            "........",
        ],
        [ToolKind.Ellipse] =
        [
            "........",
            "..####..",
            ".#....#.",
            ".#....#.",
            ".#....#.",
            ".#....#.",
            "..####..",
            "........",
        ],
        [ToolKind.Select] =
        [
            "##.##.##",
            "#......#",
            "........",
            "#......#",
            "#......#",
            "........",
            "#......#",
            "##.##.##",
        ],
        [ToolKind.Move] =
        [
            "...##...",
            "..####..",
            ".#.##.#.",
            "########",
            "########",
            ".#.##.#.",
            "..####..",
            "...##...",
        ],
        [ToolKind.Dodge] =
        [
            "...a....",
            ".a.a.a..",
            "..aaa...",
            "aaawaaa.",
            "..aaa...",
            ".a.a.a#.",
            "...a.###",
            "......#.",
        ],
        [ToolKind.Dither] =
        [
            "#.#.#.#.",
            ".#.#.#.#",
            "#.#.#.#.",
            ".#.#.#.#",
            "#.#.#.#.",
            ".#.#.#.#",
            "#.#.#.#.",
            ".#.#.#.#",
        ],
        [ToolKind.ReplaceBrush] =
        [
            "aaa.....",
            "aaa..#..",
            "aaa...#.",
            "..#####.",
            "......#.",
            ".....#ww",
            ".....www",
            ".....www",
        ],
        [ToolKind.Pivot] =
        [
            "...#....",
            "..aaa...",
            ".a.#.a..",
            "###w###.",
            ".a.#.a..",
            "..aaa...",
            "...#....",
            "........",
        ],
    };

    private static readonly string[] FolderPattern =
    [
        "........",
        "###.....",
        "#ww####.",
        "#wwwwww#",
        "#aaaaaa#",
        "#aaaaaa#",
        "########",
        "........",
    ];

    private static readonly Dictionary<ToolKind, Bitmap> Cache = new();
    private static Bitmap? _folder;

    public static Bitmap For(ToolKind tool)
    {
        if (Cache.TryGetValue(tool, out var bmp)) return bmp;
        bmp = Render(Patterns[tool]);
        Cache[tool] = bmp;
        return bmp;
    }

    /// <summary>파일 브라우저 열고 닫기 아이콘.</summary>
    public static Bitmap Folder => _folder ??= Render(FolderPattern);

    private static Bitmap Render(string[] pattern)
    {
        var bmp = new Bitmap(Size, Size);
        using var g = Graphics.FromImage(bmp);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                Color? c = pattern[y][x] switch
                {
                    '#' => Theme.TextColor,
                    'a' => Theme.AccentColor,
                    'w' => Color.FromArgb(170, 170, 178),
                    _ => null,
                };
                if (c is not { } col) continue;
                using var b = new SolidBrush(col);
                g.FillRectangle(b, x * Scale, y * Scale, Scale, Scale);
            }
        return bmp;
    }
}
