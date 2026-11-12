using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Drawing;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tiles;

/// <summary>타일맵 레이어 안의 층 하나(아래 → 위 차례는 <see cref="Tileset.Floors"/>). 같은 층은 덮어 그리고, 다른 층은 겹쳐 그린다.</summary>
public sealed record TileFloor(int Id, string Name);

/// <summary>
/// 타일 하나. <see cref="Id"/>는 바뀌지 않는다(맵은 이것으로 가리킴). 차지 칸 <see cref="Width"/>×<see cref="Height"/>(칸 수)이 같은 층에서 덮어쓰기를 정하고,
/// 그림은 차지 칸보다 클 수 있다(<see cref="DrawOffset"/> — 차지 칸 왼쪽 위 기준 그림 왼쪽 위, px). 그림은 만든 뒤 고치지 않는다.
/// </summary>
public sealed record TileDef
{
    public required int Id { get; init; }
    public string Name { get; init; } = "";
    public required PixelImage Image { get; init; }
    /// <summary>차지 칸 가로(칸 수, 1 이상).</summary>
    public int Width { get; init; } = 1;
    /// <summary>차지 칸 세로(칸 수, 1 이상).</summary>
    public int Height { get; init; } = 1;
    /// <summary>차지 칸 왼쪽 위에서 그림 왼쪽 위까지(px). 위로 솟은 그림은 Y가 음수.</summary>
    public Point DrawOffset { get; init; }
    /// <summary>찍으면 들어갈 층(<see cref="TileFloor.Id"/>).</summary>
    public int Floor { get; init; }
    /// <summary>타일 목록 화면의 자리(칸). 시트에서 가져오면 시트의 칸 자리.</summary>
    public int PaletteX { get; init; }
    public int PaletteY { get; init; }
    /// <summary>
    /// 움직이는 타일의 그림들(차례대로, 모두 <see cref="Image"/>와 같은 크기이고 첫 그림이 <see cref="Image"/>). 2장 미만이면 멈춘 타일.
    /// </summary>
    public ImmutableArray<PixelImage> Frames { get; init; } = [];
    /// <summary>움직이는 타일의 그림 하나 시간(ms).</summary>
    public int FrameMs { get; init; } = DefaultFrameMs;

    public const int DefaultFrameMs = 150;
    public const int MinFrameMs = 16;
    public const int MaxFrames = 64;

    public bool IsAnimated => Frames.Length > 1;

    public int FrameCount => IsAnimated ? Frames.Length : 1;

    /// <summary>시각 <paramref name="timeMs"/>에 보일 그림 차례.</summary>
    public int FrameAt(long timeMs)
    {
        if (!IsAnimated) return 0;
        long step = Math.Max(MinFrameMs, FrameMs);
        long i = timeMs / step % Frames.Length;
        return (int)(i < 0 ? i + Frames.Length : i);
    }

    /// <summary>차례 <paramref name="frame"/>의 그림(멈춘 타일이면 늘 <see cref="Image"/>).</summary>
    public PixelImage FrameImage(int frame) => IsAnimated ? Frames[(frame % Frames.Length + Frames.Length) % Frames.Length] : Image;

    /// <summary>그림들을 바꾼 사본(첫 그림이 <see cref="Image"/>, 1장이면 멈춘 타일). 크기가 다르면 <see cref="ArgumentException"/>.</summary>
    public TileDef WithFrames(IReadOnlyList<PixelImage> frames, int frameMs)
    {
        if (frames.Count == 0) throw new ArgumentException(null, nameof(frames));
        if (frames.Count > MaxFrames) throw new ArgumentException(DomainStrings.FormatTile_TooManyFrames(max: MaxFrames), nameof(frames));
        if (frames.Any(f => f.Width != frames[0].Width || f.Height != frames[0].Height)) throw new ArgumentException(DomainStrings.Tile_FrameSizeMismatch, nameof(frames));
        return this with
        {
            Image = frames[0],
            Frames = frames.Count > 1 ? [.. frames] : [],
            FrameMs = Math.Max(MinFrameMs, frameMs),
        };
    }

