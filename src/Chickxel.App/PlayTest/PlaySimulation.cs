using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Chickxel.Core.Model;
using Chickxel.Core.Play;
using Chickxel.Core.Tiles;
using RectangleF = System.Drawing.RectangleF;
using SizeF = System.Drawing.SizeF;
using Chickxel.App.Localization;

namespace Chickxel.App.PlayTest;

internal enum PlayMode
{
    TopDown,
    Platformer,
}

/// <summary>애니메이션 이름 줄 앞 표시: 지금 재생에 고른 이름, 있음, 다른 애니메이션으로 대신, 없음.</summary>
internal enum NameMark
{
    Chosen,
    Found,
    Substitute,
    Missing,
}

/// <summary>표시(<see cref="NameMark"/>)가 붙은 이름 한 줄.</summary>
internal readonly record struct MarkedName(NameMark Mark, string Text);

/// <summary>한 번의 갱신에 쓰는 키 입력. <see cref="JumpPressed"/>는 이번에 점프 키(스페이스)가 새로 눌렸을 때만 true.</summary>
internal readonly record struct PlayInput(bool Left, bool Right, bool Up, bool Down, bool JumpHeld, bool JumpPressed);

/// <summary>그리기 옵션.</summary>
internal readonly record struct PlayRenderOptions(int Scale, bool ShowGrid, bool ShowPivot, bool ShowCollider, bool HasFocus);

/// <summary>
/// 움직임 테스트의 상태: 탑뷰/플랫포머 컨트롤러, 맵, 애니메이션 재생기. 고정 간격으로 진행하고 화면에 그린다.
/// 위치는 픽셀 단위(y 아래가 +)이고 컨트롤러 위치는 몸 상자(기준 프레임의 불투명 영역)의 발밑 가운데다. 피벗은 거기서 몸 상자만큼 떨어진 곳이며,
/// 좌우 반전하면 몸 상자도 피벗을 기준으로 뒤집히므로 그림은 제자리에서 뒤집히고 피벗이 옮겨진다(피벗이 그림 가운데에서 멀어도 그림이 사라지지 않음).
/// 타일맵(<see cref="Map"/>)을 깔면 위치는 그 타일맵 레이어의 캔버스 좌표이고, 고른 지형 층의 칸은 벽·땅, 사다리 층의 칸은 사다리(플랫포머)가 된다.
/// 캐릭터는 피벗 높이를 기준선으로 세운 타일과 섞여 그려진다(위치로 그리는 타일셋 — 층 차례 타일셋이면 모든 타일 위).
/// </summary>
internal sealed class PlaySimulation
{
    private const float TopDownStep = 1f / 60f;
    private const float MaxFrameTime = 0.25f;
    private const int RoomCellsW = 20;
    private const int RoomCellsH = 14;

    private static readonly Color ColliderColor = Color.FromRgb(120, 220, 120);
    private static readonly IBrush ColliderFill = AppTheme.Brush(Color.FromArgb(40, 120, 220, 120));
    private static readonly IPen ColliderPen = AppTheme.Pen(ColliderColor);
    private static readonly IBrush TerrainFill = AppTheme.Brush(Color.FromArgb(50, 230, 120, 90));
    private static readonly IPen TerrainPen = AppTheme.Pen(Color.FromArgb(160, 230, 120, 90));
    private static readonly IBrush LadderFill = AppTheme.Brush(Color.FromArgb(50, 90, 160, 240));
    private static readonly IPen LadderPen = AppTheme.Pen(Color.FromArgb(180, 90, 160, 240));
    private static readonly IPen GridPen = AppTheme.Pen(Color.FromArgb(70, AppTheme.DimTextColor.R, AppTheme.DimTextColor.G, AppTheme.DimTextColor.B));
    private static readonly IPen RoomBorderPen = AppTheme.Pen(AppTheme.BorderColor, 2);
    private static readonly IPen PlatformEdgePen = AppTheme.Pen(AppTheme.BorderColor);
    private static readonly IPen OneWayTopPen = AppTheme.Pen(AppTheme.DimTextColor, 2);
    private static readonly IPen LadderRailPen = AppTheme.Pen(AppTheme.DimTextColor, 2);
    private static readonly IPen PivotShadowPen = AppTheme.Pen(Colors.Black, 3);
    private static readonly IPen PivotPen = AppTheme.Pen(AppTheme.AccentColor);
    private static readonly IPen MissingPen = AppTheme.Pen(AppTheme.WarningColor, 2);

    private readonly AnimationPlayer _player = new();
    private PlaySprites _sprites = PlaySprites.Empty;
    private float _topDownAccumulator;
    private int _levelPpu = -1;
    private PlayMap? _map;
    private IReadOnlySet<int> _terrainFloors = new HashSet<int>();
    private IReadOnlySet<int> _ladderFloors = new HashSet<int>();
    // 깔린 타일맵의 지형·사다리 칸(시작 자리 찾기에 씀)
    private HashSet<Point> _solidCells = new();
    private HashSet<Point> _ladderCells = new();
    // 깔린 타일맵의 움직이는 타일 시계(ms)
    private long _mapClockMs;
    // 타일 그림 → 화면 비트맵(그림은 만든 뒤 바뀌지 않으므로 그림마다 한 번)
    private readonly ConditionalWeakTable<PixelImage, WriteableBitmap> _tileBitmaps = new();

