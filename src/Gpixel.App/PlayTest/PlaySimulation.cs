using System.Numerics;
using System.Text;
using Gpixel.Core.Model;
using Gpixel.Core.Play;
using RectangleF = System.Drawing.RectangleF;
using SizeF = System.Drawing.SizeF;
using Gpixel.App.Localization;

namespace Gpixel.App.PlayTest;

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

/// <summary>한 번의 갱신에 쓰는 키 입력. <see cref="JumpPressed"/>는 이번에 새로 눌렸을 때만 true.</summary>
internal readonly record struct PlayInput(bool Left, bool Right, bool Up, bool Down, bool JumpHeld, bool JumpPressed);

/// <summary>그리기 옵션.</summary>
internal readonly record struct PlayRenderOptions(int Scale, bool ShowGrid, bool ShowPivot, bool ShowCollider, bool HasFocus);

/// <summary>
/// 움직임 테스트의 상태: 탑뷰/플랫포머 컨트롤러, 맵, 애니메이션 재생기. 고정 간격으로 진행하고 화면에 그린다.
/// 위치는 픽셀 단위(y 아래가 +)이며 캐릭터 위치에 칸의 피벗이 놓인다.
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
    private static readonly IPen GridPen = AppTheme.Pen(Color.FromArgb(70, AppTheme.DimTextColor.R, AppTheme.DimTextColor.G, AppTheme.DimTextColor.B));
    private static readonly IPen RoomBorderPen = AppTheme.Pen(AppTheme.BorderColor, 2);
    private static readonly IPen PlatformEdgePen = AppTheme.Pen(AppTheme.BorderColor);
    private static readonly IPen OneWayTopPen = AppTheme.Pen(AppTheme.DimTextColor, 2);
    private static readonly IPen PivotShadowPen = AppTheme.Pen(Colors.Black, 3);
    private static readonly IPen PivotPen = AppTheme.Pen(AppTheme.AccentColor);
    private static readonly IPen MissingPen = AppTheme.Pen(AppTheme.WarningColor, 2);

    private readonly AnimationPlayer _player = new();
    private PlaySprites _sprites = PlaySprites.Empty;
    private float _topDownAccumulator;
    private int _levelPpu = -1;

    /// <summary>피벗 기준 캐릭터 상자(기준 프레임의 불투명 영역). 탑뷰 충돌 상자이자 플랫포머 충돌 상자 크기.</summary>
    private RectangleF _box;

    private RectangleF _room;

    public PlayMode Mode { get; private set; } = PlayMode.TopDown;
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

    private bool FacingLeft => Mode == PlayMode.TopDown ? TopDown.Facing == Facing.Left : !Platformer.FacingRight;
    private bool FacingRight => Mode == PlayMode.TopDown ? TopDown.Facing == Facing.Right : Platformer.FacingRight;

    /// <summary>피벗이 놓이는 캐릭터 위치(픽셀).</summary>
    public Vector2 PivotPosition => Mode == PlayMode.TopDown
        ? TopDown.Position
        : Platformer.Position - new Vector2(0, _box.Bottom);

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

    /// <summary>두 모드 모두 처음 위치로 돌린다.</summary>
    public void Reset()
    {
        TopDown.Position = Vector2.Zero;
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
            Platformer.Update(dt, new PlatformerInput(input.Left, input.Right, input.JumpHeld, input.JumpPressed));
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

    private void BuildLevel()
    {
        int c = _sprites.PixelsPerUnit;
        _levelPpu = c;

        _room = new RectangleF(-RoomCellsW / 2f * c, -RoomCellsH / 2f * c, RoomCellsW * c, RoomCellsH * c);
        // 캐릭터 상자가 방 안에 머물도록 피벗이 움직일 범위를 줄인다.
        float left = _room.Left - _box.Left, right = _room.Right - _box.Right;
        float top = _room.Top - _box.Top, bottom = _room.Bottom - _box.Bottom;
        if (right < left) left = right = (left + right) / 2;
        if (bottom < top) top = bottom = (top + bottom) / 2;
        TopDown.Bounds = RectangleF.FromLTRB(left, top, right, bottom);

        var p = Platformer.Platforms;
        p.Clear();
        void Add(float x, float y, float w, float h, bool oneWay = false)
            => p.Add(new PlatformBox(new RectangleF(x * c, y * c, w * c, h * c), oneWay));
        Add(-24, 0, 48, 3);              // 바닥
        Add(-25, -16, 1, 19);            // 왼쪽 벽
        Add(24, -16, 1, 19);             // 오른쪽 벽
        Add(-16, -2, 3, 2);              // 바닥 위 상자
        Add(-9, -4, 5, 0.5f, true);      // 위에서만 밟히는 발판
        Add(3, -3, 5, 0.5f, true);
        Add(10, -6, 4, 1);               // 단단한 발판
        Add(16, -9, 4, 0.5f, true);
        Platformer.ColliderSize = new SizeF(Math.Max(1, _box.Width), Math.Max(1, _box.Height));
        Platformer.SpawnPoint = Vector2.Zero;
        Platformer.KillY = 40 * c;
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
        // 카메라는 캐릭터 상자 가운데를 정수 픽셀로 따라간다(격자·발판이 화면 픽셀에 딱 맞도록).
        int camX = (int)MathF.Floor(pivot.X + _box.X + _box.Width / 2 + 0.5f);
        int camY = (int)MathF.Floor(pivot.Y + _box.Y + _box.Height / 2 + 0.5f);
        int ox = size.Width / 2 - camX * scale;
        int oy = size.Height / 2 - camY * scale;
        Rect ToScreen(RectangleF r) => new(ox + r.X * scale, oy + r.Y * scale, r.Width * scale, r.Height * scale);

        int cellPx = _sprites.PixelsPerUnit * scale;
        if (Mode == PlayMode.TopDown)
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
        }

        DrawCharacter(g, pivot, ox, oy, scale);

        if (options.ShowCollider)
        {
            var box = Mode == PlayMode.TopDown
                ? ToScreen(new RectangleF(pivot.X + _box.X, pivot.Y + _box.Y, _box.Width, _box.Height))
                : ToScreen(Platformer.Collider);
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

    private static void DrawGrid(DrawingContext g, Size size, int ox, int oy, int cellPx)
    {
        if (cellPx < 4) return;
        for (int x = ((ox % cellPx) + cellPx) % cellPx; x < size.Width; x += cellPx)
            g.DrawLine(GridPen, new Avalonia.Point(x + 0.5, 0), new Avalonia.Point(x + 0.5, size.Height));
        for (int y = ((oy % cellPx) + cellPx) % cellPx; y < size.Height; y += cellPx)
            g.DrawLine(GridPen, new Avalonia.Point(0, y + 0.5), new Avalonia.Point(size.Width, y + 0.5));
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
                _ => UiStrings.S_PlaySimulation_DescribePlaying_D0769141F5,
            }).Append(" · ").Append(Platformer.FacingRight ? UiStrings.S_PlaySimulation_DescribePlaying_A5C08EDCE8 : UiStrings.S_PlaySimulation_DescribePlaying_D8DF5DEA2F)
              .Append(" · ").Append(Platformer.IsGrounded ? UiStrings.S_PlaySimulation_DescribePlaying_CB2F37F752 : UiStrings.S_PlaySimulation_DescribePlaying_7D9FF03FF6).AppendLine();
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
        return Candidates(PlatformerNames(Platformer.State), Platformer.FacingRight ? Facing.Right : Facing.Left);
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

    private static string[] PlatformerNames(PlatformerState state) => state switch
    {
        PlatformerState.Run => ["run", "walk", "idle"],
        PlatformerState.Jump => ["jump", "fall", "idle"],
        PlatformerState.Fall => ["fall", "jump", "idle"],
        _ => ["idle"],
    };

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
            foreach (var (expected, state) in new[]
                     {
                         ("idle", PlatformerState.Idle), ("run", PlatformerState.Run),
                         ("jump", PlatformerState.Jump), ("fall", PlatformerState.Fall),
                     })
            {
                Line(expected, AnimationSelector.Resolve(PlatformerNames(state), Facing.Right, has, PlainFacesRight));
            }
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