    /// <summary>그림이 차지 칸 아래·왼쪽에 맞도록 한 <see cref="DrawOffset"/>(그림이 차지 칸보다 크면 위로 솟음).</summary>
    public static Point BottomAligned(int imageHeight, int footprintHeight, int cellHeight) => new(0, footprintHeight * cellHeight - imageHeight);
}

/// <summary>자동 타일 규칙 틀(새 세트나 "틀로 규칙 만들기"에서 규칙 목록을 만든다 — <see cref="AutoTileShapes"/>).</summary>
public enum AutoTilePreset
{
    /// <summary>가로 줄: 좌우 이웃, 4규칙(혼자·왼쪽 끝·가운데·오른쪽 끝).</summary>
    Row,
    /// <summary>세로 줄: 위아래 이웃, 4규칙.</summary>
    Column,
    /// <summary>4방향: 상하좌우 이웃, 16규칙.</summary>
    Edge4,
    /// <summary>3×3: 8방향 이웃, 13규칙(가장자리 8·가운데·안쪽 모서리 4).</summary>
    Box13,
    /// <summary>8방향: 8방향 이웃(모서리는 양옆이 이어질 때만), 47규칙.</summary>
    Blob47,
}

/// <summary>자동 타일 규칙 하나에 넣은 타일과 무작위 비중(1 이상).</summary>
public readonly record struct AutoTileChoice(int Tile, int Weight = 1);

/// <summary>
/// 규칙의 이웃 칸 조건: 상관없음, 같은 무리가 있어야 함, 같은 무리가 없어야 함(비었거나 다른 것),
/// 뭐라도 있어야 함(같은 무리이거나 세트가 정한 층(<see cref="AutoTileSet.FilledFloors"/>)에 어떤 타일이든 있음 — 벽에 닿은 물처럼).
/// </summary>
public enum NeighborCondition : byte { Any, Same, Other, Filled }

/// <summary>
/// 자동 타일 규칙: 둘레 8칸 조건(<see cref="Same"/>·<see cref="Other"/>·<see cref="Filled"/> — 이웃 비트, 셋 다 아니면 상관없음)이 맞으면 <see cref="Tiles"/> 중 하나를 놓는다.
/// 뒤집기·회전을 허용하면 조건을 뒤집거나 돌린 모양에도 맞추고, 그때는 타일도 같은 만큼 돌려 놓는다.
/// </summary>
public sealed record AutoTileRule
{
    /// <summary>같은 무리가 있어야 하는 이웃(비트).</summary>
    public byte Same { get; init; }
    /// <summary>같은 무리가 없어야 하는 이웃(비트, <see cref="Same"/>과 겹치지 않음).</summary>
    public byte Other { get; init; }
    /// <summary>뭐라도 있어야 하는 이웃(비트, 다른 둘과 겹치지 않음).</summary>
    public byte Filled { get; init; }
    public bool AllowFlipX { get; init; }
    public bool AllowFlipY { get; init; }
    public bool AllowRotate { get; init; }
    public ImmutableArray<AutoTileChoice> Tiles { get; init; } = [];

    public bool HasTiles => !Tiles.IsDefaultOrEmpty;

    /// <summary>이웃 <paramref name="bit"/>(0~7, <see cref="AutoTileShapes.Directions"/> 차례)의 조건.</summary>
    public NeighborCondition Condition(int bit)
        => (Same & (1 << bit)) != 0 ? NeighborCondition.Same
            : (Other & (1 << bit)) != 0 ? NeighborCondition.Other
            : (Filled & (1 << bit)) != 0 ? NeighborCondition.Filled
            : NeighborCondition.Any;

    /// <summary>이웃 <paramref name="bit"/>의 조건을 바꾼 사본.</summary>
    public AutoTileRule WithCondition(int bit, NeighborCondition condition)
    {
        byte b = (byte)(1 << bit);
        return this with
        {
            Same = condition == NeighborCondition.Same ? (byte)(Same | b) : (byte)(Same & ~b),
            Other = condition == NeighborCondition.Other ? (byte)(Other | b) : (byte)(Other & ~b),
            Filled = condition == NeighborCondition.Filled ? (byte)(Filled | b) : (byte)(Filled & ~b),
        };
    }

