using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>편집 도구 종류.</summary>
public enum ToolKind
{
    Pencil,
    Eraser,
    Fill,
    Picker,
    Line,
    Rectangle,
    Ellipse,
    Select,
    Move,
    /// <summary>밝게(왼쪽)/어둡게(오른쪽) 브러시.</summary>
    Dodge,
    /// <summary>체커 무늬 디더링 브러시(기본색·보조색 번갈아).</summary>
    Dither,
    /// <summary>처음 닿은 색만 바꾸는 색 바꾸기 브러시.</summary>
    ReplaceBrush,
    /// <summary>피벗 점 끌기.</summary>
    Pivot,
    /// <summary>9-slice 테두리(Unity Sprite Border) 초록 선 끌기.</summary>
    Slice,
}

/// <summary>
/// 편집기 전체가 함께 쓰는 상태: 열린 프로젝트, 현재 레이어·프레임, 선택 영역, 도구·색, 합성 비트맵 캐시.
/// 패널들은 이 객체의 이벤트로 서로 동기화된다.
/// <para>
/// 타임라인의 열은 항상 프레임이다. 현재 레이어가 부위면 캔버스 크기(<see cref="CanvasWidth"/>)·그리기 대상(<see cref="EditImage"/>)·
/// 피벗은 그 부위 기준(부위 안 좌표)이고, 그룹이면 프레임 합성을 보며 부위를 배치한다(그리기 없음).
/// </para>
/// </summary>
public sealed class EditorContext
{
    public const int MaxRecentColors = 16;

    private ToolKind _tool = ToolKind.Pencil;
    private Color32 _primary = Color32.Black;
    private Color32 _secondary = Color32.Transparent;
    private int _brushSize = 1;
    private bool _selectionStrokeOpen;
    private int _layer, _frame;
    private Size _canvasSize;
    private bool _onionSkin;
    private AnimationTag? _selectedTag;
    private Pivot? _pivotPreview;
    private PixelImage? _blank;
    private PixelImage? _strokeImage;
    private WriteableBitmap? _blankBitmap;

    // 프레임 합성 캐시, 부위 하나를 뺀 프레임 합성 캐시, 셀(그룹이면 그룹 합성) 썸네일 캐시
    private readonly Dictionary<Frame, (PixelImage Image, WriteableBitmap Bitmap)> _compositeCache = new();
    private readonly Dictionary<(Frame, Layer), WriteableBitmap> _contextCache = new();
    private readonly Dictionary<(Layer, Frame), WriteableBitmap> _celCache = new();

    public SpriteProject? Project { get; private set; }
    public bool HasProject => Project is not null;
    public Selection Selection { get; } = new();

    /// <summary>복사한 픽셀(앱 내부 클립보드).</summary>
    public PixelImage? Clipboard { get; set; }

    public EditorContext()
    {
        // 선택 영역 떼기·내려놓기는 셀 픽셀을 직접 바꾼다.
        Selection.Changed += (_, _) => InvalidateCurrent();
    }

    /// <summary>다른 프로젝트가 열리거나 닫힘.</summary>
    public event EventHandler? ProjectReplaced;
    /// <summary>현재 프로젝트 내용이 바뀜.</summary>
    public event EventHandler<ProjectChangedEventArgs>? ProjectChanged;
    /// <summary>캔버스·미리보기·타임라인을 다시 그려야 함(내용 변경, 현재 프레임/레이어 변경 등).</summary>
    public event EventHandler? ViewChanged;
    /// <summary>현재 레이어·프레임·선택한 태그가 바뀜.</summary>
    public event EventHandler? CurrentChanged;
    public event EventHandler? DirtyChanged;
    public event EventHandler? ToolChanged;
    public event EventHandler? ToolOptionsChanged;
    public event EventHandler? ColorsChanged;
    public event EventHandler? RecentColorsChanged;
    public event EventHandler? OnionSkinChanged;
    public event EventHandler? UndoChanged;

    // ───────────── 도구·색 ─────────────

    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            if (value is not (ToolKind.Select or ToolKind.Move)) CommitFloating();
            _tool = value;
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Color32 Primary
    {
        get => _primary;
        set { if (_primary == value) return; _primary = value; ColorsChanged?.Invoke(this, EventArgs.Empty); }
    }

