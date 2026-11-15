using System.Globalization;
using Chickxel.Core.Localization;
using Chickxel.Core.Tiles;
using RectangleF = System.Drawing.RectangleF;

namespace Chickxel.App.PlayTest;

/// <summary>움직임 테스트의 캐릭터로 고를 수 있는 열린 문서 하나: 같은 탭을 알아볼 열쇠, 목록에 보일 이름, 지금 내용으로 만든 시트 문서.</summary>
internal sealed record PlayCharacterSource(object Key, string Name, Func<Chickxel.Core.Model.Document?> Build);

/// <summary>움직임 테스트에 깔 수 있는 타일맵 하나: 목록에 보일 이름과 지금 내용을 읽는 함수(문서·레이어가 없어졌으면 null).</summary>
internal sealed record PlayMapSource(string Name, Func<PlayMap?> Read);

/// <summary>깔린 타일맵(바뀌지 않는 값): 레이어가 쓰는 타일셋(여러 개면 합친 것)과 레이어 내용(맵·격자 원점·숨긴 층).</summary>
internal sealed record PlayMap(Tileset Tileset, TilemapData Data)
{
    /// <summary>
    /// 처음 맵을 깔 때 지형으로 고를 층(기본 타일셋의 층 이름 문구 키): 탑뷰는 벽·물건·물, 플랫포머는 바닥.
    /// 층 이름은 타일셋을 만든 때의 언어이므로 지금 화면 언어·한국어·영어 문구 어느 것과 같아도 맞는다.
    /// </summary>
    private static readonly string[] TopDownTerrainKeys = [nameof(DomainStrings.Tile_FloorWalls), nameof(DomainStrings.Tile_FloorObjects), nameof(DomainStrings.TileBuiltIn_Water)];
    private static readonly string[] PlatformerTerrainKeys = [nameof(DomainStrings.Tile_FloorGround)];

    /// <summary>이 맵에서 처음에 지형으로 고를 층 Id(이름이 맞는 층이 없으면 빈 것).</summary>
    public HashSet<int> DefaultTerrain(bool topDown)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in topDown ? TopDownTerrainKeys : PlatformerTerrainKeys)
        {
            if (Translations.Get(nameof(DomainStrings), key, DomainStrings.ResourceManager, DomainStrings.Culture) is { } now) names.Add(now);
            foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en") })
                if (DomainStrings.ResourceManager.GetString(key, culture) is { } s) names.Add(s);
        }
        return [.. Tileset.Floors.Where(f => names.Contains(f.Name.Trim())).Select(f => f.Id)];
    }

    /// <summary>보이는 층의 그림이 닿는 사각형(px). 타일이 없으면 빈 것.</summary>
    public Rectangle Bounds => TileMapRenderer.PixelBounds(Tileset, Data.Map, Data.Origin, Data.ShowsFloor);

    /// <summary>층들 <paramref name="floors"/>(타일셋에 있는 층만, 숨긴 층 포함)의 타일이 차지한 칸들.</summary>
    public HashSet<Point> CellsOn(IEnumerable<int> floors)
    {
        var cells = new HashSet<Point>();
        foreach (int floor in floors)
        {
            if (Tileset.FindFloor(floor) is null) continue;
            foreach (var (at, cell) in Data.Map.Floor(floor))
            {
                var size = Tileset.Footprint(cell);
                for (int y = 0; y < size.Height; y++)
                    for (int x = 0; x < size.Width; x++)
                        cells.Add(new Point(at.X + x, at.Y + y));
            }
        }
        return cells;
    }

    /// <summary>칸 → 캔버스 사각형(px).</summary>
    public RectangleF CellRect(int x, int y, int w = 1, int h = 1)
        => new(Data.Origin.X + x * Tileset.CellWidth, Data.Origin.Y + y * Tileset.CellHeight, w * Tileset.CellWidth, h * Tileset.CellHeight);
}