    /// <summary>피벗 기준 몸 상자(기준 프레임의 불투명 영역, 반전하지 않은 그림). 카메라·충돌 상자 크기의 기준.</summary>
    private RectangleF _box;

    private RectangleF _room;

    public PlayMode Mode { get; private set; } = PlayMode.TopDown;

    /// <summary>깐 타일맵(없으면 빈 방·연습용 발판).</summary>
    public PlayMap? Map => _map;
    public TopDownController TopDown { get; } = new();
    public PlatformerController Platformer { get; } = new();

    /// <summary>방향 없는 애니메이션(<c>idle</c>, <c>run</c> 등)이 오른쪽을 보고 그려졌는지.</summary>
    public bool PlainFacesRight { get; set; } = true;

    /// <summary>컨트롤러가 이름 규칙으로 고른 애니메이션(없으면 null).</summary>
    public AnimationChoice? Choice { get; private set; }

    /// <summary>지금 재생 중인 애니메이션(대체 포함, 정지 프레임이면 null).</summary>
    public AnimationClip? PlayingClip => _player.Clip;

    /// <summary>지금 그리는 프레임을 좌우 반전하는지.</summary>
    public bool FlipX { get; private set; }

    /// <summary>기준 프레임 높이(픽셀). 기본 배율을 정할 때 쓴다.</summary>
    public int ReferenceHeight { get; private set; }

    /// <summary>플랫포머에 사다리가 있는지(고른 사다리 층에 칸이 있거나 연습용 발판).</summary>
    public bool HasLadders => Platformer.Ladders.Count > 0;

    private bool FacingLeft => Mode == PlayMode.TopDown ? TopDown.Facing == Facing.Left : !Platformer.FacingRight;
    private bool FacingRight => Mode == PlayMode.TopDown ? TopDown.Facing == Facing.Right : Platformer.FacingRight;

    /// <summary>지금 그림의 몸 상자(피벗 기준): 반전해 그리면 피벗을 기준으로 뒤집은 것.</summary>
    private RectangleF Body => FlipX ? new RectangleF(-_box.Right, _box.Y, _box.Width, _box.Height) : _box;

    /// <summary>컨트롤러 위치(몸 상자 발밑 가운데).</summary>
    private Vector2 Feet => Mode == PlayMode.TopDown ? TopDown.Position : Platformer.Position;

    /// <summary>피벗이 놓이는 캐릭터 위치(픽셀).</summary>
    public Vector2 PivotPosition
    {
        get
        {
            var b = Body;
            return Feet - new Vector2(b.X + b.Width / 2, b.Bottom);
        }
    }

    /// <summary>지금 그릴 칸 이름(정지 프레임 포함). 없으면 null.</summary>
    public string? CurrentSliceName => _player.Clip is null ? _sprites.StillFrame : _player.CurrentFrame?.Slice;

    /// <summary>새 문서 스냅숏을 쓴다. 격자 크기(PPU)가 바뀌면 맵을 다시 만들고 처음 위치로 돌아간다.</summary>
    public void Load(PlaySprites sprites)
    {
        _sprites = sprites;
        ComputeReferenceBox();
        bool levelChanged = _levelPpu != sprites.PixelsPerUnit;
        BuildLevel();
        if (levelChanged) Reset();
        else ClampTopDown();
        _player.Play(null);
        UpdateAnimation(0);
    }

    public void SetMode(PlayMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        _player.Play(null);
        UpdateAnimation(0);
    }

    /// <summary>
    /// 깔 타일맵과 지형·사다리로 쓸 층을 바꾼다(맵이 null이면 빈 방·연습용 발판). <paramref name="restart"/>면 처음 위치로,
    /// 아니면 자리를 지키고 새 범위 안으로만 옮긴다(같은 맵을 다시 읽을 때).
    /// </summary>
    public void SetMap(PlayMap? map, IReadOnlySet<int> terrainFloors, IReadOnlySet<int> ladderFloors, bool restart)
    {
        _map = map;
        _terrainFloors = terrainFloors;
        _ladderFloors = ladderFloors;
        BuildLevel();
        if (restart) Reset();
        else ClampTopDown();
    }

    /// <summary>두 모드 모두 처음 위치로 돌린다(타일맵을 깔았으면 탑뷰는 가운데 가까운 빈 곳, 플랫포머는 가운데 가까운 땅 위).</summary>
    public void Reset()
    {
        TopDown.Position = TopDownSpawn();
        ClampTopDown();
        TopDown.Facing = Facing.Down;
        _topDownAccumulator = 0;
        Platformer.FacingRight = true;
        Platformer.Respawn();
        _player.Play(null);
        UpdateAnimation(0);
    }

    /// <summary>실제 경과 시간(초)만큼 진행한다.</summary>
    public void Tick(float dt, PlayInput input)
    {
        dt = Math.Clamp(dt, 0, MaxFrameTime);
        _mapClockMs += (long)(dt * 1000);
        if (Mode == PlayMode.TopDown)
        {
            _topDownAccumulator += dt;
            var dir = new DirectionalInput(input.Left, input.Right, input.Up, input.Down);
            while (_topDownAccumulator >= TopDownStep)
            {
                TopDown.Update(TopDownStep, dir);
                _topDownAccumulator -= TopDownStep;
            }
        }
        else
        {
            Platformer.Update(dt, new PlatformerInput(input.Left, input.Right, input.JumpHeld, input.JumpPressed, input.Up, input.Down));
        }
        UpdateAnimation(dt * 1000.0);
    }