    public Color32 Secondary
    {
        get => _secondary;
        set { if (_secondary == value) return; _secondary = value; ColorsChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SwapColors()
    {
        (_primary, _secondary) = (_secondary, _primary);
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public List<Color32> RecentColors { get; } = new();

    /// <summary>그리기에 쓴 색을 최근 색 맨 앞에 넣는다.</summary>
    public void PushRecentColor(Color32 c)
    {
        if (RecentColors.Count > 0 && RecentColors[0] == c) return;
        RecentColors.Remove(c);
        RecentColors.Insert(0, c);
        if (RecentColors.Count > MaxRecentColors) RecentColors.RemoveRange(MaxRecentColors, RecentColors.Count - MaxRecentColors);
        RecentColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>브러시 크기(1~<see cref="BrushFootprint.MaxSize"/>): 연필·지우개·밝게/어둡게·디더링·색 바꾸기 브러시, 선·도형 외곽 두께.</summary>
    public int BrushSize
    {
        get => _brushSize;
        set { value = Math.Clamp(value, 1, BrushFootprint.MaxSize); if (_brushSize == value) return; _brushSize = value; OptionsChanged(); }
    }

    private bool _pixelPerfect, _fillContiguous = true, _shapeFilled, _mirrorX, _mirrorY, _rightErases, _moveWrap = true;
    private bool _fillDiagonal, _fillSampleVisible, _pickerSampleVisible = true;
    private BrushShape _brushShape = BrushShape.Square;
    private int _fillTolerance, _dodgeStrength = DefaultDodgeStrength;
    private DitherPattern _ditherPattern = DitherPattern.Checker;

    public const int DefaultDodgeStrength = 10;
    public const int MaxDodgeStrength = 50;

    /// <summary>브러시 모양(사각·원·마름모). 크기 1은 모양과 관계없이 한 점.</summary>
    public BrushShape BrushShape
    {
        get => _brushShape;
        set { if (!Enum.IsDefined(value)) value = BrushShape.Square; if (_brushShape == value) return; _brushShape = value; OptionsChanged(); }
    }

    /// <summary>현재 브러시 도장(모양·크기).</summary>
    public BrushFootprint Brush => BrushFootprint.Get(_brushShape, _brushSize);

    public bool PixelPerfect { get => _pixelPerfect; set { _pixelPerfect = value; OptionsChanged(); } }
    /// <summary>채우기: true = 이어진 영역만, false = 맞는 색 전체.</summary>
    public bool FillContiguous { get => _fillContiguous; set { _fillContiguous = value; OptionsChanged(); } }
    /// <summary>채우기 허용 오차(0~100%, 0 = 정확히 같은 색만). 채널 차이 최댓값 기준(<see cref="Raster.Matches"/>).</summary>
    public int FillTolerance
    {
        get => _fillTolerance;
        set { value = Math.Clamp(value, 0, 100); if (_fillTolerance == value) return; _fillTolerance = value; OptionsChanged(); }
    }
    /// <summary><see cref="FillTolerance"/>(%)를 채널 단위(0~255)로.</summary>
    public int FillToleranceChannels => (int)Math.Round(_fillTolerance * 255 / 100.0);
    /// <summary>채우기: 이어진 영역을 대각선으로도 잇는다(8방향).</summary>
    public bool FillDiagonal { get => _fillDiagonal; set { _fillDiagonal = value; OptionsChanged(); } }
    /// <summary>채우기 영역을 보이는 레이어 합성으로 정한다(칠하기는 현재 레이어). false = 현재 레이어.</summary>
    public bool FillSampleVisible { get => _fillSampleVisible; set { _fillSampleVisible = value; OptionsChanged(); } }
    /// <summary>스포이드(임시 스포이드 포함): 보이는 레이어 합성에서 집는다. false = 현재 레이어에서.</summary>
    public bool PickerSampleVisible { get => _pickerSampleVisible; set { _pickerSampleVisible = value; OptionsChanged(); } }
    /// <summary>밝게/어둡게 한 번의 명도 변화(1~50%).</summary>
    public int DodgeStrength
    {
        get => _dodgeStrength;
        set { value = Math.Clamp(value, 1, MaxDodgeStrength); if (_dodgeStrength == value) return; _dodgeStrength = value; OptionsChanged(); }
    }
    /// <summary>디더링 무늬.</summary>
    public DitherPattern DitherPattern
    {
        get => _ditherPattern;
        set { if (!Enum.IsDefined(value)) value = DitherPattern.Checker; if (_ditherPattern == value) return; _ditherPattern = value; OptionsChanged(); }
    }
    /// <summary>사각형·원: 채움(true) / 외곽(false).</summary>
    public bool ShapeFilled { get => _shapeFilled; set { _shapeFilled = value; OptionsChanged(); } }
    /// <summary>대칭 그리기: 좌우(세로 축 기준 반전).</summary>
    public bool MirrorX { get => _mirrorX; set { _mirrorX = value; OptionsChanged(); } }
    /// <summary>대칭 그리기: 상하.</summary>
    public bool MirrorY { get => _mirrorY; set { _mirrorY = value; OptionsChanged(); } }
    /// <summary>오른쪽 버튼으로 보조색 대신 지운다(연필·도형·채우기).</summary>
    public bool RightButtonErases { get => _rightErases; set { _rightErases = value; OptionsChanged(); } }
    /// <summary>이동 도구로 셀 내용을 옮길 때 밖으로 나간 픽셀을 반대편으로 돌린다.</summary>
    public bool MoveWrap { get => _moveWrap; set { _moveWrap = value; OptionsChanged(); } }

    private void OptionsChanged() => ToolOptionsChanged?.Invoke(this, EventArgs.Empty);

    public bool OnionSkin
    {
        get => _onionSkin;
        set { if (_onionSkin == value) return; _onionSkin = value; OnionSkinChanged?.Invoke(this, EventArgs.Empty); }
    }

    // ───────────── 프로젝트 ─────────────

    /// <summary>캔버스(그리는 그림) 가로: 부위를 고르면 그 부위 크기, 아니면 프로젝트 캔버스.</summary>
    public int CanvasWidth => CurrentPart?.PartSize.Width ?? Project?.Width ?? 0;
    public int CanvasHeight => CurrentPart?.PartSize.Height ?? Project?.Height ?? 0;
    public int FrameCount => Project?.Frames.Count ?? 0;
    public int LayerCount => Project?.Layers.Count ?? 0;

    /// <summary>현재 레이어 번호(0 = 맨 아래).</summary>
    public int CurrentLayer => _layer;
    public int CurrentFrame => _frame;

    /// <summary>현재 레이어 객체(프로젝트가 없으면 null).</summary>
    public Layer? CurrentLayerObject
        => Project is { } p && p.Layers.Count > 0 ? p.Layers[Math.Clamp(_layer, 0, p.Layers.Count - 1)] : null;

    /// <summary>현재 레이어가 부위면 그 부위, 아니면 null.</summary>
    public Layer? CurrentPart => CurrentLayerObject is { IsPart: true } l ? l : null;

    /// <summary>현재 레이어가 그룹인지(캔버스는 부위 배치 모드).</summary>
    public bool IsGroupSelected => CurrentLayerObject?.IsGroup == true;

    /// <summary>고른 그룹, 또는 고른 부위가 속한 그룹(없으면 null).</summary>
    public Layer? CurrentGroup => CurrentLayerObject switch
    {
        { IsGroup: true } g => g,
        { IsPart: true } part => part.Parent,
        _ => null,
    };

    private Size CurrentCanvasSize => new(CanvasWidth, CanvasHeight);

    public void SetProject(SpriteProject? project)
    {
        if (Project is not null)
        {
            CommitFloating();
            Project.Changed -= OnProjectChanged;
            Project.DirtyChanged -= OnProjectDirty;
            Project.History.Changed -= OnUndoChanged;
        }
        Selection.Deselect(_blank ?? new PixelImage(1, 1));
        _selectionStrokeOpen = false;
        _strokeImage = null;
        ClearCaches();
        Project = project;
        _layer = project is null ? 0 : project.Layers.Count - 1;
        _frame = 0;
        _canvasSize = CurrentCanvasSize;
        _selectedTag = null;
        _pivotPreview = null;
        if (project is not null)
        {
            project.Changed += OnProjectChanged;
            project.DirtyChanged += OnProjectDirty;
            project.History.Changed += OnUndoChanged;
        }
        ProjectReplaced?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        UndoChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnUndoChanged(object? sender, EventArgs e) => UndoChanged?.Invoke(this, EventArgs.Empty);

    private void OnProjectDirty(object? sender, EventArgs e) => DirtyChanged?.Invoke(this, EventArgs.Empty);

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        var p = Project!;
        // 스트로크 중에는 현재 셀만 바뀐다. 그 밖의 변경(실행취소, 부위 위치·크기 등)은 모든 캐시를 버린다.
        if (e.Change == ProjectChange.Pixels && p.IsStrokeActive) InvalidateCurrent();
        else if ((e.Change & ~ProjectChange.Path & ~ProjectChange.Settings) != 0) ClearCaches();
        int layer = Math.Clamp(_layer, 0, p.Layers.Count - 1), frame = Math.Clamp(_frame, 0, p.Frames.Count - 1);
        bool currentChanged = layer != _layer || frame != _frame;
        _layer = layer;
        _frame = frame;
        if (_selectedTag is not null && !p.Tags.Contains(_selectedTag)) { _selectedTag = null; currentChanged = true; }
        var size = CurrentCanvasSize;
        if ((e.Change & ProjectChange.Canvas) != 0 || size != _canvasSize)
        {
            _canvasSize = size;
            Selection.Deselect(EditImageOrEmpty);
            _selectionStrokeOpen = false;
        }
        ProjectChanged?.Invoke(this, e);
        if (currentChanged) CurrentChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 현재 레이어·프레임을 바꾼다(떠 있는 선택은 먼저 내려놓음). 캔버스 크기가 다른 레이어(부위 ↔ 프레임, 크기가 다른 부위)로
    /// 바뀌면 선택 영역은 해제된다.
    /// </summary>
    public void SetCurrent(int layer, int frame)
    {
        if (Project is not { } p) return;
        layer = Math.Clamp(layer, 0, p.Layers.Count - 1);
        frame = Math.Clamp(frame, 0, p.Frames.Count - 1);
        if (layer == _layer && frame == _frame) return;
        CommitFloating();
        if (layer != _layer)
        {
            var next = p.Layers[layer];
            var size = next.IsPart ? next.PartSize : p.Size;
            if (size != _canvasSize)
            {
                Selection.Deselect(EditImageOrEmpty);
                _selectionStrokeOpen = false;
            }
            _pivotPreview = null;
        }
        _layer = layer;
        _frame = frame;
        _canvasSize = CurrentCanvasSize;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentFrame(int frame) => SetCurrent(_layer, frame);

    public void SetCurrentLayer(int layer) => SetCurrent(layer, _frame);

    /// <summary>타임라인에서 고른 태그(재생·미리보기 대상).</summary>
    public AnimationTag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (ReferenceEquals(_selectedTag, value)) return;
            _selectedTag = value;
            CurrentChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>재생할 태그: 고른 태그가 현재 프레임을 포함하면 그것, 아니면 현재 프레임이 속한 첫 태그.</summary>
    public AnimationTag? ActiveTag
        => Project is null ? null
            : _selectedTag is { } t && Project.Tags.Contains(t) && t.Contains(_frame) ? t : Project.TagOfFrame(_frame);

    /// <summary>재생 순서(태그가 있으면 그 방향대로, 없으면 모든 프레임).</summary>
    public List<int> PlaybackOrder() => ActiveTag?.GetPlaybackOrder() ?? Enumerable.Range(0, FrameCount).ToList();

    public bool PlaybackLoops => ActiveTag?.Loop ?? true;

    public int FrameDuration(int frame) => Project is { } p && frame >= 0 && frame < p.Frames.Count ? p.Frames[frame].DurationMs : 100;

    // ───────────── 그리기 대상 ─────────────

    /// <summary>현재 레이어가 그룹이 아니고, 보이고(그룹 포함), 잠기지 않았으면(그룹 포함) true.</summary>
    public bool CanEdit
        => Project is { } p && !p.Layers[_layer].IsGroup && p.IsLayerVisible(_layer) && !p.IsLayerLocked(_layer);

    /// <summary>현재 셀 이미지(스트로크 중이면 그 이미지, 부위면 부위 크기, 그룹·빈 셀이면 null).</summary>
    public PixelImage? EditImage => _strokeImage ?? Project?.GetCel(_layer, _frame);

    /// <summary>선택 영역 계산용: 현재 셀이 없으면 캔버스 크기의 빈 이미지.</summary>
    public PixelImage EditImageOrEmpty
    {
        get
        {
            if (EditImage is { } img) return img;
            if (_blank is null || _blank.Width != CanvasWidth || _blank.Height != CanvasHeight)
                _blank = new PixelImage(Math.Max(1, CanvasWidth), Math.Max(1, CanvasHeight));
            return _blank;
        }
    }

    /// <summary>현재 셀에 그리기 시작(빈 셀이면 새로 만듦). 그룹에는 그릴 수 없다.</summary>
    public void BeginStroke()
    {
        if (Project is not { } p || p.Layers[_layer].IsGroup) return;
        _strokeImage = p.BeginCelStroke(_layer, _frame);
    }
    /// <summary>스트로크 확정(바뀐 것이 없으면 기록 안 함).</summary>
    public void CommitStroke(string label)
    {
        if (Project is null) return;
        _strokeImage = null;
        Project.CommitCelStroke(label);
    }

    /// <summary>스트로크 취소: 셀을 시작 전으로 되돌리고 기록하지 않는다.</summary>
    public void CancelStroke()
    {
        if (Project is null) return;
        _strokeImage = null;
        Project.CancelCelStroke();
        InvalidateCurrent();
    }

    /// <summary>스트로크 중 픽셀이 바뀌었음을 알린다(다시 그리기).</summary>
    public void NotifyPixels()
    {
        if (Project is null) return;
        InvalidateCurrent();
        Project.NotifyPixelsChanged();
    }

    // ───────────── 선택 영역 편집(실행취소 묶음) ─────────────

    /// <summary>선택 영역을 바꾸는 동작 전에 부른다: 아직 열린 기록이 없으면 현재 셀 스트로크를 시작한다.</summary>
    public void BeginSelectionEdit()
    {
        if (Project is null || _selectionStrokeOpen) return;
        BeginStroke();
        _selectionStrokeOpen = true;
    }

    /// <summary>떠 있는 픽셀을 내려놓고 열린 선택 편집 기록을 확정한다.</summary>
    public void CommitFloating()
    {
        if (Project is null) return;
        if (Selection.IsFloating && _strokeImage is { } img) Selection.Anchor(img);
        if (_selectionStrokeOpen)
        {
            _selectionStrokeOpen = false;
            CommitStroke("선택 편집");
        }
    }

    /// <summary>구조 편집(레이어·프레임·태그·설정) 전에 열린 그리기를 마무리한다.</summary>
    /// <summary>9-slice 대상의 테두리: 부위를 골랐으면 그 부위, 아니면 프레임 시트 모든 칸(프로젝트 설정).</summary>
    public SpriteBorder SliceBorder => CurrentPart?.PartBorder ?? Project?.Sprite.Border ?? SpriteBorder.None;

    /// <summary>9-slice 대상의 테두리를 바꾼다(실행취소 한 단계). 같으면 아무것도 하지 않는다.</summary>
    public void SetSliceBorder(SpriteBorder border)
    {
        if (Project is not { } p || border == SliceBorder) return;
        if (CurrentPart is { } part) SetPartBorder(part, border);
        else Structural(() => p.Edit("9-slice 테두리", ProjectChange.Settings, () => p.Sprite.Border = border));
    }

    /// <summary>부위 하나의 9-slice 테두리를 바꾼다(그룹 화면에서 끌 때, 실행취소 한 단계).</summary>
    public void SetPartBorder(Layer part, SpriteBorder border)
    {
        if (Project is not { } p || part.PartBorder == border) return;
        int index = p.IndexOf(part);
        Structural(() => p.SetPartBorder(index, border));
    }

    public void Structural(Action action)
    {
        if (Project is null) return;
        CommitFloating();
        action();
    }

    public bool UndoStep()
    {
        if (Project is null) return false;
        CommitFloating();
        return Project.Undo();
    }

    public bool RedoStep()
    {
        if (Project is null) return false;
        CommitFloating();
        return Project.Redo();
    }

    // ───────────── 피벗 ─────────────

    /// <summary>현재 피벗(끄는 중이면 미리보기 값): 부위면 그 부위의 피벗, 아니면 현재 프레임 피벗.</summary>
    public Pivot Pivot
        => _pivotPreview ?? CurrentPart?.PartPivot ?? (Project is { } p ? p.GetFramePivot(_frame) : Pivot.Center);

    /// <summary>피벗 끄는 중: 기록 없이 화면만 바꾼다.</summary>
    public void PreviewPivot(Pivot pivot)
    {
        _pivotPreview = pivot;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 끌기를 마친 피벗을 기록한다. 부위면 그 부위의 피벗, 아니면 현재 프레임에 피벗 재정의가 있으면
    /// 그 프레임만, 없으면 프로젝트 기본 피벗을 바꾼다.
    /// </summary>
    public void CommitPivot(Pivot before)
    {
        if (Project is not { } p || _pivotPreview is not { } after) return;
        _pivotPreview = null;
        if (after == before) { ViewChanged?.Invoke(this, EventArgs.Empty); return; }
        if (CurrentPart is not null) p.SetPartPivot(_layer, after);
        else if (p.Frames[_frame].Pivot is not null) p.SetFramePivot(_frame, after);
        else p.Edit("기본 피벗", ProjectChange.Settings, () => p.Sprite.DefaultPivot = after);
    }

    // ───────────── 합성 비트맵 캐시 ─────────────

    private void ClearCaches()
    {
        foreach (var (_, b) in _compositeCache.Values) b.Dispose();
        _compositeCache.Clear();
        foreach (var b in _contextCache.Values) b.Dispose();
        _contextCache.Clear();
        foreach (var b in _celCache.Values) b.Dispose();
        _celCache.Clear();
    }

    /// <summary>
    /// 현재 셀이 바뀜(스트로크 중): 현재 프레임의 합성, 현재 레이어가 들어간 "부위 하나 뺀 합성",
    /// 현재 셀과 그 그룹의 썸네일 캐시를 버린다.
    /// </summary>
    private void InvalidateCurrent()
    {
        if (Project is not { } p || _frame < 0 || _frame >= p.Frames.Count || _layer < 0 || _layer >= p.Layers.Count) return;
        var frame = p.Frames[_frame];
        var layer = p.Layers[_layer];
        if (_compositeCache.Remove(frame, out var entry)) entry.Bitmap.Dispose();
        foreach (var key in _contextCache.Keys.Where(k => k.Item1 == frame && k.Item2 != layer).ToList())
        {
            _contextCache[key].Dispose();
            _contextCache.Remove(key);
        }
        RemoveCel(layer, frame);
        if (layer.Parent is { } group) RemoveCel(group, frame);
    }

    private void RemoveCel(Layer layer, Frame frame)
    {
        if (_celCache.Remove((layer, frame), out var bmp)) bmp.Dispose();
    }

    /// <summary>프레임 합성 이미지와 비트맵(캐시).</summary>
    private (PixelImage Image, WriteableBitmap Bitmap)? CompositeEntry(int frame)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count) return null;
        var owner = p.Frames[frame];
        if (_compositeCache.TryGetValue(owner, out var entry)) return entry;
        var img = new PixelImage(p.Width, p.Height);
        p.FlattenInto(img, frame);
        entry = (img, ImageUtil.ToBitmap(img));
        _compositeCache[owner] = entry;
        return entry;
    }

    /// <summary>프레임 전체 합성(보이는 레이어·그룹) 비트맵. 호출자는 Dispose하지 않는다.</summary>
    public WriteableBitmap? FrameBitmap(int frame) => CompositeEntry(frame)?.Bitmap;

    /// <summary>
    /// 캔버스에 보이는 그림: 부위를 골랐으면 그 부위의 셀(부위 크기, 빈 셀이면 null), 아니면 프레임 전체 합성.
    /// 이전 프레임 보기·양파 껍질에 쓴다. 호출자는 Dispose하지 않는다.
    /// </summary>
    public WriteableBitmap? ViewBitmap(int frame) => CurrentPart is not null ? CelBitmap(_layer, frame) : FrameBitmap(frame);

    /// <summary>현재 캔버스 그림: 부위면 그 부위의 현재 셀(빈 셀이면 투명), 아니면 현재 프레임 합성.</summary>
    public WriteableBitmap? DisplayBitmap
    {
        get
        {
            if (Project is null) return null;
            if (ViewBitmap(_frame) is { } bmp) return bmp;
            if (CurrentPart is null) return null;
            int w = Math.Max(1, CanvasWidth), h = Math.Max(1, CanvasHeight);
            if (_blankBitmap is null || _blankBitmap.PixelSize.Width != w || _blankBitmap.PixelSize.Height != h)
            {
                _blankBitmap?.Dispose();
                _blankBitmap = ImageUtil.NewBitmap(w, h);
            }
            return _blankBitmap;
        }
    }

    /// <summary>
    /// 프레임 합성에서 부위 하나(<paramref name="exclude"/>)를 뺀 비트맵(프레임 크기, 캐시). 부위 화면의 흐린 주변,
    /// 그룹 화면에서 부위를 끄는 동안의 배경에 쓴다. 호출자는 Dispose하지 않는다.
    /// </summary>
    public WriteableBitmap? ContextBitmap(int frame, Layer exclude)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count) return null;
        var key = (p.Frames[frame], exclude);
        if (_contextCache.TryGetValue(key, out var bmp)) return bmp;
        var img = new PixelImage(p.Width, p.Height);
        p.FlattenInto(img, frame, layerFilter: l => !ReferenceEquals(l, exclude));
        bmp = ImageUtil.ToBitmap(img);
        _contextCache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// 현재 캔버스에서 보이는 색. 부위 화면이면 부위 셀의 색(투명이면 그 자리의 프레임 합성 색), 아니면 프레임 합성 색.
    /// </summary>
    public Color32 CompositeColorAt(int x, int y)
    {
        if (CurrentPart is { } part)
        {
            var c = EditImage is { } cel && cel.Contains(x, y) ? cel.Get(x, y) : Color32.Transparent;
            if (c.A != 0) return c;
            x += part.PartOffset.X;
            y += part.PartOffset.Y;
        }
        return CompositeEntry(_frame) is { } entry && entry.Image.Contains(x, y) ? entry.Image.Get(x, y) : Color32.Transparent;
    }

    /// <summary>
    /// 현재 캔버스에서 보이는 그림 전체(캔버스 크기, 새 이미지): 각 픽셀이 <see cref="CompositeColorAt"/>와 같다.
    /// 채우기의 "보이는 레이어 전체" 기준에 쓴다.
    /// </summary>
    public PixelImage VisibleImage()
    {
        int w = Math.Max(1, CanvasWidth), h = Math.Max(1, CanvasHeight);
        var img = new PixelImage(w, h);
        if (CompositeEntry(_frame) is not { } entry) return img;
        var frame = entry.Image;
        if (CurrentPart is not { } part)
        {
            img.CopyFrom(frame);
            return img;
        }
        var cel = EditImage;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = cel is not null && cel.Contains(x, y) ? cel.Get(x, y) : Color32.Transparent;
                int fx = x + part.PartOffset.X, fy = y + part.PartOffset.Y;
                if (c.A == 0 && frame.Contains(fx, fy)) c = frame.Get(fx, fy);
                img.Set(x, y, c);
            }
        return img;
    }

    /// <summary>
    /// 셀 하나(레이어 × 프레임)의 비트맵: 그룹이면 그 그룹의 합성(프레임 크기), 부위·일반 레이어면 셀.
    /// 빈 셀(그룹이면 모든 부위가 빈 셀)이면 null. 호출자는 Dispose하지 않는다.
    /// </summary>
    public WriteableBitmap? CelBitmap(int layer, int frame)
    {
        if (Project is not { } p || frame < 0 || frame >= p.Frames.Count || layer < 0 || layer >= p.Layers.Count) return null;
        var l = p.Layers[layer];
        var key = (l, p.Frames[frame]);
        if (_celCache.TryGetValue(key, out var bmp)) return bmp;
        PixelImage? img;
        if (l.IsGroup)
            img = p.ChildrenOf(l).Any(c => p.GetCel(c, p.Frames[frame]) is not null) ? p.FlattenGroup(l, frame) : null;
        else img = p.GetCel(layer, frame);
        if (img is null) return null;
        bmp = ImageUtil.ToBitmap(img);
        _celCache[key] = bmp;
        return bmp;
    }
}
