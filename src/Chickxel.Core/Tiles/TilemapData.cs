using System.Collections.Immutable;
using System.Drawing;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 타일맵 레이어 내용: 쓰는 타일셋들(<see cref="Model.SpriteProject.Tilesets"/>의 이름표, 자리 차례 — 맵 Id 규칙은 <see cref="TileSources"/>), 맵,
/// 격자 원점(칸 (0,0) 왼쪽 위의 캔버스 좌표, px). 첫 타일셋(자리 0)이 칸 크기와 층을 정한다.
/// 바뀌지 않는 값(실행취소 사본은 참조만 담음).
/// </summary>
public sealed record TilemapData(ImmutableArray<string> TilesetKeys, TileMap Map, Point Origin)
{
    /// <summary>첫 타일셋(자리 0)의 이름표.</summary>
    public string PrimaryTilesetKey => TilesetKeys[0];

    /// <summary>이름표 <paramref name="key"/>의 자리(쓰지 않으면 -1).</summary>
    public int SlotOf(string key) => TilesetKeys.IndexOf(key);

    /// <summary>숨긴 층(합성·내보내기에서 그리지 않음).</summary>
    public ImmutableArray<int> HiddenFloors { get; init; } = [];

    /// <summary>잠근 층(타일 도구가 고치지 않음).</summary>
    public ImmutableArray<int> LockedFloors { get; init; } = [];

    public bool IsFloorHidden(int floor) => HiddenFloors.Contains(floor);

    public bool IsFloorLocked(int floor) => LockedFloors.Contains(floor);

    /// <summary>그릴 층인지(숨기지 않은 층).</summary>
    public bool ShowsFloor(int floor) => !HiddenFloors.Contains(floor);

    /// <summary>캔버스 좌표(px) → 칸(나눗셈은 내림).</summary>
    public Point CellAt(Tileset tileset, int x, int y)
        => new(FloorDiv(x - Origin.X, tileset.CellWidth), FloorDiv(y - Origin.Y, tileset.CellHeight));

    /// <summary>칸 사각형 → 캔버스 사각형(px).</summary>
    public Rectangle PixelRect(Tileset tileset, Rectangle cells)
        => new(Origin.X + cells.X * tileset.CellWidth, Origin.Y + cells.Y * tileset.CellHeight, cells.Width * tileset.CellWidth, cells.Height * tileset.CellHeight);

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}

/// <summary>화면의 층 보기: 모든 층 그대로, 고른 층 강조(다른 층은 흐리게), 고른 층만.</summary>
public enum TileFloorViewMode { All, Dim, Only }

/// <summary>
/// 화면에서 타일맵 레이어 <paramref name="Layer"/>의 층 <paramref name="Floor"/>에 맞춘 보기(<see cref="Model.SpriteProject.TileFloorView"/>, 저장하지 않음).
/// <paramref name="Mode"/>는 <see cref="TileFloorViewMode.Dim"/> 또는 <see cref="TileFloorViewMode.Only"/>.
/// </summary>
public sealed record TileFloorView(Model.Layer Layer, int Floor, TileFloorViewMode Mode)
{
    /// <summary>강조 보기에서 다른 층의 불투명도(0~255).</summary>
    public const byte DimOpacity = 80;
}