    private void UpdateAnimation(double deltaMs)
    {
        Choice = Mode == PlayMode.TopDown
            ? TopDown.SelectAnimation(_sprites.HasClip, PlainFacesRight)
            : Platformer.SelectAnimation(_sprites.HasClip, PlainFacesRight);

        AnimationClip? clip;
        if (Choice is { } choice)
        {
            clip = _sprites.GetClip(choice.Name);
            FlipX = choice.FlipX;
            // 매달려 멈추면 오르는 애니메이션으로 대신할 때 그 프레임에서 멈춘다.
            if (Mode == PlayMode.Platformer && Platformer.State is PlatformerState.Hang or PlatformerState.WallHang
                && !choice.Name.Contains("hang", StringComparison.Ordinal) && !choice.Name.StartsWith("idle", StringComparison.Ordinal))
                deltaMs = 0;
        }
        else
        {
            // 이름 규칙에 맞는 것이 없으면 첫 애니메이션(없으면 정지 프레임)을 방향 없는 그림처럼 쓴다.
            clip = _sprites.ClipNames.Count > 0 ? _sprites.GetClip(_sprites.ClipNames[0]) : null;
            FlipX = FacingLeft ? PlainFacesRight : FacingRight && !PlainFacesRight;
        }
        _player.Play(clip);
        _player.Update(deltaMs);
    }

    private void ComputeReferenceBox()
    {
        int ppu = _sprites.PixelsPerUnit;
        _box = new RectangleF(-ppu * 0.4f, -ppu, ppu * 0.8f, ppu);
        ReferenceHeight = ppu;

        string? slice = null;
        var idle = AnimationSelector.Resolve(["idle"], Facing.Down, _sprites.HasClip, PlainFacesRight)
                   ?? AnimationSelector.Resolve(["idle"], Facing.Right, _sprites.HasClip, PlainFacesRight);
        string? clipName = idle?.Name ?? (_sprites.ClipNames.Count > 0 ? _sprites.ClipNames[0] : null);
        if (clipName is not null) slice = _sprites.GetClip(clipName)?.Frames[0].Slice;
        slice ??= _sprites.StillFrame;
        if (slice is null || _sprites.GetFrame(slice, false) is not { } frame) return;

        var o = frame.Opaque;
        _box = new RectangleF(o.X - frame.PivotX, o.Y - frame.PivotY, o.Width, o.Height);
        ReferenceHeight = frame.Height;
    }

    /// <summary>플랫포머 충돌 상자(발밑 가운데 기준): 몸 상자 크기.</summary>
    private RectangleF PlatformerBody => new(-Platformer.ColliderSize.Width / 2, -Platformer.ColliderSize.Height, Platformer.ColliderSize.Width, Platformer.ColliderSize.Height);

