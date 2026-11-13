using Chickxel.Core.Tiles;

namespace Chickxel.App.PlayTest;

/// <summary>움직임 테스트(탑뷰)에 깔 수 있는 타일맵 하나: 목록에 보일 이름과 지금 내용을 읽는 함수(문서·레이어가 없어졌으면 null).</summary>
internal sealed record PlayMapSource(string Name, Func<PlayMap?> Read);

/// <summary>깔린 타일맵(바뀌지 않는 값): 레이어가 쓰는 타일셋(여러 개면 합친 것)과 레이어 내용(맵·격자 원점·숨긴 층).</summary>
internal sealed record PlayMap(Tileset Tileset, TilemapData Data)
{
    /// <summary>보이는 층의 그림이 닿는 사각형(px). 타일이 없으면 빈 것.</summary>
    public Rectangle Bounds => TileMapRenderer.PixelBounds(Tileset, Data.Map, Data.Origin, Data.ShowsFloor);
}
