using System.Drawing.Imaging;
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
}

/// <summary>
/// 편집기 전체가 함께 쓰는 상태: 열린 프로젝트, 현재 레이어·프레임(아틀라스면 스프라이트), 선택 영역, 도구·색, 합성 비트맵 캐시.
/// 패널들은 이 객체의 이벤트로 서로 동기화된다.
/// <para>
/// 타임라인의 "열(column)"은 애니메이션 프로젝트에서는 프레임, 아틀라스 프로젝트에서는 스프라이트다.
/// 캔버스 크기(<see cref="CanvasWidth"/>)·그리기 대상(<see cref="EditImage"/>)·피벗은 아틀라스면 현재 스프라이트 기준이다.
/// </para>
/// </summary>
public sealed class EditorContext
{
    public const int MaxRecentColors = 16;

    private ToolKind _tool = ToolKind.Pencil;
    private Color32 _primary = Color32.Black;
    private Color32 _secondary = Color32.White;
    private int _brushSize = 1;
    private bool _selectionStrokeOpen;
    private int _layer, _frame, _sprite;
    private Size _canvasSize;
    private bool _onionSkin;
    private AnimationTag? _selectedTag;
    private Pivot? _pivotPreview;
    private PixelImage? _blank;
    private PixelImage? _strokeImage;

    // 합성 캐시(주인 = 프레임 또는 아틀라스 스프라이트 객체 기준)와 셀 썸네일 캐시
    private readonly Dictionary<object, (PixelImage Image, Bitmap Bitmap)> _compositeCache = new();
    private readonly Dictionary<(Layer, object), Bitmap> _celCache = new();

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

    public int BrushSize
    {
        get => _brushSize;
        set { value = Math.Clamp(value, 1, 8); if (_brushSize == value) return; _brushSize = value; OptionsChanged(); }
    }

    private bool _pixelPerfect, _fillContiguous = true, _shapeFilled, _mirrorX, _mirrorY, _rightErases, _moveWrap = true;

    public bool PixelPerfect { get => _pixelPerfect; set { _pixelPerfect = value; OptionsChanged(); } }
    /// <summary>채우기: true = 4방향 이어진 영역, false = 같은 색 전체.</summary>
    public bool FillContiguous { get => _fillContiguous; set { _fillContiguous = value; OptionsChanged(); } }
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

    /// <summary>아틀라스 프로젝트(스프라이트 목록)인지.</summary>
    public bool IsAtlas => Project?.IsAtlas == true;

    /// <summary>캔버스(그리는 그림) 가로: 애니메이션 = 프로젝트 캔버스, 아틀라스 = 현재 스프라이트.</summary>
    public int CanvasWidth => CurrentAtlasSprite?.Width ?? Project?.Width ?? 0;
    public int CanvasHeight => CurrentAtlasSprite?.Height ?? Project?.Height ?? 0;
    public int FrameCount => Project?.Frames.Count ?? 0;
    public int SpriteCount => Project?.Sprites.Count ?? 0;
    public int LayerCount => Project?.Layers.Count ?? 0;

    /// <summary>현재 레이어 번호(0 = 맨 아래).</summary>
    public int CurrentLayer => _layer;
    public int CurrentFrame => _frame;
    public int CurrentSprite => _sprite;

    /// <summary>아틀라스면 현재 스프라이트, 아니면 null.</summary>
    public AtlasSprite? CurrentAtlasSprite
        => Project is { IsAtlas: true } p && p.Sprites.Count > 0 ? p.Sprites[Math.Clamp(_sprite, 0, p.Sprites.Count - 1)] : null;

    /// <summary>타임라인 열 수(애니메이션 = 프레임, 아틀라스 = 스프라이트).</summary>
    public int ColumnCount => IsAtlas ? SpriteCount : FrameCount;

    /// <summary>현재 열(애니메이션 = 프레임, 아틀라스 = 스프라이트).</summary>
    public int CurrentColumn => IsAtlas ? _sprite : _frame;