    private void BuildLevel()
    {
        int c = _sprites.PixelsPerUnit;
        _levelPpu = c;
        float w = Math.Max(1, _box.Width), h = Math.Max(1, _box.Height);
        Platformer.ColliderSize = new SizeF(w, h);
        // 탑뷰는 발 부분만 부딪친다(세운 벽 앞을 지나갈 때 머리가 걸리지 않게).
        float feet = Math.Min(h, Math.Max(2, (_map?.Tileset.CellHeight ?? c) / 2f));
        TopDown.Collider = new RectangleF(-w / 2, -feet, w, feet);

        var walls = TopDown.Walls;
        var platforms = Platformer.Platforms;
        var ladders = Platformer.Ladders;
        walls.Clear();
        platforms.Clear();
        ladders.Clear();
        _solidCells = new HashSet<Point>();
        _ladderCells = new HashSet<Point>();

        if (_map is { } map)
        {
            _solidCells = map.CellsOn(_terrainFloors);
            _ladderCells = map.CellsOn(_ladderFloors);
            _ladderCells.ExceptWith(_solidCells);
            // 지형은 가로로 이어진 칸을, 사다리는 세로로 이어진 칸을 한 상자로 묶는다.
            foreach (var r in Runs(_solidCells, horizontal: true))
            {
                var px = map.CellRect(r.X, r.Y, r.Width, r.Height);
                walls.Add(px);
                platforms.Add(new PlatformBox(px));
            }
            foreach (var r in Runs(_ladderCells, horizontal: false))
                ladders.Add(map.CellRect(r.X, r.Y, r.Width, r.Height));

            var bounds = map.Bounds is { Width: > 0, Height: > 0 } mb ? new RectangleF(mb.X, mb.Y, mb.Width, mb.Height) : RectangleF.Empty;
            foreach (var cells in new[] { _solidCells, _ladderCells })
                if (CellBounds(cells) is { Width: > 0 } cb)
                {
                    var px = map.CellRect(cb.X, cb.Y, cb.Width, cb.Height);
                    bounds = bounds.IsEmpty ? px : RectangleF.Union(bounds, px);
                }
            _room = bounds.IsEmpty ? DefaultRoom(c) : bounds;
            Platformer.SpawnPoint = PlatformerSpawn(map);
            Platformer.KillY = _room.Bottom + 10 * map.Tileset.CellHeight;
        }
        else
        {
            _room = DefaultRoom(c);
            void Add(float x, float y, float bw, float bh, bool oneWay = false)
                => platforms.Add(new PlatformBox(new RectangleF(x * c, y * c, bw * c, bh * c), oneWay));
            Add(-24, 0, 48, 3);              // 바닥
            Add(-25, -16, 1, 19);            // 왼쪽 벽
            Add(24, -16, 1, 19);             // 오른쪽 벽
            Add(-16, -2, 3, 2);              // 바닥 위 상자
            Add(-9, -4, 5, 0.5f, true);      // 위에서만 밟히는 발판
            Add(3, -3, 5, 0.5f, true);
            Add(10, -6, 4, 1);               // 단단한 발판
            Add(16, -9, 4, 0.5f, true);
            ladders.Add(new RectangleF(9 * c, -6 * c, c, 6 * c)); // 단단한 발판 옆 사다리
            Platformer.SpawnPoint = Vector2.Zero;
            Platformer.KillY = 40 * c;
        }

        // 몸 상자가 방 안에 머물도록 발밑 가운데가 움직일 범위를 줄인다.
        float left = _room.Left + w / 2, right = _room.Right - w / 2;
        float top = _room.Top + h, bottom = _room.Bottom;
        if (right < left) left = right = (left + right) / 2;
        if (bottom < top) top = bottom = (top + bottom) / 2;
        TopDown.Bounds = RectangleF.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF DefaultRoom(int c) => new(-RoomCellsW / 2f * c, -RoomCellsH / 2f * c, RoomCellsW * c, RoomCellsH * c);

    /// <summary>칸들을 가로(또는 세로)로 이어진 줄 사각형(칸 단위)으로 묶는다.</summary>
    private static List<Rectangle> Runs(HashSet<Point> cells, bool horizontal)
    {
        var sorted = cells.OrderBy(p => horizontal ? p.Y : p.X).ThenBy(p => horizontal ? p.X : p.Y).ToList();
        var runs = new List<Rectangle>();
        int i = 0;
        while (i < sorted.Count)
        {
            var start = sorted[i];
            int len = 1;
            while (i + len < sorted.Count && sorted[i + len] == (horizontal ? new Point(start.X + len, start.Y) : new Point(start.X, start.Y + len))) len++;
            runs.Add(horizontal ? new Rectangle(start.X, start.Y, len, 1) : new Rectangle(start.X, start.Y, 1, len));
            i += len;
        }
        return runs;
    }

    private static Rectangle CellBounds(HashSet<Point> cells)
    {
        if (cells.Count == 0) return Rectangle.Empty;
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var p in cells)
        {
            l = Math.Min(l, p.X);
            t = Math.Min(t, p.Y);
            r = Math.Max(r, p.X);
            b = Math.Max(b, p.Y);
        }
        return Rectangle.FromLTRB(l, t, r + 1, b + 1);
    }

    /// <summary>탑뷰 처음 위치(발밑 가운데): 방 가운데, 지형에 걸리면 가운데에서 가까운 빈 칸.</summary>
    private Vector2 TopDownSpawn()
    {
        if (_map is not { } map) return new Vector2(_box.X + _box.Width / 2, _box.Bottom);
        var center = new Vector2(_room.X + _room.Width / 2, _room.Y + _room.Height / 2 + _box.Height / 2);
        if (IsFree(map, center, TopDown.Collider)) return center;
        foreach (var cell in CellsByDistance(map, center))
        {
            var at = CellFeet(map, cell);
            if (IsFree(map, at, TopDown.Collider)) return at;
        }
        return center;
    }

    /// <summary>플랫포머 처음 위치(발밑 가운데): 방 가운데에서 가까운, 몸이 들어가는 빈 칸 중 바로 아래가 지형(또는 사다리)인 곳. 없으면 빈 칸, 그것도 없으면 가운데.</summary>
    private Vector2 PlatformerSpawn(PlayMap map)
    {
        var center = new Vector2(_room.X + _room.Width / 2, _room.Y + _room.Height / 2);
        if (_solidCells.Count == 0 && _ladderCells.Count == 0) return center;
        Vector2? free = null;
        foreach (var cell in CellsByDistance(map, center))
        {
            var at = CellFeet(map, cell);
            if (!IsFree(map, at, PlatformerBody)) continue;
            var below = new Point(cell.X, cell.Y + 1);
            if (_solidCells.Contains(below) || _ladderCells.Contains(below) && !_ladderCells.Contains(cell)) return at;
            free ??= at;
        }
        return free ?? center;
    }

    /// <summary>칸의 아래 가운데(발을 둘 곳).</summary>
    private static Vector2 CellFeet(PlayMap map, Point cell)
    {
        var r = map.CellRect(cell.X, cell.Y);
        return new Vector2(r.X + r.Width / 2, r.Bottom);
    }

    /// <summary>방(한 칸 여유) 안의 칸들을 <paramref name="center"/>에서 가까운 차례로.</summary>
    private IEnumerable<Point> CellsByDistance(PlayMap map, Vector2 center)
    {
        int cw = map.Tileset.CellWidth, ch = map.Tileset.CellHeight;
        var o = map.Data.Origin;
        int x0 = (int)MathF.Floor((_room.Left - o.X) / cw), x1 = (int)MathF.Ceiling((_room.Right - o.X) / cw);
        int y0 = (int)MathF.Floor((_room.Top - o.Y) / ch) - 1, y1 = (int)MathF.Ceiling((_room.Bottom - o.Y) / ch);
        if ((long)(x1 - x0) * (y1 - y0) > 1_000_000) return [];
        var list = new List<Point>((x1 - x0) * (y1 - y0));
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                list.Add(new Point(x, y));
        return list.OrderBy(p => Vector2.DistanceSquared(CellFeet(map, p) - new Vector2(0, ch / 2f), center));
    }

