using System.Numerics;
using System.Text;
using Gpixel.Core.Model;
using Gpixel.Core.Play;
using RectangleF = System.Drawing.RectangleF;
using SizeF = System.Drawing.SizeF;

namespace Gpixel.App.PlayTest;

internal enum PlayMode
{
    TopDown,
    Platformer,
}

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
            var ft = AppTheme.Text("열린 이미지가 없습니다.", AppTheme.DimTextBrush);
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
            AppTheme.DrawText(g, "화면을 클릭하면 키보드로 조작합니다.", new Avalonia.Point(10, 10), AppTheme.WarningBrush);
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

    /// <summary>재생 중인 애니메이션, 상태, 이름 후보 순서(대체 포함).</summary>
    public string DescribePlaying()
    {
        if (!_sprites.HasDocument) return "열린 이미지가 없습니다.";
        var sb = new StringBuilder();
        if (Mode == PlayMode.TopDown)
        {
            sb.Append("상태: ").Append(TopDown.IsMoving ? "이동" : "멈춤").Append(" · ").Append(FacingText(TopDown.Facing)).AppendLine();
        }
        else
        {
            sb.Append("상태: ").Append(Platformer.State switch
            {
                PlatformerState.Run => "달리기",
                PlatformerState.Jump => "점프",
                PlatformerState.Fall => "낙하",
                _ => "멈춤",
            }).Append(" · ").Append(Platformer.FacingRight ? "오른쪽" : "왼쪽")
              .Append(" · ").Append(Platformer.IsGrounded ? "바닥" : "공중").AppendLine();
        }

        var pos = PivotPosition;
        sb.Append("위치: ").Append(MathF.Round(pos.X)).Append(", ").Append(MathF.Round(pos.Y)).AppendLine(" px");

        if (_player.Clip is { } clip)
        {
            sb.Append("재생: ").Append(clip.Name);
            if (Choice is null) sb.Append(" (규칙에 맞는 이름이 없어 첫 애니메이션)");
            if (FlipX) sb.Append(" · 좌우 반전");
            sb.AppendLine();
            int index = _player.FrameIndex;
            sb.Append("프레임: ").Append(index + 1).Append('/').Append(clip.Frames.Count);
            if (_player.CurrentFrame is { } f)
            {
                sb.Append(" · 칸 '").Append(f.Slice).Append('\'');
                if (_sprites.GetFrame(f.Slice, false) is null) sb.Append(" (칸 없음)");
            }
            sb.AppendLine();
        }
        else if (_sprites.StillFrame is not null)
        {
            sb.Append("재생: 애니메이션 없음 → ").Append(_sprites.StillDescription).Append(" 정지 화면");
            if (FlipX) sb.Append(" · 좌우 반전");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("재생: 보여 줄 애니메이션·칸이 없습니다.");
        }

        sb.AppendLine("이름 후보(위에서부터):");
        bool found = false;
        foreach (var (name, flip) in CurrentCandidates())
        {
            bool exists = _sprites.HasClip(name);
            bool chosen = !found && exists && Choice is { } c && c.Name == name;
            sb.Append(chosen ? "  ▶ " : exists ? "  ✓ " : "  ✗ ").Append(name);
            if (flip) sb.Append(" (반전)");
            sb.AppendLine();
            if (chosen)
            {
                found = true;
                break;
            }
        }
        return sb.ToString().TrimEnd();
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
    /// 이 모드가 기대하는 애니메이션 이름마다 실제로 쓰일 애니메이션. ✓ 그대로 있음, △ 다른 것으로 대신, ✗ 없음.
    /// </summary>
    public (string Lines, string? Hint) DescribeExpected()
    {
        if (!_sprites.HasDocument) return ("", null);
        var sb = new StringBuilder();
        bool anySubstitute = false, anyMissing = false;

        void Line(string expected, AnimationChoice? choice)
        {
            if (choice is { } c && c.Name == expected && !c.FlipX)
            {
                sb.Append("✓ ").AppendLine(expected);
                return;
            }
            if (choice is { } other)
            {
                anySubstitute = true;
                sb.Append("△ ").Append(expected).Append(" → ").Append(other.Name).AppendLine(other.FlipX ? " (반전)" : "");
            }
            else
            {
                anyMissing = true;
                sb.Append("✗ ").Append(expected).AppendLine(" 없음");
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
                ? "애니메이션도 칸도 없어 보여 줄 그림이 없습니다."
                : $"애니메이션이 없어 {_sprites.StillDescription}을(를) 정지 화면으로 보여 줍니다. 위 이름으로 애니메이션을 만들면 상태에 따라 자동으로 바뀝니다.";
        }
        else if (anySubstitute || anyMissing)
        {
            var parts = new List<string>();
            if (anySubstitute) parts.Add("△는 다른 애니메이션으로 대신합니다");
            if (anyMissing) parts.Add($"✗는 대신할 것도 없어 첫 애니메이션 '{_sprites.ClipNames[0]}'을(를) 씁니다");
            hint = string.Join(". ", parts) + ". 위 이름으로 애니메이션을 만들면 상태에 따라 자동으로 골라집니다.";
        }
        return (sb.ToString().TrimEnd(), hint);

        AnimationChoice? IdleChoice(Facing f)
            => AnimationSelector.Resolve(["idle"], f, has, PlainFacesRight)
               ?? AnimationSelector.Resolve(["idle"], Facing.Down, has, PlainFacesRight);
    }

    private static string FacingText(Facing facing) => facing switch
    {
        Facing.Up => "위",
        Facing.Left => "왼쪽",
        Facing.Right => "오른쪽",
        _ => "아래",
    };

}
