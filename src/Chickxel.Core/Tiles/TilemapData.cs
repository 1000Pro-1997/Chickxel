using System.Collections.Immutable;
using System.Drawing;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 타일맵 레이어 내용: 쓰는 타일셋(<see cref="Model.SpriteProject.Tilesets"/>의 이름표), 맵, 격자 원점(칸 (0,0) 왼쪽 위의 캔버스 좌표, px).
/// 바뀌지 않는 값(실행취소 사본은 참조만 담음).
/// </summary>
public sealed record TilemapData(string TilesetKey, TileMap Map, Point Origin)
{
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