    /// <summary>발밑 가운데 <paramref name="feet"/>에 상자 <paramref name="box"/>(발 기준)를 두었을 때 지형 칸에 걸리지 않는지.</summary>
    private bool IsFree(PlayMap map, Vector2 feet, RectangleF box)
    {
        int cw = map.Tileset.CellWidth, ch = map.Tileset.CellHeight;
        var o = map.Data.Origin;
        float l = feet.X + box.Left - o.X, r = feet.X + box.Right - o.X, t = feet.Y + box.Top - o.Y, b = feet.Y + box.Bottom - o.Y;
        int x0 = (int)MathF.Floor(l / cw), x1 = (int)MathF.Floor((r - 0.001f) / cw);
        int y0 = (int)MathF.Floor(t / ch), y1 = (int)MathF.Floor((b - 0.001f) / ch);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (_solidCells.Contains(new Point(x, y))) return false;
        return true;
    }

    private void ClampTopDown()
    {
        if (TopDown.Bounds is not { } b) return;
        var pos = TopDown.Position;
        TopDown.Position = new Vector2(Math.Clamp(pos.X, b.Left, b.Right), Math.Clamp(pos.Y, b.Top, b.Bottom));
    }

    // ───────────────────────── 그리기 ─────────────────────────

    public void Render(DrawingContext g, Avalonia.Size viewSize, PlayRenderOptions options)
    {
        var full = new Rect(viewSize);
        g.FillRectangle(AppTheme.BackBrush, full);
        if (!_sprites.HasDocument)
        {
            var ft = AppTheme.Text(UiStrings.S_PlaySimulation_Render_80BEB72BD9, AppTheme.DimTextBrush);
            g.DrawText(ft, new Avalonia.Point((viewSize.Width - ft.Width) / 2, (viewSize.Height - ft.Height) / 2));
            return;
        }

        var size = new Size((int)viewSize.Width, (int)viewSize.Height);
        int scale = Math.Max(1, options.Scale);
        var pivot = PivotPosition;
        var feet = Feet;
        // 카메라는 몸 상자 가운데를 정수 픽셀로 따라간다(격자·발판이 화면 픽셀에 딱 맞도록).
        int camX = (int)MathF.Floor(feet.X + 0.5f);
        int camY = (int)MathF.Floor(feet.Y - _box.Height / 2 + 0.5f);
        int ox = size.Width / 2 - camX * scale;
        int oy = size.Height / 2 - camY * scale;
        Rect ToScreen(RectangleF r) => new(ox + r.X * scale, oy + r.Y * scale, r.Width * scale, r.Height * scale);

        int cellPx = _sprites.PixelsPerUnit * scale;
        if (_map is { } map)
        {
            var room = ToScreen(_room);
            DrawMapWithCharacter(g, map, size, ox, oy, scale, pivot);
            if (options.ShowGrid)
            {
                using (g.PushClip(room))
                    DrawGrid(g, size, ox + map.Data.Origin.X * scale, oy + map.Data.Origin.Y * scale, map.Tileset.CellWidth * scale);
            }
            if (options.ShowCollider)
            {
                // 지형 칸과 (플랫포머) 사다리 칸
                foreach (var wall in TopDown.Walls)
                {
                    var r = ToScreen(wall);
                    g.FillRectangle(TerrainFill, r);
                    g.DrawRectangle(null, TerrainPen, Crisp(r));
                }
                if (Mode == PlayMode.Platformer)
                    foreach (var ladder in Platformer.Ladders)
                    {
                        var r = ToScreen(ladder);
                        g.FillRectangle(LadderFill, r);
                        g.DrawRectangle(null, LadderPen, Crisp(r));
                    }
            }
        }
        else if (Mode == PlayMode.TopDown)
        {
            var room = ToScreen(_room);
            g.FillRectangle(AppTheme.PanelBrush, room);
            if (options.ShowGrid)
            {
                using (g.PushClip(room)) DrawGrid(g, size, ox, oy, cellPx);
            }
            g.DrawRectangle(null, RoomBorderPen, room);
        }
        else
        {
            if (options.ShowGrid) DrawGrid(g, size, ox, oy, cellPx);
            var oneWay = AppTheme.Brush(AppTheme.CheckerA);
            foreach (var platform in Platformer.Platforms)
            {
                var r = ToScreen(platform.Bounds);
                g.FillRectangle(platform.OneWay ? oneWay : AppTheme.InputBrush, r);
                if (platform.OneWay) g.DrawLine(OneWayTopPen, new Avalonia.Point(r.Left, r.Top + 1), new Avalonia.Point(r.Right, r.Top + 1));
                else g.DrawRectangle(null, PlatformEdgePen, Crisp(r));
            }
            foreach (var ladder in Platformer.Ladders) DrawLadder(g, ToScreen(ladder), cellPx);
        }

        if (_map is null) DrawCharacter(g, pivot, ox, oy, scale);

        if (options.ShowCollider)
        {
            var c = Mode == PlayMode.TopDown ? TopDown.Collider : PlatformerBody;
            var box = ToScreen(new RectangleF(feet.X + c.X, feet.Y + c.Y, c.Width, c.Height));
            g.FillRectangle(ColliderFill, box);
            g.DrawRectangle(null, ColliderPen, Crisp(box));
        }

        if (options.ShowPivot)
        {
            double px = ox + pivot.X * scale, py = oy + pivot.Y * scale;
            const double arm = 6;
            var h1 = new Avalonia.Point(px - arm, py);
            var h2 = new Avalonia.Point(px + arm, py);
            var v1 = new Avalonia.Point(px, py - arm);
            var v2 = new Avalonia.Point(px, py + arm);
            g.DrawLine(PivotShadowPen, h1, h2);
            g.DrawLine(PivotShadowPen, v1, v2);
            g.DrawLine(PivotPen, h1, h2);
            g.DrawLine(PivotPen, v1, v2);
        }

        if (!options.HasFocus)
        {
            AppTheme.DrawText(g, UiStrings.S_PlaySimulation_Render_EAEFC07141, new Avalonia.Point(10, 10), AppTheme.WarningBrush);
        }
    }

