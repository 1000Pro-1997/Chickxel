using System.Drawing;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Tiles;

/// <summary>
/// 맵을 바이트로(프로젝트 파일 <c>tilemaps/L&lt;레이어&gt;.bin</c>, 실행취소 기록 조각). 리틀 엔디언:
/// "CXTM", 버전(1), 층 수, 층마다 (층 Id, 칸 수, 칸마다 X·Y·타일 Id·뒤집기 회전(1바이트)·자동 타일 세트 Id). 층·칸은 Id·(Y, X) 차례라 같은 맵은 같은 바이트.
/// </summary>
public static class TileMapCodec
{
    private static ReadOnlySpan<byte> Magic => "CXTM"u8;
    private const int Version = 1;
    /// <summary>한 맵의 칸 수 최대(손상된 파일이 메모리를 다 쓰지 않게).</summary>
    public const int MaxCells = 16_000_000;

    public static byte[] Encode(TileMap map)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Magic);
            w.Write(Version);
            var floors = map.FloorIds.Order().ToList();
            w.Write(floors.Count);
            foreach (int f in floors)
            {
                var cells = map.Floor(f).OrderBy(c => c.Key.Y).ThenBy(c => c.Key.X).ToList();
                w.Write(f);
                w.Write(cells.Count);
                foreach (var (at, cell) in cells)
                {
                    w.Write(at.X);
                    w.Write(at.Y);
                    w.Write(cell.Tile);
                    w.Write((byte)cell.Transform);
                    w.Write(cell.AutoTile);
                }
            }
        }
        return ms.ToArray();
    }

    /// <summary>바이트 → 맵. 형식이 틀리면 <see cref="InvalidDataException"/>.</summary>
    public static TileMap Decode(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes, writable: false));
            if (!r.ReadBytes(4).AsSpan().SequenceEqual(Magic) || r.ReadInt32() != Version) throw new InvalidDataException(DomainStrings.Tile_MapInvalid);
            int floors = r.ReadInt32();
            if (floors < 0 || floors > 4096) throw new InvalidDataException(DomainStrings.Tile_MapInvalid);
            var cells = new List<PlacedTile>();
            long total = 0;
            for (int i = 0; i < floors; i++)
            {
                int floor = r.ReadInt32();
                int count = r.ReadInt32();
                total += count;
                if (count < 0 || total > MaxCells) throw new InvalidDataException(DomainStrings.Tile_MapInvalid);
                for (int k = 0; k < count; k++)
                {
                    var at = new Point(r.ReadInt32(), r.ReadInt32());
                    int tile = r.ReadInt32();
                    var transform = (TileTransform)(r.ReadByte() & 7);
                    int auto = r.ReadInt32();
                    cells.Add(new PlacedTile(floor, at, new TileCell(tile, transform, auto)));
                }
            }
            return TileMap.FromCells(cells);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException(DomainStrings.Tile_MapInvalid, ex);
        }
    }
}