    /// <summary>이웃 모양(같은 무리 비트 <paramref name="mask"/>, 뭐라도 있는 비트 <paramref name="filled"/>)이 조건에 맞는지.</summary>
    public bool Matches(byte mask, byte filled) => Misses(mask, filled) == 0;

    /// <summary>맞지 않는 조건 수(가장 비슷한 규칙 고르기).</summary>
    public int Misses(byte mask, byte filled)
        => System.Numerics.BitOperations.PopCount((uint)((Same & ~mask) | (Other & mask) | (Filled & ~filled)) & 0xFF);

    /// <summary>조건을 뒤집고 돌린 사본(타일을 같은 만큼 돌리면 이 조건에 맞음).</summary>
    public AutoTileRule Transformed(TileTransform t)
        => t == TileTransform.None ? this : this with
        {
            Same = AutoTileShapes.Transform(Same, t),
            Other = AutoTileShapes.Transform(Other, t),
            Filled = AutoTileShapes.Transform(Filled, t),
        };
}

/// <summary>
/// 자동 타일 세트: 칸마다 둘레 8칸을 보고 <see cref="Rules"/>를 앞에서부터 맞춰 처음 맞는 규칙의 타일을 놓는다(맞는 것이 없으면 조건이 가장 덜 어긋난 규칙).
/// 이웃 = 어느 층이든 그 칸에 같은 무리(<see cref="Group"/>)의 세트로 찍은 타일. 무리가 비면 이 세트끼리만 이어진다.
/// </summary>
public sealed record AutoTileSet
{
    public required int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>찍을 층.</summary>
    public int Floor { get; init; }
    /// <summary>이어지는 무리 이름: 같은 이름의 세트끼리 서로 이웃으로 친다(흙과 진흙처럼). 비면 이 세트만.</summary>
    public string Group { get; init; } = "";
    /// <summary>"뭐라도 있음" 조건이 볼 층(이 층들에 어떤 타일이든 있으면 있음). 비면 모든 층. 이어지는 세트의 타일은 층과 상관없이 있음.</summary>
    public ImmutableArray<int> FilledFloors { get; init; } = [];
    public ImmutableArray<AutoTileRule> Rules { get; init; } = [];

    /// <summary>규칙 중 "뭐라도 있음" 조건을 쓰는 것이 있는지.</summary>
    public bool UsesFilled => Rules.Any(r => r.Filled != 0);

    /// <summary>"뭐라도 있음"이 층 <paramref name="floor"/>를 보는지.</summary>
    public bool CountsFloor(int floor) => FilledFloors.IsDefaultOrEmpty || FilledFloors.Contains(floor);

    /// <summary>이 세트가 <paramref name="other"/>와 이어지는지(같은 세트이거나 같은 무리).</summary>
    public bool JoinsWith(AutoTileSet other)
        => other.Id == Id || Group.Length > 0 && string.Equals(Group, other.Group, StringComparison.Ordinal);

    /// <summary>틀 <paramref name="preset"/>의 빈 규칙으로 만든 세트(뒤집기·회전 허용은 모든 규칙에 같이).</summary>
    public static AutoTileSet FromPreset(int id, string name, int floor, AutoTilePreset preset, bool flipX = false, bool flipY = false, bool rotate = false)
        => new()
        {
            Id = id,
            Name = name,
            Floor = floor,
            Rules = [.. AutoTileShapes.Rules(preset).Select(r => r with { AllowFlipX = flipX, AllowFlipY = flipY, AllowRotate = rotate })],
        };
}

/// <summary>
/// 타일셋: 칸 크기, 층 목록, 타일, 자동 타일 세트. 바뀌지 않는 값이고 고치면 새 값을 만든다(<c>with</c>).
/// <see cref="Key"/>는 프로젝트 안에서 타일맵 레이어가 가리키는 이름표, <see cref="SourcePath"/>는 따로 저장한 타일셋 파일(<see cref="FileExtension"/>) 경로.
/// </summary>
public sealed record Tileset
{
    public const string FileExtension = ".chickxel-tileset";
    public const int MaxCellSize = 1024;
    /// <summary>타일 하나의 차지 칸 최대(칸 수).</summary>
    public const int MaxFootprint = 32;