    /// <summary>1픽셀 테두리가 화면 픽셀에 맞도록 반 픽셀 안쪽으로 줄인 사각형.</summary>
    private static Rect Crisp(Rect r) => r.Width > 1 && r.Height > 1 ? r.Deflate(0.5) : r;

    /// <summary>연습용 사다리: 양옆 기둥과 한 칸에 세 개씩 가로대.</summary>
    private static void DrawLadder(DrawingContext g, Rect r, int cellPx)
    {
        double inset = r.Width * 0.2;
        double l = r.Left + inset, rr = r.Right - inset;
        g.DrawLine(LadderRailPen, new Avalonia.Point(l, r.Top), new Avalonia.Point(l, r.Bottom));
        g.DrawLine(LadderRailPen, new Avalonia.Point(rr, r.Top), new Avalonia.Point(rr, r.Bottom));
        double step = Math.Max(4, cellPx / 3.0);
        for (double y = r.Top + step / 2; y < r.Bottom; y += step)
            g.DrawLine(LadderRailPen, new Avalonia.Point(l, y), new Avalonia.Point(rr, y));
    }

    private static void DrawGrid(DrawingContext g, Size size, int ox, int oy, int cellPx)
    {
        if (cellPx < 4) return;
        for (int x = ((ox % cellPx) + cellPx) % cellPx; x < size.Width; x += cellPx)
            g.DrawLine(GridPen, new Avalonia.Point(x + 0.5, 0), new Avalonia.Point(x + 0.5, size.Height));
        for (int y = ((oy % cellPx) + cellPx) % cellPx; y < size.Height; y += cellPx)
            g.DrawLine(GridPen, new Avalonia.Point(0, y + 0.5), new Avalonia.Point(size.Width, y + 0.5));
    }

    /// <summary>
    /// 깔린 타일맵을 그리는 차례대로 그리고, 캐릭터를 그 사이에 끼운다: 위치로 그리는 타일셋이면 바닥 묶음과 기준선이 발(피벗) 높이 이하인 세운 것 다음,
    /// 나머지 세운 것과 머리 위 묶음 앞(벽 뒤에선 가려지고 앞에선 덮음). 층 차례 타일셋이면 모든 타일 다음.
    /// </summary>
    private void DrawMapWithCharacter(DrawingContext g, PlayMap map, Size size, int ox, int oy, int scale, Vector2 pivot)
    {
        var ts = map.Tileset;
        var data = map.Data;
        int left = (int)Math.Floor(-ox / (double)scale) - 1, top = (int)Math.Floor(-oy / (double)scale) - 1;
        var view = new Rectangle(left, top, size.Width / scale + 3, size.Height / scale + 3);
        var draws = TileMapRenderer.DrawOrder(ts, data.Map, data.Origin, view, data.ShowsFloor, _mapClockMs, freeFloor: data.IsFreeFloor);
        int insert = draws.Count;
        if (ts.SortMode == TileSortMode.ByPosition)
        {
            insert = 0;
            for (int i = 0; i < draws.Count; i++)
            {
                var t = draws[i].Tile;
                var kind = ts.DrawClassOf(t.Cell);
                if (kind == TileDrawClass.Overhead || kind == TileDrawClass.Sorted && ts.SortLineAt(t.At, t.Cell, data.IsFreeFloor(t.Floor)) + data.Origin.Y > pivot.Y) break;
                insert = i + 1;
            }
        }
        using (ImageUtil.Pixelated(g))
        {
            for (int i = 0; i < draws.Count; i++)
            {
                if (i == insert) DrawCharacter(g, pivot, ox, oy, scale);
                var d = draws[i];
                g.DrawImage(_tileBitmaps.GetValue(d.Image, ImageUtil.ToBitmap), new Rect(0, 0, d.Image.Width, d.Image.Height),
                    new Rect(ox + d.Rect.X * scale, oy + d.Rect.Y * scale, d.Rect.Width * scale, d.Rect.Height * scale));
            }
            if (insert == draws.Count) DrawCharacter(g, pivot, ox, oy, scale);
        }
    }