    /// <summary>열의 셀 주인 객체(프레임 또는 스프라이트).</summary>
    private object Owner(SpriteProject p, int column) => p.IsAtlas ? p.Sprites[column] : p.Frames[column];

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
        _sprite = 0;
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
        // 스트로크 중에는 현재 셀만 바뀐다. 그 밖의 변경(실행취소 등)은 모든 캐시를 버린다.
        if (e.Change == ProjectChange.Pixels && p.IsStrokeActive) InvalidateCurrent();
        else if ((e.Change & ~ProjectChange.Path & ~ProjectChange.Settings) != 0) ClearCaches();
        int layer = Math.Clamp(_layer, 0, p.Layers.Count - 1), frame = Math.Clamp(_frame, 0, p.Frames.Count - 1);
        int sprite = Math.Clamp(_sprite, 0, Math.Max(0, p.Sprites.Count - 1));
        bool currentChanged = layer != _layer || frame != _frame || sprite != _sprite;
        _layer = layer;
        _frame = frame;
        _sprite = sprite;
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

    /// <summary>현재 레이어·프레임을 바꾼다(떠 있는 선택은 먼저 내려놓음).</summary>
    public void SetCurrent(int layer, int frame)
    {
        if (Project is null) return;
        layer = Math.Clamp(layer, 0, Project.Layers.Count - 1);
        frame = Math.Clamp(frame, 0, Project.Frames.Count - 1);
        if (layer == _layer && frame == _frame) return;
        CommitFloating();
        _layer = layer;
        _frame = frame;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentFrame(int frame) => SetCurrent(_layer, frame);

    /// <summary>아틀라스: 현재 레이어·스프라이트를 바꾼다. 스프라이트가 바뀌면 선택 영역은 해제된다(크기가 다를 수 있음).</summary>
    public void SetCurrentSprite(int layer, int sprite)
    {
        if (Project is not { IsAtlas: true } p) return;
        layer = Math.Clamp(layer, 0, p.Layers.Count - 1);
        sprite = Math.Clamp(sprite, 0, p.Sprites.Count - 1);
        if (layer == _layer && sprite == _sprite) return;
        CommitFloating();
        if (sprite != _sprite)
        {
            Selection.Deselect(EditImageOrEmpty);
            _selectionStrokeOpen = false;
            _pivotPreview = null;
        }
        _layer = layer;
        _sprite = sprite;
        _canvasSize = CurrentCanvasSize;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>타임라인 열 고르기(애니메이션 = 프레임, 아틀라스 = 스프라이트).</summary>
    public void SetCurrentColumn(int layer, int column)
    {
        if (IsAtlas) SetCurrentSprite(layer, column);
        else SetCurrent(layer, column);
    }

    public void SetCurrentColumn(int column) => SetCurrentColumn(_layer, column);

    public void SetCurrentLayer(int layer) => SetCurrentColumn(layer, CurrentColumn);

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

    /// <summary>재생 순서(태그가 있으면 그 방향대로, 없으면 모든 프레임). 아틀라스는 재생하지 않으므로 비어 있다.</summary>
    public List<int> PlaybackOrder()
        => IsAtlas ? [] : ActiveTag?.GetPlaybackOrder() ?? Enumerable.Range(0, FrameCount).ToList();

    public bool PlaybackLoops => ActiveTag?.Loop ?? true;

    public int FrameDuration(int frame) => Project is { } p && frame >= 0 && frame < p.Frames.Count ? p.Frames[frame].DurationMs : 100;

    // ───────────── 그리기 대상 ─────────────

    /// <summary>현재 레이어가 보이고 잠기지 않았으면 true.</summary>
    public bool CanEdit => Project is { } p && p.Layers[_layer] is { Visible: true, Locked: false };

    /// <summary>현재 셀 이미지(스트로크 중이면 그 이미지, 빈 셀이면 null).</summary>
    public PixelImage? EditImage
        => _strokeImage ?? (Project is not { } p ? null : p.IsAtlas ? p.GetSpriteCel(_layer, _sprite) : p.GetCel(_layer, _frame));

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

    /// <summary>현재 셀에 그리기 시작(빈 셀이면 새로 만듦).</summary>
    public void BeginStroke()
    {
        if (Project is not { } p) return;
        _strokeImage = p.IsAtlas ? p.BeginSpriteStroke(_layer, _sprite) : p.BeginCelStroke(_layer, _frame);
    }

    /// <summary>스트로크 확정(바뀐 것이 없으면 기록 안 함).</summary>
    public void CommitStroke(string label)
    {
        if (Project is null) return;
        _strokeImage = null;
        Project.CommitCelStroke(label);
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

    /// <summary>현재 프레임(아틀라스면 현재 스프라이트)의 피벗(끄는 중이면 미리보기 값).</summary>
    public Pivot Pivot
        => _pivotPreview ?? CurrentAtlasSprite?.Pivot ?? (Project is { } p ? p.GetFramePivot(_frame) : Pivot.Center);

    /// <summary>피벗 끄는 중: 기록 없이 화면만 바꾼다.</summary>
    public void PreviewPivot(Pivot pivot)
    {
        _pivotPreview = pivot;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 끌기를 마친 피벗을 기록한다. 아틀라스면 현재 스프라이트의 피벗, 애니메이션이면 현재 프레임에 피벗 재정의가 있으면
    /// 그 프레임만, 없으면 프로젝트 기본 피벗을 바꾼다.
    /// </summary>
    public void CommitPivot(Pivot before)
    {
        if (Project is not { } p || _pivotPreview is not { } after) return;
        _pivotPreview = null;
        if (after == before) { ViewChanged?.Invoke(this, EventArgs.Empty); return; }
        if (p.IsAtlas) p.SetSpritePivot(_sprite, after);
        else if (p.Frames[_frame].Pivot is not null) p.SetFramePivot(_frame, after);
        else p.Edit("기본 피벗", ProjectChange.Settings, () => p.Sprite.DefaultPivot = after);
    }

    // ───────────── 합성 비트맵 캐시 ─────────────

    private void ClearCaches()
    {
        foreach (var (_, b) in _compositeCache.Values) b.Dispose();
        _compositeCache.Clear();
        foreach (var b in _celCache.Values) b.Dispose();
        _celCache.Clear();
    }

    /// <summary>현재 열(프레임/스프라이트)의 합성·셀 캐시를 버린다.</summary>
    private void InvalidateCurrent()
    {
        if (Project is not { } p) return;
        int column = CurrentColumn;
        if (column < 0 || column >= ColumnCount) return;
        var owner = Owner(p, column);
        if (_compositeCache.Remove(owner, out var entry)) entry.Bitmap.Dispose();
        foreach (var key in _celCache.Keys.Where(k => k.Item2 == owner).ToList())
        {
            _celCache[key].Dispose();
            _celCache.Remove(key);
        }
    }

    /// <summary>열(애니메이션 = 프레임, 아틀라스 = 스프라이트)의 합성 이미지와 비트맵(캐시).</summary>
    private (PixelImage Image, Bitmap Bitmap)? CompositeEntry(int column)
    {
        if (Project is not { } p || column < 0 || column >= ColumnCount) return null;
        var owner = Owner(p, column);
        if (_compositeCache.TryGetValue(owner, out var entry)) return entry;
        PixelImage img;
        if (owner is AtlasSprite s)
        {
            img = new PixelImage(s.Width, s.Height);
            p.FlattenSpriteInto(img, column);
        }
        else
        {
            img = new PixelImage(p.Width, p.Height);
            p.FlattenInto(img, column);
        }
        entry = (img, ImageUtil.ToBitmap(img));
        _compositeCache[owner] = entry;
        return entry;
    }

    /// <summary>애니메이션 프레임의 합성(보이는 레이어) 비트맵(아틀라스면 null). 호출자는 Dispose하지 않는다.</summary>
    public Bitmap? FrameBitmap(int frame) => IsAtlas ? null : CompositeEntry(frame)?.Bitmap;

    /// <summary>현재 프레임(아틀라스면 현재 스프라이트) 합성 비트맵.</summary>
    public Bitmap? DisplayBitmap => CompositeEntry(CurrentColumn)?.Bitmap;

    /// <summary>현재 프레임(아틀라스면 현재 스프라이트)에서 보이는 색.</summary>
    public Color32 CompositeColorAt(int x, int y) => CompositeEntry(CurrentColumn)?.Image.Get(x, y) ?? Color32.Transparent;

    /// <summary>셀 하나(레이어 × 열)의 비트맵(빈 셀이면 null). 호출자는 Dispose하지 않는다.</summary>
    public Bitmap? CelBitmap(int layer, int column)
    {
        if (Project is not { } p || column < 0 || column >= ColumnCount) return null;
        var key = (p.Layers[layer], Owner(p, column));
        if (_celCache.TryGetValue(key, out var bmp)) return bmp;
        var cel = p.IsAtlas ? p.GetSpriteCel(layer, column) : p.GetCel(layer, column);
        if (cel is null) return null;
        bmp = new Bitmap(cel.Width, cel.Height, PixelFormat.Format32bppArgb);
        ImageUtil.CopyToBitmap(cel, bmp);
        _celCache[key] = bmp;
        return bmp;
    }
}