    public required string Key { get; init; }
    public string Name { get; init; } = "";
    public int CellWidth { get; init; } = 16;
    public int CellHeight { get; init; } = 16;
    public ImmutableArray<TileFloor> Floors { get; init; } = [];
    public ImmutableArray<TileDef> Tiles { get; init; } = [];
    public ImmutableArray<AutoTileSet> AutoTiles { get; init; } = [];
    /// <summary>타일셋 파일 경로(전체 경로). 없으면 프로젝트 안에만 있다.</summary>
    public string? SourcePath { get; init; }

    public Size CellSize => new(CellWidth, CellHeight);

    /// <summary>칸이 정사각형이라 90° 회전할 수 있는지.</summary>
    public bool CanRotate => CellWidth == CellHeight;

    /// <summary>새 이름표(겹치지 않는 문자열).</summary>
    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>빈 타일셋(기본 층: 바닥·장식·위).</summary>
    public static Tileset Create(string name, int cellWidth, int cellHeight)
    {
        ValidateCell(cellWidth, cellHeight);
        return new Tileset
        {
            Key = NewKey(),
            Name = name,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            Floors = [new TileFloor(1, DomainStrings.Tile_FloorGround), new TileFloor(2, DomainStrings.Tile_FloorDecor), new TileFloor(3, DomainStrings.Tile_FloorTop)],
        };
    }

    public static void ValidateCell(int cellWidth, int cellHeight)
    {
        if (cellWidth < 1 || cellHeight < 1 || cellWidth > MaxCellSize || cellHeight > MaxCellSize)
            throw new ArgumentException(DomainStrings.FormatTile_CellSizeRange(max: MaxCellSize));
    }

    // 찾기 표와 돌린 그림(만든 뒤 바뀌지 않으므로 처음 쓸 때 만든다 — with 사본은 새로 만듦)
    private Dictionary<int, TileDef>? _byId;
    private ConcurrentDictionary<(int Tile, TileTransform Transform, int Frame), PixelImage>? _transformed;
    private int? _maxSpan;
    private (long Step, long Period)? _animation;

    // with 사본이 찾기 표를 물려받지 않게(바뀐 타일 목록과 어긋남)
    private Tileset(Tileset other)
    {
        Key = other.Key;
        Name = other.Name;
        CellWidth = other.CellWidth;
        CellHeight = other.CellHeight;
        Floors = other.Floors;
        Tiles = other.Tiles;
        AutoTiles = other.AutoTiles;
        SourcePath = other.SourcePath;
    }

    // 값 비교는 쓰지 않는다(찾기 표·그림 사본까지 비교하게 되므로): 같은 객체일 때만 같다.
    public bool Equals(Tileset? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public Tileset()
    {
    }

    private Dictionary<int, TileDef> ById => _byId ??= Tiles.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());

    public TileDef? Find(int id) => ById.GetValueOrDefault(id);

    public TileFloor? FindFloor(int id) => Floors.FirstOrDefault(f => f.Id == id);

    public AutoTileSet? FindAutoTile(int id) => AutoTiles.FirstOrDefault(a => a.Id == id);

    /// <summary>층의 차례(아래 = 0). 없는 층이면 -1.</summary>
    public int FloorOrder(int floorId)
    {
        for (int i = 0; i < Floors.Length; i++)
            if (Floors[i].Id == floorId) return i;
        return -1;
    }

    /// <summary>있는 층이면 그대로, 아니면 첫 층(층이 없으면 0).</summary>
    public int ValidFloor(int floorId) => FloorOrder(floorId) >= 0 ? floorId : Floors.Length > 0 ? Floors[0].Id : 0;

    public int NextTileId => Tiles.Length == 0 ? 1 : Tiles.Max(t => t.Id) + 1;
    public int NextFloorId => Floors.Length == 0 ? 1 : Floors.Max(f => f.Id) + 1;
    public int NextAutoTileId => AutoTiles.Length == 0 ? 1 : AutoTiles.Max(a => a.Id) + 1;