    private void DrawCharacter(DrawingContext g, Vector2 pivot, int ox, int oy, int scale)
    {
        var sliceName = CurrentSliceName;
        var frame = sliceName is null ? null : _sprites.GetFrame(sliceName, FlipX);
        if (frame is null)
        {
            // 칸을 찾을 수 없으면 캐릭터 상자 자리에 X를 그린다.
            var r = new Rect(ox + (pivot.X + _box.X) * scale, oy + (pivot.Y + _box.Y) * scale, _box.Width * scale, _box.Height * scale);
            g.DrawRectangle(null, MissingPen, r);
            g.DrawLine(MissingPen, r.TopLeft, r.BottomRight);
            g.DrawLine(MissingPen, r.TopRight, r.BottomLeft);
            return;
        }

        int wx = (int)MathF.Floor(pivot.X - frame.PivotX + 0.5f);
        int wy = (int)MathF.Floor(pivot.Y - frame.PivotY + 0.5f);
        var dest = new Rect(ox + wx * scale, oy + wy * scale, frame.Width * scale, frame.Height * scale);
        using (ImageUtil.Pixelated(g))
            g.DrawImage(frame.Bitmap, new Rect(0, 0, frame.Width, frame.Height), dest);
    }

    // ───────────────────────── 상태 설명 ─────────────────────────