    /// <summary>가장 큰 차지 칸 한 변(칸 수, 회전을 생각해 가로·세로 중 큰 쪽). 타일이 없으면 1.</summary>
    public int MaxSpan
    {
        get
        {
            _maxSpan ??= Tiles.Length == 0 ? 1 : Math.Max(1, Tiles.Max(t => Math.Max(t.Width, t.Height)));
            return _maxSpan.Value;
        }
    }

    /// <summary>칸에 놓인 타일의 차지 칸(돌리면 가로·세로가 바뀜). 없는 타일은 1×1.</summary>
    public Size Footprint(TileCell cell)
    {
        if (Find(cell.Tile) is not { } def) return new Size(1, 1);
        return cell.Transform.SwapsAxes() ? new Size(def.Height, def.Width) : new Size(def.Width, def.Height);
    }

    /// <summary>
    /// 칸 <paramref name="at"/>에 놓인 타일 그림의 자리(격자 원점 기준 px)와 돌리고 뒤집은 그림(움직이는 타일은 시각 <paramref name="timeMs"/>의 그림). 없는 타일이면 null.
    /// </summary>
    public (Rectangle Rect, PixelImage Image)? Placement(Point at, TileCell cell, long timeMs = 0)
    {
        if (Find(cell.Tile) is not { } def) return null;
        var box = new Size(def.Width * CellWidth, def.Height * CellHeight);
        var local = new Rectangle(def.DrawOffset, def.Image.Size);
        var (rect, _) = cell.Transform.ApplyToRect(local, box);
        rect.Offset(at.X * CellWidth, at.Y * CellHeight);
        return (rect, TransformedImage(def, cell.Transform, def.FrameAt(timeMs)));
    }

    /// <summary>돌리고 뒤집은 타일 그림(움직이는 타일은 차례 <paramref name="frame"/>의 그림, 같은 것은 한 번만 만듦, 어느 스레드에서나).</summary>
    public PixelImage TransformedImage(TileDef def, TileTransform transform, int frame = 0)
    {
        var image = def.FrameImage(frame);
        if (transform == TileTransform.None) return image;
        var cache = _transformed ??= new();
        return cache.GetOrAdd((def.Id, transform, def.IsAnimated ? frame % def.FrameCount : 0), _ => transform.ApplyToImage(image));
    }

    /// <summary>움직이는 타일이 있는지.</summary>
    public bool HasAnimation => Animation.Period > 0;

    /// <summary>
    /// 시각 <paramref name="timeMs"/>의 그림 묶음 번호: 같은 번호면 모든 움직이는 타일이 같은 그림(그린 결과를 다시 써도 됨). 움직이는 타일이 없으면 늘 0.
    /// </summary>
    public long AnimationPhase(long timeMs)
    {
        var (step, period) = Animation;
        if (period <= 0) return 0;
        long t = timeMs % period;
        if (t < 0) t += period;
        return t / step;
    }

    // 움직이는 타일 그림 시간들의 최대공약수(바뀌는 간격)와 최소공배수(한 바퀴, 너무 길면 자름)
    private (long Step, long Period) Animation
    {
        get
        {
            if (_animation is { } a) return a;
            long step = 0, period = 0;
            foreach (var t in Tiles)
            {
                if (!t.IsAnimated) continue;
                long ms = Math.Max(TileDef.MinFrameMs, t.FrameMs), cycle = ms * t.Frames.Length;
                step = step == 0 ? ms : Gcd(step, ms);
                period = period == 0 ? cycle : Math.Min(period / Gcd(period, cycle) * cycle, 3_600_000);
            }
            _animation = (step, period);
            return _animation.Value;
        }
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    /// <summary>회전이 칸 모양에 맞지 않으면(정사각형 칸이 아님) 회전을 뺀 것.</summary>
    public TileTransform Allowed(TileTransform t) => CanRotate ? t : t & ~TileTransform.Rotate90;
}