    /// <summary>재생 중인 애니메이션·상태 글과, 이름 후보 순서(위에서부터, 고른 것에서 멈춤).</summary>
    public (string Text, IReadOnlyList<MarkedName> Candidates) DescribePlaying()
    {
        if (!_sprites.HasDocument) return (UiStrings.S_PlaySimulation_DescribePlaying_67892F24DE, []);
        var sb = new StringBuilder();
        if (Mode == PlayMode.TopDown)
        {
            sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_1AADC2BB1E).Append(TopDown.IsMoving ? UiStrings.S_PlaySimulation_DescribePlaying_E9319F8530 : UiStrings.S_PlaySimulation_DescribePlaying_D0769141F5).Append(" · ").Append(FacingText(TopDown.Facing)).AppendLine();
        }
        else
        {
            sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_1AADC2BB1E).Append(Platformer.State switch
            {
                PlatformerState.Run => UiStrings.S_PlaySimulation_DescribePlaying_1F7AD1E3B5,
                PlatformerState.Jump => UiStrings.S_PlaySimulation_DescribePlaying_ED1785452A,
                PlatformerState.Fall => UiStrings.S_PlaySimulation_DescribePlaying_C6084A0A78,
                PlatformerState.Climb or PlatformerState.WallClimb => UiStrings.PlayTest_StateClimb,
                PlatformerState.Hang or PlatformerState.WallHang => UiStrings.PlayTest_StateHang,
                _ => UiStrings.S_PlaySimulation_DescribePlaying_D0769141F5,
            }).Append(" · ").Append(Platformer.FacingRight ? UiStrings.S_PlaySimulation_DescribePlaying_A5C08EDCE8 : UiStrings.S_PlaySimulation_DescribePlaying_D8DF5DEA2F)
              .Append(" · ").Append(Platformer.Climbing switch
              {
                  ClimbKind.Ladder => UiStrings.PlayTest_OnLadder,
                  ClimbKind.Wall => UiStrings.PlayTest_OnWall,
                  _ => Platformer.IsGrounded ? UiStrings.S_PlaySimulation_DescribePlaying_CB2F37F752 : UiStrings.S_PlaySimulation_DescribePlaying_7D9FF03FF6,
              }).AppendLine();
        }

        var pos = PivotPosition;
        sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_8DB794B5EA).Append(MathF.Round(pos.X)).Append(", ").Append(MathF.Round(pos.Y)).AppendLine(" px");

        if (_player.Clip is { } clip)
        {
            sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_8813C12991).Append(clip.Name);
            if (Choice is null) sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_23CB7FA7CD);
            if (FlipX) sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_A526E5944C);
            sb.AppendLine();
            int index = _player.FrameIndex;
            sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_E13FAEC802).Append(index + 1).Append('/').Append(clip.Frames.Count);
            if (_player.CurrentFrame is { } f)
            {
                sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_A913098D03).Append(f.Slice).Append('\'');
                if (_sprites.GetFrame(f.Slice, false) is null) sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_2395CC8590);
            }
            sb.AppendLine();
        }
        else if (_sprites.StillFrame is not null)
        {
            sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_51984A5112).Append(_sprites.StillDescription).Append(UiStrings.S_PlaySimulation_DescribePlaying_4236C67AF3);
            if (FlipX) sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_A526E5944C);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(UiStrings.S_PlaySimulation_DescribePlaying_566000BF91);
        }

        sb.Append(UiStrings.S_PlaySimulation_DescribePlaying_F84837ED42);
        var candidates = new List<MarkedName>();
        foreach (var (name, flip) in CurrentCandidates())
        {
            bool exists = _sprites.HasClip(name);
            bool chosen = exists && Choice is { } c && c.Name == name;
            candidates.Add(new MarkedName(chosen ? NameMark.Chosen : exists ? NameMark.Found : NameMark.Missing, flip ? name + UiStrings.S_PlaySimulation_DescribePlaying_DC3EA22F20 : name));
            if (chosen) break;
        }
        return (sb.ToString(), candidates);
    }

    /// <summary>지금 상태에서 컨트롤러가 시도하는 이름 순서(코어 선택 규칙과 같은 순서).</summary>
    private IEnumerable<(string Name, bool Flip)> CurrentCandidates()
    {
        if (Mode == PlayMode.TopDown)
        {
            var facing = TopDown.Facing;
            var list = new List<(string, bool)>();
            if (TopDown.IsMoving) list.AddRange(Candidates(["walk", "run"], facing));
            list.AddRange(Candidates(["idle"], facing));
            if (facing != Facing.Down) list.AddRange(Candidates(["idle"], Facing.Down));
            return list;
        }
        return Candidates(PlatformerController.AnimationNames(Platformer.State), Platformer.FacingRight ? Facing.Right : Facing.Left);
    }

    private IEnumerable<(string Name, bool Flip)> Candidates(string[] baseNames, Facing facing)
    {
        foreach (var b in baseNames)
        {
            yield return ($"{b}_{AnimationSelector.DirectionName(facing)}", false);
            if (facing == Facing.Right) yield return ($"{b}_left", true);
            if (facing == Facing.Left) yield return ($"{b}_right", true);
            yield return (b, facing switch
            {
                Facing.Left => PlainFacesRight,
                Facing.Right => !PlainFacesRight,
                _ => false,
            });
        }
    }

    /// <summary>
    /// 이 모드가 기대하는 애니메이션 이름마다 실제로 쓰일 애니메이션: 그대로 있음(<see cref="NameMark.Found"/>),
    /// 다른 것으로 대신(<see cref="NameMark.Substitute"/>), 없음(<see cref="NameMark.Missing"/>).
    /// </summary>
    public (IReadOnlyList<MarkedName> Lines, string? Hint) DescribeExpected()
    {
        if (!_sprites.HasDocument) return ([], null);
        var lines = new List<MarkedName>();
        bool anySubstitute = false, anyMissing = false;

        void Line(string expected, AnimationChoice? choice)
        {
            if (choice is { } c && c.Name == expected && !c.FlipX)
            {
                lines.Add(new MarkedName(NameMark.Found, expected));
                return;
            }
            if (choice is { } other)
            {
                anySubstitute = true;
                lines.Add(new MarkedName(NameMark.Substitute, $"{expected} → {other.Name}" + (other.FlipX ? UiStrings.S_PlaySimulation_DescribeExpected_D850BC602E : "")));
            }
            else
            {
                anyMissing = true;
                lines.Add(new MarkedName(NameMark.Missing, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PlaySimulation_DescribeExpected_E3AA3E9D72,expected)));
            }
        }

        Func<string, bool> has = _sprites.HasClip;
        if (Mode == PlayMode.TopDown)
        {
            Facing[] facings = [Facing.Down, Facing.Up, Facing.Left, Facing.Right];
            foreach (var f in facings)
            {
                Line($"idle_{AnimationSelector.DirectionName(f)}", IdleChoice(f));
            }
            foreach (var f in facings)
            {
                Line($"walk_{AnimationSelector.DirectionName(f)}",
                    AnimationSelector.Resolve(["walk", "run"], f, has, PlainFacesRight) ?? IdleChoice(f));
            }
        }
        else
        {
            var expected = new List<(string, PlatformerState)>
            {
                ("idle", PlatformerState.Idle), ("run", PlatformerState.Run),
                ("jump", PlatformerState.Jump), ("fall", PlatformerState.Fall),
            };
            if (HasLadders) expected.AddRange([("climb", PlatformerState.Climb), ("hang", PlatformerState.Hang)]);
            if (Platformer.Settings.WallClimb) expected.AddRange([("wall_climb", PlatformerState.WallClimb), ("wall_hang", PlatformerState.WallHang)]);
            foreach (var (name, state) in expected)
                Line(name, AnimationSelector.Resolve(PlatformerController.AnimationNames(state), Facing.Right, has, PlainFacesRight));
        }

        string? hint = null;
        if (_sprites.ClipNames.Count == 0)
        {
            hint = _sprites.StillFrame is null
                ? UiStrings.S_PlaySimulation_DescribeExpected_669B0EA693
                : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PlaySimulation_DescribeExpected_AAF31909FF,_sprites.StillDescription);
        }
        else if (anySubstitute || anyMissing)
        {
            var parts = new List<string>();
            if (anySubstitute) parts.Add(UiStrings.S_PlaySimulation_DescribeExpected_4734C4A161);
            if (anyMissing) parts.Add(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_PlaySimulation_DescribeExpected_172D55E96E,_sprites.ClipNames[0]));
            hint = string.Join(". ", parts) + UiStrings.S_PlaySimulation_DescribeExpected_3D90F34B55;
        }
        return (lines, hint);

        AnimationChoice? IdleChoice(Facing f)
            => AnimationSelector.Resolve(["idle"], f, has, PlainFacesRight)
               ?? AnimationSelector.Resolve(["idle"], Facing.Down, has, PlainFacesRight);
    }

    private static string FacingText(Facing facing) => facing switch
    {
        Facing.Up => UiStrings.S_PlaySimulation_FacingText_5C117F45EA,
        Facing.Left => UiStrings.S_PlaySimulation_FacingText_94139E69A3,
        Facing.Right => UiStrings.S_PlaySimulation_FacingText_BAF1C45CD4,
        _ => UiStrings.S_PlaySimulation_FacingText_63B60AAB9C,
    };

}
