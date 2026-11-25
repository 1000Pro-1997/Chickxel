using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.Model;

/// <summary>
/// Chickxel 프로젝트(<c>.chickxel</c>): 공통 캔버스 크기 × 레이어 × 프레임의 셀(cel) + 태그(애니메이션) +
/// Unity·내보내기 설정 + 실행취소 기록.
/// <para>
/// 레이어는 아래 → 위 평평한 목록이고, 그 위에 레이어 나무(<see cref="Layer.Parent"/>)가 있다: 레이어의 자손은 그 레이어 바로 아래 번호들에
/// 이어 붙어 있고(형제끼리는 목록 순서), 나무에 드는 레이어는 자리 있는 레이어(<see cref="LayerKind.Part"/>)뿐이다. 그리는 차례는 부모 자신의 그림이 아래,
/// 그 위에 자식 덩어리들(아래 → 위)이다(<see cref="DrawOrder"/>). 부모를 옮기면 자손이 같이 옮겨 가고, 숨기기·잠그기·불투명도는 자손까지 미친다.
/// </para>
/// <para>
/// 레이어마다 스킨(<see cref="Skin"/>) 목록을 갖고 그중 하나(<see cref="Layer.ActiveSkin"/>)만 보인다. 셀은 스킨마다 따로이고,
/// 스킨의 빈 셀(없거나 모두 투명)은 다른 스킨 셀로 채우지 않고 빈 칸으로 보인다. 셀을 읽는 메서드(<see cref="GetCel(int, int)"/>·합성)와
/// 그리는 메서드(스트로크·<see cref="SetCel"/>) 모두 지금 스킨 자신의 셀을 쓴다. 여러 레이어의 스킨 묶음은 스킨 프리셋(<see cref="SkinPresets"/>).
/// </para>
/// <para>
/// 모든 구조 변경 메서드(레이어·프레임·태그·캔버스·설정)는 <see cref="History"/>에 기록되고
/// <see cref="Changed"/>를 발생시키며 수정됨 표시를 켠다. 그리기는 <see cref="BeginCelStroke"/> →
/// 픽셀 수정(필요하면 <see cref="NotifyPixelsChanged"/>로 다시 그리기 알림) → <see cref="CommitCelStroke"/>로
/// 스트로크 하나를 기록한다. 레이어·프레임·태그 객체의 속성을 직접 바꾸지 말고 Set… 메서드나
/// <see cref="Edit(string, ProjectChange, Action)"/> 안에서 바꿔야 실행취소된다.
/// </para>
/// </summary>
public sealed partial class SpriteProject
{
    public static string UntitledName => DomainStrings.Project_UntitledName;
    public const string FileExtension = ".chickxel";
    /// <summary>앱 이름이 Gpixel이던 때의 프로젝트 확장자. 열기·저장은 그대로 되고, 새로 만드는 파일은 늘 <see cref="FileExtension"/>.</summary>
    public const string LegacyFileExtension = ".gpixel";
    /// <summary>프로젝트로 여는 확장자(지금 것 먼저).</summary>
    public static IReadOnlyList<string> FileExtensions { get; } = [FileExtension, LegacyFileExtension];

    /// <summary>경로가 프로젝트 파일(<see cref="FileExtension"/> 또는 <see cref="LegacyFileExtension"/>)인지(대소문자 무시).</summary>
    public static bool IsProjectFile(string path)
        => path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(LegacyFileExtension, StringComparison.OrdinalIgnoreCase);

    private readonly List<Layer> _layers = new();
    private readonly List<Frame> _frames = new();
    private readonly List<AnimationTag> _tags = new();
    private readonly List<SkinPreset> _presets = new();
    /// <summary>
    /// 셀: (레이어, 프레임, 스킨) → 이미지. 일반 레이어는 캔버스 크기, 자리 있는 레이어는 자기 크기, 타일맵은 셀이 없다.
    /// 스킨 키는 레이어의 첫 스킨이 아닌 스킨일 때만 쓰고, 첫 스킨 셀은 null 키다.
    /// </summary>
    private readonly Dictionary<CelKey, PixelImage> _cels = new();
    private int _width, _height;
    private string? _path;
    private string? _tempTitle;
    private bool _isDirty;
    private int _editDepth;
    private StrokeState? _stroke;

    /// <summary>레이어 1개 · 빈 프레임 1개짜리 새 프로젝트.</summary>
    public SpriteProject(int width, int height) : this(width, height, addDefaults: true)
    {
    }

    private SpriteProject(int width, int height, bool addDefaults)
    {
        ValidateSize(width, height);
        _width = width;
        _height = height;
        if (!addDefaults) return;
        _layers.Add(new Layer(NextLayerName()));
        _frames.Add(new Frame());
    }

    /// <summary>레이어·프레임이 하나도 없는 프로젝트(파일 읽기·가져오기용, 내부 전용).</summary>
    internal static SpriteProject CreateBlank(int width, int height) => new(width, height, addDefaults: false);

    /// <summary>이미지 한 장(레이어 1개 · 프레임 1개)으로 새 프로젝트를 만든다.</summary>
    public static SpriteProject FromImage(PixelImage image, string? layerName = null)
    {
        var p = CreateBlank(image.Width, image.Height);
        var layer = new Layer(layerName ?? p.NextLayerName());
        var frame = new Frame();
        p._layers.Add(layer);
        p._frames.Add(frame);
        if (!image.IsRegionEmpty(image.Bounds)) p._cels[new(layer, frame, null)] = image.Clone();
        return p;
    }

    /// <summary>
    /// 같은 크기 그림 여러 장(움직이는 GIF·영상 프레임)으로 새 프로젝트를 만든다: 레이어 1개, 그림마다 프레임 하나(길이 = <paramref name="durationsMs"/>).
    /// 그림이 없거나 크기가 다르면 <see cref="ArgumentException"/>.
    /// </summary>
    public static SpriteProject FromFrames(IReadOnlyList<PixelImage> images, IReadOnlyList<int> durationsMs, string? layerName = null)
    {
        if (images.Count == 0) throw new ArgumentException(null, nameof(images));
        int w = images[0].Width, h = images[0].Height;
        var p = CreateBlank(w, h);
        var layer = new Layer(layerName ?? p.NextLayerName());
        p._layers.Add(layer);
        for (int i = 0; i < images.Count; i++)
        {
            var image = images[i];
            if (image.Width != w || image.Height != h) throw new ArgumentException(null, nameof(images));
            var frame = new Frame { DurationMs = i < durationsMs.Count ? durationsMs[i] : Frame.DefaultDurationMs };
            p._frames.Add(frame);
            if (!image.IsRegionEmpty(image.Bounds)) p._cels[new(layer, frame, null)] = image;
        }
        return p;
    }

    // ───────────── 상태 ─────────────

    public int Width => _width;
    public int Height => _height;
    public Size Size => new(_width, _height);
    public Rectangle Bounds => new(0, 0, _width, _height);

    /// <summary>레이어(0 = 맨 아래). 레이어의 자손은 그 레이어 바로 아래에 이어 붙어 있다(그리는 차례는 <see cref="DrawOrder"/>).</summary>
    public IReadOnlyList<Layer> Layers => _layers;
    public IReadOnlyList<Frame> Frames => _frames;
    public IReadOnlyList<AnimationTag> Tags => _tags;

    /// <summary>스킨 프리셋(레이어마다 입힐 스킨 묶음). 바꿀 때는 스킨 프리셋 메서드로(실행취소됨).</summary>
    public IReadOnlyList<SkinPreset> SkinPresets => _presets;

    /// <summary>Unity 설정(모드·PPU·기본 피벗·Single 테두리·메시·Read/Write). 바꿀 때는 <see cref="Edit(string, ProjectChange, Action)"/> 안에서.</summary>
    public SpriteSettings Sprite { get; } = new() { Mode = SpriteMode.Multiple };

    /// <summary>내보내기 설정. 바꿀 때는 <see cref="Edit(string, ProjectChange, Action)"/> 안에서.</summary>
    public ExportSettings Export { get; } = new();

    /// <summary>실행취소 기록. 스트로크 중일 수 있으므로 실행취소는 <see cref="Undo"/>/<see cref="Redo"/>를 쓴다.</summary>
    public UndoStack History { get; } = new();

    /// <summary><c>.chickxel</c> 파일 경로(저장 전이면 null).</summary>
    public string? Path
    {
        get => _path;
        set
        {
            if (_path == value) return;
            _path = value;
            NotifyChanged(ProjectChange.Path, markDirty: false);
        }
    }

    /// <summary>파일 이름(확장자 포함). 저장 전이면 임시 제목(<see cref="TempTitle"/>), 없으면 <see cref="UntitledName"/>.</summary>
    public string DisplayName => _path is null ? _tempTitle ?? UntitledName : System.IO.Path.GetFileName(_path);

    /// <summary>임시 제목 최대 길이(넘으면 자른다).</summary>
    public const int MaxTempTitleLength = 100;

    /// <summary>
    /// 저장한 적 없는 새 프로젝트의 임시 제목(사용자가 탭 메뉴로, AI가 도구로 정함): 경로가 없는 동안 <see cref="DisplayName"/>(탭·창 제목)에
    /// "새 프로젝트" 대신 보이고, 처음 저장할 때 제안하는 파일 이름이 된다. 파일에 쓰지 않고 실행취소 대상도 아니다.
    /// 줄바꿈 같은 제어 문자는 빈칸으로 바꾸고 앞뒤 공백을 없애며 <see cref="MaxTempTitleLength"/>자로 자른다. 비면 null(없음).
    /// </summary>
    public string? TempTitle
    {
        get => _tempTitle;
        set
        {
            var title = CleanTempTitle(value);
            if (_tempTitle == title) return;
            _tempTitle = title;
            // 이름이 바뀐 것과 같이 알린다(탭·창 제목) — 수정됨 표시는 켜지 않는다.
            NotifyChanged(ProjectChange.Path, markDirty: false);
        }
    }

    private static string? CleanTempTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i])) chars[i] = ' ';
        var title = new string(chars).Trim();
        if (title.Length > MaxTempTitleLength)
            title = title[..(char.IsHighSurrogate(title[MaxTempTitleLength - 1]) ? MaxTempTitleLength - 1 : MaxTempTitleLength)].TrimEnd();
        return title.Length == 0 ? null : title;
    }

    /// <summary>확장자 뺀 파일 이름(칸 이름 규칙 <c>{파일}</c>). 저장 전이면 "sprite".</summary>
    public string BaseName => _path is null ? "sprite" : System.IO.Path.GetFileNameWithoutExtension(_path);

    /// <summary>
    /// 마지막으로 내보낸 결과 파일(전체 경로: 이미지 + 있던 <c>.meta</c>). null이면 기록 없음(예전 파일·다른 이름으로 저장).
    /// 다음 내보내기에서 여기에 있고 이번에 만들지 않는 파일만 "옛 결과"로 정리한다. 실행취소 대상이 아니다.
    /// </summary>
    public IReadOnlyList<string>? ExportedFiles { get; set; }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (value) ChangeStamp++;
            if (_isDirty == value) return;
            _isDirty = value;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 내용이 바뀔 때마다(<see cref="NotifyChanged"/>, 수정됨 켜기) 늘어나는 수. 작업 스레드에서 저장하는 동안 바뀌었는지 알아볼 때 쓴다
    /// (<see cref="Chickxel.Core.IO.ProjectSaveJob.Finish"/>는 그 사이 바뀌었으면 수정됨을 끄지 않는다).
    /// </summary>
    public long ChangeStamp { get; private set; }

    /// <summary>내용이 바뀔 때마다 발생.</summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary><see cref="IsDirty"/>가 바뀔 때 발생.</summary>
    public event EventHandler? DirtyChanged;

    public void NotifyChanged(ProjectChange change, bool markDirty = true)
    {
        ChangeStamp++;
        if (markDirty) IsDirty = true;
        Changed?.Invoke(this, new ProjectChangedEventArgs(change));
    }

    /// <summary>스트로크 중 화면을 다시 그리도록 픽셀 변경을 알린다(수정됨 표시 켬).</summary>
    public void NotifyPixelsChanged() => NotifyChanged(ProjectChange.Pixels);

    public int IndexOf(Layer layer) => _layers.IndexOf(layer);
    public int IndexOf(Frame frame) => _frames.IndexOf(frame);

    public AnimationTag? FindTag(string name) => _tags.Find(t => t.Name == name);

    public bool HasTag(string name) => FindTag(name) is not null;

    /// <summary>프레임이 속한 첫 태그(없으면 null).</summary>
    public AnimationTag? TagOfFrame(int frameIndex) => _tags.Find(t => t.Contains(frameIndex));

    /// <summary>
    /// 효과 목록 <paramref name="effects"/>의 스크립트가 프레임 <paramref name="frameIndex"/>에서 볼 태그 이름(<c>ctx.frameTag</c> — 첫 태그, 없으면 "").
    /// <c>ctx.frameTag</c>를 읽는 켜진 스크립트 효과가 없으면 늘 ""(다른 효과의 그리기·캐시 열쇠는 태그와 무관). <see cref="LayerEffects.WithFrameTag"/>로 넣는다.
    /// </summary>
    public string FrameTagFor(IReadOnlyList<LayerEffect> effects, int frameIndex)
        => LayerEffects.ReadsFrameTag(effects) ? TagOfFrame(frameIndex)?.Name ?? "" : "";

    /// <summary>부위 <paramref name="part"/>의 프레임 <paramref name="frameIndex"/> 위치(캔버스 안 왼쪽 위): 기준 위치 + 그 프레임에서 옮긴 거리(<see cref="MovePartFrom"/>).</summary>
    public Point GetPartOffset(Layer part, int frameIndex)
    {
        var o = part.PartOffset;
        return (uint)frameIndex < (uint)_frames.Count && _frames[frameIndex].PartShifts.TryGetValue(part, out var s) ? new Point(o.X + s.X, o.Y + s.Y) : o;
    }

    /// <summary>부위 <paramref name="part"/>의 프레임 <paramref name="frameIndex"/> 영역(캔버스 좌표, <see cref="GetPartOffset"/> + 부위 크기).</summary>
    public Rectangle GetPartRect(Layer part, int frameIndex) => new(GetPartOffset(part, frameIndex), part.PartSize);

    /// <summary>프레임 사본(표시 시간·피벗·레이어별 프레임 피벗·부위 위치 — 이름은 빼고).</summary>
    private static Frame CopyFrame(Frame src)
    {
        var copy = new Frame { DurationMs = src.DurationMs, Pivot = src.Pivot };
        foreach (var (layer, pivot) in src.LayerPivots) copy.LayerPivots[layer] = pivot;
        foreach (var (part, shift) in src.PartShifts) copy.PartShifts[part] = shift;
        return copy;
    }

    /// <summary>부위의 프레임마다 옮긴 거리를 모두 <paramref name="map"/>으로 바꾼다(0이 되면 지움 — 캔버스 뒤집기·회전·크기 바꾸기). Edit 안에서.</summary>
    private void MapPartShifts(Layer part, Func<Point, Point> map)
    {
        foreach (var f in _frames)
            if (f.PartShifts.TryGetValue(part, out var shift)) SetShift(f, part, map(shift));
    }

    /// <summary>프레임 <paramref name="frame"/>에서 부위가 옮긴 거리를 둔다(0이면 지움). Edit 안에서.</summary>
    private static void SetShift(Frame frame, Layer part, Point shift)
    {
        if (shift.IsEmpty) frame.PartShifts.Remove(part);
        else frame.PartShifts[part] = shift;
    }

    // ───────────── 셀 ─────────────

    /// <summary>셀 키(스킨 = 레이어의 첫 스킨이 아닌 스킨, 아니면 null).</summary>
    internal readonly record struct CelKey(Layer Layer, Frame Frame, Skin? Skin);

    /// <summary>셀 키의 스킨: 레이어의 첫 스킨이 아닌 스킨이면 그 스킨, 아니면 null(첫 스킨·타일맵).</summary>
    private static Skin? SkinKey(Layer layer, Skin? skin)
        => skin is not null && !layer.IsTilemap && !ReferenceEquals(layer.SkinList[0], skin) ? skin : null;

    /// <summary>지금 스킨(<see cref="Layer.ActiveSkin"/>)의 셀 키 스킨.</summary>
    private static Skin? ActiveKey(Layer layer) => SkinKey(layer, layer.ActiveSkin);

    private static bool HasContent(PixelImage? image) => image is not null && !image.IsRegionEmpty(image.Bounds);

    /// <summary>
    /// 보이는 셀 이미지(빈 셀이면 null): 지금 스킨(<see cref="Layer.ActiveSkin"/>) 자신의 셀.
    /// 자리 있는 레이어 셀은 자기 크기, 타일맵은 항상 null. 반환값을 직접 고치지 말고 스트로크로 고친다.
    /// </summary>
    public PixelImage? GetCel(int layerIndex, int frameIndex) => GetCel(_layers[layerIndex], _frames[frameIndex]);

    /// <inheritdoc cref="GetCel(int, int)"/>
    public PixelImage? GetCel(Layer layer, Frame frame) => _cels.GetValueOrDefault(new(layer, frame, ActiveKey(layer)));

    /// <summary>
    /// 스킨 자신의 셀(빈 셀이면 null — 다른 스킨 셀로 채우지 않음). <paramref name="skin"/>이 null이면 지금 스킨. 그 레이어의 스킨이 아니면 null.
    /// </summary>
    public PixelImage? GetOwnCel(Layer layer, Frame frame, Skin? skin = null)
        => _cels.GetValueOrDefault(new(layer, frame, skin is null ? ActiveKey(layer) : SkinKey(layer, skin)));

    /// <summary>보이는 셀(<see cref="GetCel(int, int)"/>)이 없거나 모두 투명이면 true.</summary>
    public bool IsCelEmpty(int layerIndex, int frameIndex)
        => _layers[layerIndex].IsTilemap ? TilemapCel(_layers[layerIndex], frameIndex) is null : !HasContent(GetCel(layerIndex, frameIndex));

    /// <summary>
    /// 일반 레이어에 원본 없이 그림을 만드는 효과(<see cref="LayerEffects.GeneratesContent"/> — 글씨, <c>ctx.frameSource</c>를 읽는 스크립트)가 프레임 <paramref name="frameIndex"/>에 켜져 있는지
    /// (켜져 있고 범위 안). 그러면 그 프레임에 셀이 없어도 빈 그림 위에 효과를 그린다(합성·셀 썸네일·효과 원본·아래로 합치기·영구 적용·내보내기). 자리 있는 레이어·타일맵은 false.
    /// </summary>
    public bool HasContentEffect(int layerIndex, int frameIndex) => HasContentEffect(_layers[layerIndex], frameIndex);

    /// <inheritdoc cref="HasContentEffect(int, int)"/>
    public bool HasContentEffect(Layer layer, int frameIndex) => layer.Kind == LayerKind.Normal && LayerEffects.GeneratesContent(layer.Effects, frameIndex);

    /// <summary>
    /// 그릴 셀: 보이는 셀(<see cref="GetCel(Layer, Frame)"/>), 없으면 <see cref="HasContentEffect(Layer, int)"/>인 프레임의 빈 그림(함께 쓰는 사본 — 고치지 않는다).
    /// </summary>
    private PixelImage? RenderCel(Layer layer, int frameIndex)
        => layer.IsTilemap ? TilemapCel(layer, frameIndex) : GetCel(layer, _frames[frameIndex]) ?? (HasContentEffect(layer, frameIndex) ? BlankCel() : null);

    /// <summary>
    /// 레이어의 저장된 셀(<see cref="CelsOf(Layer)"/>)에, 일반 레이어면 셀이 없지만 <paramref name="effects"/> 중 원본 없이 그리는 효과가 켜진 프레임의 빈 그림을 더한 목록
    /// (아래로 합치기·영구 적용이 효과를 그려 넣을 셀).
    /// </summary>
    private List<(CelKey Key, PixelImage Cel)> CelsWithContentOf(Layer layer, IReadOnlyList<LayerEffect> effects)
    {
        var list = CelsOf(layer);
        if (layer.Kind != LayerKind.Normal) return list;
        for (int f = 0; f < _frames.Count; f++)
        {
            var key = new CelKey(layer, _frames[f], ActiveKey(layer));
            if (!_cels.ContainsKey(key) && LayerEffects.GeneratesContent(effects, f)) list.Add((key, BlankCel()));
        }
        return list;
    }

    private PixelImage? _blankCel;

    /// <summary>캔버스 크기 빈 그림(여러 곳에서 읽기만 하므로 하나를 같이 쓴다 — 렌더 캐시 열쇠도 같아진다). 캔버스 크기가 바뀌면 새로 만든다.</summary>
    private PixelImage BlankCel()
    {
        var blank = _blankCel;
        if (blank is null || blank.Width != _width || blank.Height != _height) _blankCel = blank = new PixelImage(_width, _height);
        return blank;
    }

    /// <summary>
    /// 지금 스킨의 셀 내용을 통째로 바꾼다(null = 비우기, 실행취소됨). 링크된 셀이면 링크된 셀도 모두
    /// 같은 새 그림이 된다(비우기는 이 셀만 비우고 링크에서 뺀다 — 셀 자체를 갈아 끼우려면 <see cref="PlaceCel"/>).
    /// 이미지 크기는 캔버스(자리 있는 레이어면 자기) 크기와 같아야 한다. 타일맵이면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void SetCel(int layerIndex, int frameIndex, PixelImage? image, string? label = null)
    {
        var key = CelKeyFor(layerIndex, frameIndex, image);
        Edit(label ?? DomainStrings.Project_ChangeCel, ProjectChange.Pixels, () =>
        {
            if (image is null) _cels.Remove(key);
            else SetLinked(key, image);
        });
    }

    /// <summary>지금 스킨의 셀 키(타일맵이면 <see cref="InvalidOperationException"/>, 넣을 그림 크기가 셀 크기와 다르면 <see cref="ArgumentException"/>).</summary>
    private CelKey CelKeyFor(int layerIndex, int frameIndex, PixelImage? image)
    {
        var layer = _layers[layerIndex];
        if (layer.IsTilemap) throw new InvalidOperationException(DomainStrings.Tile_CannotDrawPixels);
        var size = CelSize(layer);
        if (image is not null && image.Size != size)
            throw new ArgumentException(layer.IsPart ? DomainStrings.Project_PartCelSizeMismatch : DomainStrings.Project_CanvasCelSizeMismatch, nameof(image));
        return new CelKey(layer, _frames[frameIndex], ActiveKey(layer));
    }

    /// <summary>저장된 모든 셀(모든 스킨 자신의 셀). 읽기 전용으로 쓴다(고치려면 스트로크나 <see cref="MapCels"/>).</summary>
    public IEnumerable<(Layer Layer, Frame Frame, PixelImage Cel)> StoredCels
        => _cels.Select(kv => (kv.Key.Layer, kv.Key.Frame, kv.Value));

    /// <summary>
    /// 여러 셀의 픽셀을 한꺼번에 바꾼다(실행취소 한 단계). <paramref name="include"/>가 참인 레이어·프레임의 저장된 셀마다
    /// (모든 스킨 자신의 셀) <paramref name="map"/>을 부르고, 새 이미지(같은 크기)를 돌려주면 그것으로 바꿔 넣는다(null = 그대로).
    /// 링크된 셀은 그림 하나에 한 번만 부르고, 링크된 셀 모두(<paramref name="include"/>가 거짓인 프레임도)에 같은 새 그림을 넣는다.
    /// 잠긴 레이어(조상이 잠긴 레이어 포함)는 건너뛴다. 바꾼 셀 수를 돌려준다(0이면 기록 없음).
    /// </summary>
    public int MapCels(string label, Func<Layer, Frame, bool> include, Func<PixelImage, PixelImage?> map)
    {
        CommitOpenStrokes();
        var mapped = new Dictionary<PixelImage, PixelImage?>(ReferenceEqualityComparer.Instance);
        foreach (var (key, cel) in _cels)
        {
            if (mapped.ContainsKey(cel) || IsLayerLocked(key.Layer) || !include(key.Layer, key.Frame)) continue;
            var image = map(cel);
            if (image is not null && image.Size != cel.Size) throw new ArgumentException(DomainStrings.Project_ChangedCelSizeMismatch, nameof(map));
            if (image is not null) InheritLinkTag(cel, image);
            mapped[cel] = image;
        }
        var changes = _cels.Where(kv => mapped.GetValueOrDefault(kv.Value) is not null).Select(kv => (kv.Key, Image: mapped[kv.Value]!)).ToList();
        if (changes.Count == 0) return 0;
        Edit(label, ProjectChange.Pixels, () =>
        {
            foreach (var (key, image) in changes) _cels[key] = image;
        });
        return changes.Count;
    }

    /// <summary>레이어 셀의 크기: 자리 있는 레이어면 자기 크기, 아니면 캔버스 크기.</summary>
    public Size CelSize(Layer layer) => layer.IsPart ? layer.PartSize : Size;

    /// <summary>
    /// 프레임의 보이는 레이어를 그리는 차례(<see cref="DrawOrder"/>)대로 합성한 새 이미지(레이어마다 지금 스킨).
    /// </summary>
    public PixelImage Flatten(int frameIndex, bool includeHidden = false)
    {
        var result = new PixelImage(_width, _height);
        FlattenInto(result, frameIndex, includeHidden);
        return result;
    }

    /// <summary>
    /// <paramref name="target"/>(캔버스 크기)을 지우고 프레임을 합성해 넣는다(메모리 재사용).
    /// 일반 레이어는 (0,0)에, 자리 있는 레이어는 그 프레임의 자리(<see cref="GetPartOffset"/>)에 그린다(레이어마다 지금 스킨 자신의 셀). 숨긴 레이어는 자손도 숨긴다.
    /// 레이어 덩어리(자기 그림 + 자식 덩어리들)는 자기 그림이 아래다. 자기 셀에만 입히는 효과는 셀마다 입혀 그리고(밖으로 번진 부분은 캔버스에서 잘림),
    /// 자손까지 합친 그림에 입히는 효과(<see cref="LayerEffect.IncludeChildren"/>)가 이 프레임에 켜져 있거나 불투명도가 255보다 작은 부모는 덩어리를 먼저 합친 그림에
    /// 그 효과를 입혀 자기 불투명도로 한 번에 그린다. 순서표는 <see cref="PlanFrame"/>과 같다.
    /// <paramref name="layerFilter"/>가 있으면 레이어마다 자기 그림을 걸러낸다(자식은 따로 거름, 보이기 여부는 <paramref name="includeHidden"/>로).
    /// <paramref name="celMap"/>이 있으면 그리기 전에 셀 이미지를 그 결과로 바꿔 그린다(미리보기용, 셀은 그대로 — 효과는 바꾼 그림에 입힘).
    /// </summary>
    public void FlattenInto(PixelImage target, int frameIndex, bool includeHidden = false, Func<Layer, bool>? layerFilter = null,
        Func<Layer, PixelImage, PixelImage>? celMap = null)
    {
        if (target.Width != _width || target.Height != _height)
            throw new ArgumentException(DomainStrings.Project_TargetCanvasSizeMismatch, nameof(target));
        PlanFrame(frameIndex, includeHidden, layerFilter, celMap).RenderInto(target);
    }

    /// <summary>
    /// 레이어 하나의 덩어리(자기 그림 + 보이는 자손)만 합성한 캔버스 크기 이미지(썸네일용, 그 레이어 자신의 보이기 여부는 무시, 효과·불투명도 포함 — <see cref="PlanSubtree"/>).
    /// <paramref name="skins"/>에 든 레이어는 그 스킨으로 입혀 본다(나머지는 지금 스킨, 그 스킨의 빈 셀은 빈 칸).
    /// </summary>
    public PixelImage FlattenSubtree(Layer layer, int frameIndex, IReadOnlyDictionary<Layer, Skin>? skins = null) => PlanSubtree(layer, frameIndex, skins).Render();

    /// <summary>
    /// 자기 셀에 입히는 효과(<see cref="OwnEffectsOf"/>)가 입혀질 그림(효과 미리보기용, 효과 없이): 지금 보이는 셀(셀이 없어도 <see cref="HasContentEffect(Layer, int)"/>면 빈 그림).
    /// 빈 셀이면 null. 돌려준 그림은 고치지 않는다. 자손까지 합친 그림은 <see cref="SubtreeEffectSource"/>.
    /// </summary>
    public PixelImage? EffectSource(Layer layer, int frameIndex) => RenderCel(layer, frameIndex);

    /// <summary>
    /// 자손까지 합친 그림에 입히는 효과(<see cref="SubtreeEffectsOf"/>)가 입혀질 그림(효과 미리보기용): 자기 그림(자기 효과까지)과 보이는 자손 덩어리를
    /// 그 불투명도·효과로 합친 캔버스 크기 그림(이 레이어의 자손까지 효과·불투명도 없이). 그릴 것이 없으면 null. 돌려준 그림은 고치지 않는다.
    /// </summary>
    public PixelImage? SubtreeEffectSource(Layer layer, int frameIndex)
    {
        var plan = SubtreeSourcePlan(layer, frameIndex);
        return plan.IsEmpty ? null : plan.Render();
    }

    /// <summary>
    /// 프레임 <paramref name="frameIndex"/>에 자기 셀에 입힐 레이어 효과(<see cref="OwnEffectsOf"/>를 <see cref="LayerEffects.At"/>로): 범위 밖 효과를 빼고, 움직이는 효과는 그 프레임의 진행과
    /// 피벗(<see cref="EffectPivot"/>)을, 다른 프레임 그림을 쓰는 효과(잔상)가 있으면 다른 프레임 원본(<see cref="EffectFrameSource"/>)도 채운다.
    /// 다른 레이어나 <c>ctx.slice</c>를 읽는 스크립트 효과에는 그 프레임의 배치·이웃 레이어(<see cref="ScriptLayersAt"/>)를 채운다.
    /// <paramref name="skin"/>을 주면 다른 프레임도 그 스킨 셀로 본다(레이어 아틀라스의 스킨별 셀). 자손까지 합친 그림에 입히는 효과는 <see cref="SubtreeEffectsAt"/>.
    /// </summary>
    public IReadOnlyList<LayerEffect> EffectsAt(Layer layer, int frameIndex, Skin? skin = null)
        => EffectsAt(layer, OwnEffectsOf(layer), frameIndex, skin is null ? ActiveKey(layer) : SkinKey(layer, skin), skin is null ? null : f => GetOwnCel(layer, _frames[f], skin));

    /// <summary>
    /// 프레임 <paramref name="frameIndex"/>에 자손까지 합친 그림에 입힐 효과(<see cref="SubtreeEffectsOf"/>를 <see cref="LayerEffects.At"/>로 — 피벗은 <see cref="SubtreeEffectPivot"/>,
    /// 다른 프레임 원본은 <see cref="SubtreeFrameSource"/>, 배치는 캔버스 기준). 자식이 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<LayerEffect> SubtreeEffectsAt(Layer layer, int frameIndex) => SubtreeEffectsAt(layer, SubtreeEffectsOf(layer), frameIndex);

    private IReadOnlyList<LayerEffect> SubtreeEffectsAt(Layer layer, IReadOnlyList<LayerEffect> effects, int frameIndex)
    {
        if (effects.Count == 0) return effects;
        Func<int, PixelImage?>? source = LayerEffects.UsesOtherFrames(effects) ? SubtreeFrameSource(layer) : null;
        return LayerEffects.WithFrameTag(LayerEffects.WithScriptLayers(LayerEffects.At(effects, frameIndex, _frames.Count, SubtreeEffectPivot(layer, frameIndex), source,
            source is null ? null : f => SubtreeEffectPivot(layer, f)), ScriptLayersOf(layer, frameIndex, effects, DefaultCels, nested: false, subtree: true)),
            FrameTagFor(effects, frameIndex));
    }

    /// <summary>
    /// 레이어의 효과 목록 <paramref name="effects"/>(자기 셀에 입히는 것)를 프레임 <paramref name="frameIndex"/>에 맞춘다(링크된 칸의 효과 끝 모습 포함 — 셀 키 스킨 <paramref name="celSkin"/>).
    /// 다른 프레임 원본은 <paramref name="cels"/>(프레임 번호 → 셀, null = <see cref="EffectFrameSource"/>), 잔상이 없으면 넣지 않는다(만들 것 없음).
    /// 스크립트가 보는 이웃 레이어 셀은 <paramref name="neighborCels"/>(null = 지금 보이는 셀).
    /// </summary>
    private IReadOnlyList<LayerEffect> EffectsAt(Layer layer, IReadOnlyList<LayerEffect> effects, int frameIndex, Skin? celSkin, Func<int, PixelImage?>? cels,
        Func<Layer, int, PixelImage?>? neighborCels = null)
    {
        effects = WithHeldEnds(layer, effects, frameIndex, celSkin, neighborCels ?? DefaultCels, nested: false);
        Func<int, PixelImage?>? source = !LayerEffects.UsesOtherFrames(effects) ? null
            : cels is null ? EffectFrameSource(layer)
            : f => (uint)f < (uint)_frames.Count ? cels(f) : null;
        return LayerEffects.WithFrameTag(LayerEffects.WithScriptLayers(LayerEffects.At(effects, frameIndex, _frames.Count, EffectPivot(layer, frameIndex), source,
            source is null ? null : f => EffectPivot(layer, f)), ScriptLayersOf(layer, frameIndex, effects, neighborCels ?? DefaultCels, nested: false)),
            FrameTagFor(effects, frameIndex));
    }

    /// <summary>
    /// 자기 셀 효과가 다른 프레임 그림을 볼 때 쓰는 원본(프레임 번호 → <see cref="EffectSource"/> — 셀 그대로, 범위 밖·빈 셀이면 null).
    /// 원본만 제공하고 잔상은 그 프레임의 다른 효과를 별도로 적용하므로 효과 재귀 없이 그 프레임 모습을 얻는다.
    /// </summary>
    public Func<int, PixelImage?> EffectFrameSource(Layer layer) => f => (uint)f < (uint)_frames.Count ? EffectSource(layer, f) : null;

    /// <summary>자손까지 합친 그림 효과가 다른 프레임 그림을 볼 때 쓰는 원본(프레임 번호 → <see cref="SubtreeEffectSource"/>).</summary>
    public Func<int, PixelImage?> SubtreeFrameSource(Layer layer) => f => (uint)f < (uint)_frames.Count ? SubtreeEffectSource(layer, f) : null;

    // ───────────── 스트로크(픽셀 편집) ─────────────

    public bool IsStrokeActive => _stroke is not null;

    /// <summary>
    /// 셀에 그리기 시작: 편집할 셀 이미지(없으면 투명 셀을 새로 만듦, 자리 있는 레이어면 자기 크기)를 돌려준다. 이 이미지를 직접 고친 뒤
    /// <see cref="CommitCelStroke"/>로 바뀐 픽셀을 한 단계로 기록한다. 지금 스킨 자신의 셀이다(비어 있으면 빈 셀에서 시작 —
    /// 다른 스킨 그림을 복사하지 않음, 바꾼 픽셀이 없으면 기록 없이 원래대로).
    /// 잠긴 레이어(조상이 잠긴 레이어 포함)·타일맵이면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public PixelImage BeginCelStroke(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        var frame = _frames[frameIndex];
        if (layer.IsTilemap) throw new InvalidOperationException(DomainStrings.Tile_CannotDrawPixels);
        CommitOpenStrokes();
        if (IsLayerLocked(layerIndex)) throw new InvalidOperationException(DomainStrings.Project_LockedLayerCannotDraw);
        var key = new CelKey(layer, frame, ActiveKey(layer));
        bool created = !_cels.TryGetValue(key, out var image);
        if (created)
        {
            var size = CelSize(layer);
            image = new PixelImage(size.Width, size.Height);
            _cels[key] = image;
        }
        var before = image!.Clone();
        _stroke = new StrokeState(key, image, before, created);
        _strokeOverflowChanged = false;
        _strokeOverflowBefore = null;
        return image;
    }

    /// <summary>스트로크 확정. 바뀐 픽셀이 없으면 기록하지 않고 false.</summary>
    public bool CommitCelStroke(string label)
    {
        var s = _stroke;
        _stroke = null;
        if (s is null) return false;
        FlushStrokeOutside(s.Key);
        if (_strokeOverflowChanged)
        {
            if (!SameOverflow(_overflow.TryGetValue(s.Key, out var o) ? o : null, _strokeOverflowBefore)) return CommitOverflowStroke(s, label);
            // 캔버스 밖 그림이 결국 그대로면 셀 픽셀만 기록한다.
            SetOverflow(s.Key, _strokeOverflowBefore);
            _strokeOverflowChanged = false;
            _strokeOverflowBefore = null;
        }
        var patch = PixelPatch.Create(s.Before, s.Image);
        if (patch is null)
        {
            s.Image.CopyFrom(s.Before);
            if (s.Created) _cels.Remove(s.Key);
            return false;
        }
        History.Push(new CelStrokeCommand(this, label, s, patch));
        NotifyChanged(ProjectChange.Pixels);
        return true;
    }

    /// <summary>스트로크 취소: 셀을 시작 전으로 되돌린다.</summary>
    public void CancelCelStroke()
    {
        var s = _stroke;
        _stroke = null;
        if (s is null) return;
        RevertStrokeOverflow(s);
        s.Image.CopyFrom(s.Before);
        if (s.Created) _cels.Remove(s.Key);
        NotifyChanged(ProjectChange.Pixels, markDirty: false);
    }

    /// <summary>열린 스트로크를 확정한 뒤 실행취소.</summary>
    public bool Undo()
    {
        CommitOpenStrokes();
        return History.Undo();
    }

    /// <summary>열린 스트로크를 확정한 뒤 다시 실행.</summary>
    public bool Redo()
    {
        CommitOpenStrokes();
        return History.Redo();
    }

    // ───────────── 일반 편집(실행취소 기록) ─────────────

    /// <summary>
    /// 실행취소 가능한 편집 하나: 전후 상태(레이어·프레임·태그 목록과 속성, 셀 참조, 캔버스 크기, 설정)를
    /// 기록한다. 안에서 다른 편집 메서드를 불러도 한 단계로 묶인다. 예외가 나면 편집 전으로 되돌린다.
    /// 끝에 자동 배치가 켜진 레이어의 자식 자리를 다시 계산해 같은 단계에 넣는다(<see cref="Layer.AutoLayout"/>).
    /// 셀 이미지는 참조만 기록하므로 <paramref name="action"/> 안에서 기존 셀 픽셀을 직접 고치지 말고
    /// 새 이미지로 바꿔 넣어야 한다(픽셀 편집은 스트로크로).
    /// </summary>
    public void Edit(string label, ProjectChange change, Action action)
    {
        if (_editDepth > 0)
        {
            action();
            return;
        }
        CommitOpenStrokes();
        var before = Snapshot.Capture(this);
        _editDepth++;
        try
        {
            action();
            change |= RelayoutChildren();
        }
        catch
        {
            before.Restore(this);
            throw;
        }
        finally
        {
            _editDepth--;
        }
        History.Push(new SnapshotCommand(this, label, before, Snapshot.Capture(this), change));
        NotifyChanged(change);
    }

    public T Edit<T>(string label, ProjectChange change, Func<T> action)
    {
        T result = default!;
        Edit(label, change, () => { result = action(); });
        return result;
    }

    // ───────────── 레이어 ─────────────

    /// <summary>
    /// 부위 크기 바꾸기: 모든 프레임의 셀을 <paramref name="anchor"/> 기준으로 옮기고, 그림이 캔버스에서 같은 자리에 남도록 위치를,
    /// 같은 그림 지점을 가리키도록 피벗을 보정한다.
    /// </summary>
    public void ResizePart(int layerIndex, int width, int height, Anchor anchor)
    {
        var part = RequirePart(layerIndex);
        ValidatePartSize(width, height);
        var old = part.PartSize;
        if (old.Width == width && old.Height == height) return;
        var (ox, oy) = PixelImage.AnchorOffset(old.Width, old.Height, width, height, anchor);
        Pivot Moved(Pivot p)
        {
            var (px, py) = p.ToLocalPixel(old.Width, old.Height);
            return new Pivot((px + ox) / width, 1f - (py + oy) / height);
        }
        var offset = new Point(part.PartOffset.X - ox, part.PartOffset.Y - oy);
        Edit(DomainStrings.Project_ResizePart, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var resize = OncePerImage(cel => cel.ResizeCanvas(width, height, anchor));
            foreach (var (key, cel) in CelsOf(part)) _cels[key] = resize(cel);
            part.PartSize = new Size(width, height);
            part.PartOffset = offset;
            MapLayerPivots(part, Moved);
        });
    }

    /// <summary>
    /// 부위 크기 바꾸기 + 그림 늘이기(부위 화면의 크기 모드): 새 부위 칸은 지금 부위 좌표 <paramref name="canvas"/> 자리(크기 = 새 부위 크기)이고,
    /// 모든 프레임·스킨의 셀을 새 칸 좌표 <paramref name="image"/> 자리에 그 크기로 늘이거나 줄여 놓는다(최근접 — <paramref name="slice"/>를 주고
    /// 부위 테두리를 쓸 수 있으면 9-slice로). 부위 위치는 그림이 프레임에서 같은 자리에 남도록 <paramref name="canvas"/> 왼쪽 위만큼 옮기고, 피벗은 그림을 따라간다.
    /// </summary>
    public void ResizePartScaled(int layerIndex, Rectangle canvas, Rectangle image, NineSliceFill? slice = null)
    {
        var part = RequirePart(layerIndex);
        ValidatePartSize(canvas.Width, canvas.Height);
        if (image.Width < 1 || image.Height < 1 || image.Width > PixelImage.MaxSize || image.Height > PixelImage.MaxSize)
            throw new ArgumentOutOfRangeException(nameof(image), DomainStrings.FormatProject_InvalidImageSize(max: PixelImage.MaxSize, width: image.Width, height: image.Height));
        var old = part.PartSize;
        if (canvas == new Rectangle(Point.Empty, old) && image == new Rectangle(Point.Empty, old)) return;
        var border = slice is not null && NineSlice.Applies(part.PartBorder, old.Width, old.Height) ? part.PartBorder : SpriteBorder.None;
        var fill = slice ?? NineSliceFill.Stretch;
        Pivot Moved(Pivot p)
        {
            var (px, py) = p.ToLocalPixel(old.Width, old.Height);
            var at = MapResizedCanvasPoint(new PointF(px, py), old, image, border);
            return new Pivot(at.X / canvas.Width, 1f - at.Y / canvas.Height);
        }
        var offset = new Point(part.PartOffset.X + canvas.X, part.PartOffset.Y + canvas.Y);
        Edit(DomainStrings.Project_ResizePart, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var resize = OncePerImage(cel => ImageTransform.ResizeCanvasScaled(cel, canvas.Width, canvas.Height, image, border, fill));
            foreach (var (key, cel) in CelsOf(part)) _cels[key] = resize(cel);
            part.PartSize = canvas.Size;
            part.PartOffset = offset;
            MapLayerPivots(part, Moved);
        });
    }

    /// <summary>
    /// 레이어 크기(타임라인 칸 썸네일 자리, 캔버스 좌표): 부위는 부위 영역, 일반 레이어는 모든 프레임 셀(캔버스 밖 그림 포함)의 칠한 픽셀을 감싸는 사각형.
    /// 칠한 픽셀이 없으면 <see cref="Rectangle.Empty"/>.
    /// </summary>
    public Rectangle LayerBounds(int layerIndex)
    {
        var layer = _layers[layerIndex];
        if (layer.IsPart) return layer.PartRect;
        if (layer.IsTilemap) return TilemapBounds(layer);
        var bounds = Rectangle.Empty;
        foreach (var key in LayerCelKeys(layer))
        {
            var (full, origin) = FullOf(key, _cels.GetValueOrDefault(key), Size);
            var r = full.GetOpaqueBounds();
            if (r.IsEmpty) continue;
            r.Offset(origin);
            bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
        }
        return bounds;
    }

    /// <summary>
    /// 모든 레이어 그림을 감싸는 가장 작은 사각형(캔버스 좌표 — 캔버스 밖으로 나갈 수 있음): 일반·타일맵 레이어는 <see cref="LayerBounds"/>(모든 프레임, 캔버스 밖 그림 포함),
    /// 자리 있는 레이어는 그 영역(프레임마다 옮긴 자리 모두 — 셀 없는 빈 부모는 빼고). 숨긴 레이어도 넣는다. 그림이 하나도 없으면 <see cref="Rectangle.Empty"/>.
    /// </summary>
    public Rectangle ContentBounds()
    {
        var bounds = Rectangle.Empty;
        void Add(Rectangle r)
        {
            if (r.Width > 0 && r.Height > 0) bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
        }
        for (int i = 0; i < _layers.Count; i++)
        {
            var layer = _layers[i];
            if (!layer.IsPart)
            {
                Add(LayerBounds(i));
                continue;
            }
            if (HasChildren(layer) && !_cels.Keys.Any(k => ReferenceEquals(k.Layer, layer))) continue; // 셀 없는 빈 부모는 담는 칸일 뿐
            Add(layer.PartRect);
            foreach (var f in _frames)
                if (f.PartShifts.TryGetValue(layer, out var s)) Add(layer.PartRect with { X = layer.PartOffset.X + s.X, Y = layer.PartOffset.Y + s.Y });
        }
        return bounds;
    }

    /// <summary>
    /// 타임라인 칸 썸네일로 보일 자리(캔버스 좌표 — 그 레이어가 칸에 크게 보이게, 썸네일이 효과 없는 그림이라 효과 여백 없이): 일반 레이어는 <see cref="LayerBounds"/>,
    /// <paramref name="withChildren"/>이고 자식이 있으면 자기와 자손의 영역들(프레임마다 옮긴 자리 모두, 셀 없는 빈 부모는 빼고)을 합친 사각형을 캔버스 안으로 자른 것
    /// (<see cref="PlanCelThumbnail"/>의 <c>withChildren</c>과 짝). 모든 프레임에서 같으므로 칸마다 같은 자리·배율이다.
    /// 자리 있는 레이어 자기 그림만(썸네일이 이미 자기 크기)·칠한 픽셀이 없음·캔버스 전체면 <see cref="Rectangle.Empty"/>(자르지 않음).
    /// </summary>
    public Rectangle ThumbnailBounds(int layerIndex, bool withChildren = false)
    {
        var layer = _layers[layerIndex];
        Rectangle bounds;
        if (withChildren && HasChildren(layer))
        {
            bounds = Rectangle.Empty;
            foreach (var part in SubtreeOf(layer))
            {
                if (part.PartSize.Width < 1 || part.PartSize.Height < 1) continue;
                if (HasChildren(part) && !_cels.Keys.Any(k => ReferenceEquals(k.Layer, part))) continue; // 셀 없는 빈 부모는 담는 칸일 뿐
                var r = part.PartRect;
                foreach (var f in _frames)
                    if (f.PartShifts.TryGetValue(part, out var s)) r = Rectangle.Union(r, part.PartRect with { X = part.PartOffset.X + s.X, Y = part.PartOffset.Y + s.Y });
                bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
            }
        }
        else if (layer.IsPart) return Rectangle.Empty;
        else bounds = LayerBounds(layerIndex);
        if (bounds.IsEmpty) return Rectangle.Empty;
        bounds = Rectangle.Intersect(bounds, new Rectangle(0, 0, _width, _height));
        return bounds.Width < 1 || bounds.Height < 1 || bounds.Size == Size ? Rectangle.Empty : bounds;
    }

    /// <summary>
    /// 캔버스 크기 바꾸기(<see cref="ResizeCanvasAndScale"/>)의 대응: 원래 캔버스(<paramref name="old"/>) 좌표의 점(연속)이 그림을 <paramref name="image"/>(새 캔버스 좌표)에 놓은 뒤
    /// 놓이는 곳. <paramref name="border"/>가 있으면 9-slice(테두리 안은 그대로, 가운데만 비율대로), 없으면 전체를 비율대로.
    /// </summary>
    public static PointF MapResizedCanvasPoint(PointF p, Size old, Rectangle image, SpriteBorder border)
        => new((float)MapResizedAxis(p.X, old.Width, border.Left, border.Right, image.X, image.Width, !border.IsNone),
            (float)MapResizedAxis(p.Y, old.Height, border.Top, border.Bottom, image.Y, image.Height, !border.IsNone));

    /// <summary>캔버스 크기 바꾸기의 한 축 대응(원래 좌표 <paramref name="v"/> → 새 캔버스 좌표, 그림 자리 <paramref name="pos"/>·<paramref name="size"/>).</summary>
    private static double MapResizedAxis(double v, int old, int borderStart, int borderEnd, int pos, int size, bool sliced)
        => pos + (sliced ? NineSlice.MapCoordinate(v, old, borderStart, borderEnd, size) : v * size / old);

    /// <summary>일반 레이어의 셀 키(셀이 있거나 캔버스 밖 그림만 있는 프레임, 겹치면 한 번).</summary>
    private List<CelKey> LayerCelKeys(Layer layer)
        => _cels.Keys.Where(k => ReferenceEquals(k.Layer, layer)).Concat(_overflow.Keys.Where(k => ReferenceEquals(k.Layer, layer))).Distinct().ToList();

    /// <summary>프레임 <paramref name="fromFrame"/>부터 끝까지(범위 밖이면 <see cref="ArgumentOutOfRangeException"/>).</summary>
    private List<Frame> FramesFrom(int fromFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromFrame, _frames.Count);
        return _frames.GetRange(fromFrame, _frames.Count - fromFrame);
    }

    /// <summary>부위 9-slice 테두리(px). 부위 아틀라스 내보내기에 쓴다.</summary>
    public void SetPartBorder(int layerIndex, SpriteBorder border)
    {
        var part = RequirePart(layerIndex);
        Edit(DomainStrings.Project_PartBorder, ProjectChange.Layers, () => part.PartBorder = border);
    }

    /// <summary>타임라인 효과 줄 접기/펼치기(표시용, 실행취소·수정됨 표시 없음).</summary>
    public void SetLayerEffectsCollapsed(int layerIndex, bool collapsed)
    {
        var layer = _layers[layerIndex];
        if (layer.EffectsCollapsed == collapsed) return;
        layer.EffectsCollapsed = collapsed;
        NotifyChanged(ProjectChange.Layers, markDirty: false);
    }

    /// <summary>
    /// 일반 레이어를 바로 아래 일반 레이어에 병합한다(위 레이어를 효과를 구워 넣고 불투명도대로 합성, 보이기 여부와 관계없이 — 캔버스 밖으로 번진 효과는 잘림).
    /// 위 레이어는 삭제된다. 일반 레이어가 아니거나(자리 있는 레이어·타일맵) 스킨이 둘 이상인 레이어가 끼면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void MergeLayerDown(int index)
    {
        if (index <= 0 || index >= _layers.Count) throw new ArgumentOutOfRangeException(nameof(index), DomainStrings.Project_NoLayerBelow);
        var upper = _layers[index];
        var lower = _layers[index - 1];
        if (upper.Kind != LayerKind.Normal || lower.Kind != LayerKind.Normal)
            throw new InvalidOperationException(DomainStrings.Project_CannotMergePartGroup);
        if (upper.SkinList.Count > 1 || lower.SkinList.Count > 1) throw new InvalidOperationException(DomainStrings.Project_CannotMergeSkins);
        Edit(DomainStrings.Project_MergeDown, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            // 효과(잔상)가 위 레이어의 다른 프레임 셀을 볼 수 있으므로 셀을 바꾸기 전에 모두 그려 둔다. 셀이 없어도 글씨처럼 원본 없이 그리는 효과가 켜진 프레임은 빈 그림에 그린다.
            (PixelImage Image, int OffsetX, int OffsetY) RenderUpper(CelKey key, PixelImage cel)
            {
                var effects = EffectsAt(upper, _frames.IndexOf(key.Frame));
                return LayerEffects.UsesOtherFrames(effects)
                    ? LayerEffects.Render(cel, effects, LayerEffects.Padding(upper.Effects, cel.Width, cel.Height))
                    : LayerEffects.Render(cel, effects);
            }
            var rendered = CelsWithContentOf(upper, upper.Effects).Select(c => (c.Key, Render: RenderUpper(c.Key, c.Cel))).ToList();
            // 아래 레이어의 링크된 셀은 합친 결과가 모두 같으면 계속 같이 쓴다
            var merges = new List<(CelKey Key, PixelImage? Before, PixelImage After)>();
            foreach (var (key, (image, dx, dy)) in rendered)
            {
                var lowerKey = new CelKey(lower, key.Frame, null);
                var bottom = _cels.GetValueOrDefault(lowerKey);
                var merged = bottom?.Clone() ?? new PixelImage(_width, _height);
                merged.Composite(image, dx, dy, upper.Opacity);
                if (_overflow.TryGetValue(key, out var upperOverflow))
                {
                    var (cel, overflow) = MergeOverflow(lowerKey, merged, upperOverflow, upper.Opacity);
                    merged = cel;
                    SetOverflow(lowerKey, overflow);
                }
                merges.Add((lowerKey, bottom, merged));
                _cels.Remove(key);
            }
            // 셀 없이 캔버스 밖 그림만 있는 프레임
            foreach (var (key, upperOverflow) in _overflow.Where(kv => ReferenceEquals(kv.Key.Layer, upper) && !rendered.Any(r => r.Key == kv.Key)).ToList())
            {
                var lowerKey = new CelKey(lower, key.Frame, null);
                var bottom = _cels.GetValueOrDefault(lowerKey);
                var (cel, overflow) = MergeOverflow(lowerKey, bottom ?? new PixelImage(_width, _height), upperOverflow, upper.Opacity);
                merges.Add((lowerKey, bottom, cel));
                SetOverflow(lowerKey, overflow);
            }
            PutKeepingLinks(merges);
            RemoveOverflowOf(upper);
            if (ReferenceEquals(PivotLayer, upper)) PivotLayer = null;
            _layers.RemoveAt(index);
        });
    }

    /// <summary>레이어의 모든 셀(모든 스킨, 키와 이미지) 사본 목록.</summary>
    private List<(CelKey Key, PixelImage Cel)> CelsOf(Layer layer)
        => _cels.Where(kv => ReferenceEquals(kv.Key.Layer, layer)).Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>프레임의 모든 셀(모든 레이어·스킨) 사본 목록.</summary>
    private List<(CelKey Key, PixelImage Cel)> CelsOf(Frame frame)
        => _cels.Where(kv => ReferenceEquals(kv.Key.Frame, frame)).Select(kv => (kv.Key, kv.Value)).ToList();

    /// <summary>레이어 속성 바꾸기(null인 값은 그대로).</summary>
    public void SetLayerProperties(int index, string? name = null, bool? visible = null, bool? locked = null, byte? opacity = null)
    {
        var layer = _layers[index];
        Edit(DomainStrings.Project_LayerProperties, ProjectChange.Layers, () =>
        {
            if (!string.IsNullOrWhiteSpace(name)) layer.Name = name.Trim();
            if (visible is { } v) layer.Visible = v;
            if (locked is { } l) layer.Locked = l;
            if (opacity is { } o) layer.Opacity = o;
        });
    }

    /// <summary>레이어 효과 목록을 통째로 바꾼다(실행취소 한 단계, 같으면 기록 없음). 일반·부위·그룹 모두.</summary>
    public void SetLayerEffects(int index, IReadOnlyList<LayerEffect> effects)
    {
        var layer = _layers[index];
        var list = effects.ToArray();
        if (layer.Effects.SequenceEqual(list)) return;
        Edit(DomainStrings.Project_LayerEffects, ProjectChange.Layers, () => layer.Effects = list);
    }

    /// <summary>
    /// 효과 미리보기(대화상자에서 고치는 동안): 실행취소 기록·수정됨 표시 없이 바꾸고 다시 그리게 한다.
    /// 끝낼 때 <see cref="EndLayerEffectsPreview"/>를 반드시 부른다.
    /// </summary>
    public void PreviewLayerEffects(int index, IReadOnlyList<LayerEffect> effects)
    {
        _layers[index].Effects = effects.ToArray();
        NotifyChanged(ProjectChange.Layers, markDirty: false);
    }

    /// <summary>
    /// 효과 미리보기 끝: 레이어를 미리보기 전 효과(<paramref name="original"/>)로 되돌리고, <paramref name="commit"/>이면 미리보던 효과를
    /// <see cref="SetLayerEffects"/>로 확정한다(실행취소 한 단계 = 대화상자 한 번).
    /// </summary>
    public void EndLayerEffectsPreview(int index, IReadOnlyList<LayerEffect> original, bool commit)
    {
        var layer = _layers[index];
        var previewed = layer.Effects;
        layer.Effects = original.ToArray();
        if (commit) SetLayerEffects(index, previewed);
        if (!commit || layer.Effects.SequenceEqual(original)) NotifyChanged(ProjectChange.Layers, markDirty: false);
    }

    /// <summary>
    /// 효과 영구 적용(실행취소 한 단계): <see cref="LayerEffects.BakeSet"/>의 효과(고른 효과 + 그 위의 켜진 효과)를 그 효과들이 입혀지는 프레임의
    /// 저장된 셀마다(모든 스킨 자신의 셀) 보이던 그대로 구워 넣고 목록에서 뺀다.
    /// 일반 레이어에서 구울 효과 중 글씨처럼 원본 없이 그리는 효과가 켜진 프레임은 셀이 없어도 빈 그림에 구워 새 셀로 넣는다.
    /// 아래(뒤) 효과는 효과로 남는다. 일반 레이어는 캔버스 크기로 잘리고, 부위는 번진 그림이 들어가도록 부위 크기·위치를 넓힌다
    /// (모든 프레임·스킨 셀을 같이 옮기고, 피벗·테두리는 같은 그림 지점 — 자동 배치 그룹이면 다시 배치). 효과가 바꾸지 않는 프레임도 그대로 구운 것으로 친다.
    /// 부위에서 피벗 같이 옮기기가 켜진 효과(흔들기·둥실둥실 등)가 그림을 옮긴 거리는 그림에 굽지 않고, 그 프레임의 부위 위치를 그만큼(반올림) 옮긴다
    /// (<see cref="GetPartOffset"/> — 피벗은 부위 기준 비율이라 같이 따라가고, 그 프레임은 "현재 프레임부터" 따로 둔 프레임이 된다).
    /// 자손까지 합친 그림에 입히는 효과(자식이 있는 레이어의 <see cref="LayerEffect.IncludeChildren"/>)가 끼거나 잠긴 레이어면 <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <returns>구운 효과(목록 순서)와 그 프레임 범위(합, null = 모든 프레임), 바꾼 셀 수, 부위를 넓혔는지.</returns>
    public (IReadOnlyList<LayerEffect> Baked, FrameRange? Frames, int Cels, bool Grown) BakeLayerEffects(int layerIndex, IEnumerable<int> effectIndices)
        => ApplyBake(PrepareBake(layerIndex, effectIndices));

    /// <summary>
    /// 굽기 계산(<see cref="BakeLayerEffects"/>의 무거운 부분 — 셀마다 효과 입히기). 프로젝트는 읽기만 하므로 아무도 프로젝트를 고치지 않는 동안
    /// 작업 스레드에서 돌려도 된다. 결과는 <see cref="ApplyBake"/>로 같은 상태에 넣는다. 자손까지 효과·잠긴 레이어·구울 효과 없음은 여기서 예외.
    /// </summary>
    public BakePlan PrepareBake(int layerIndex, IEnumerable<int> effectIndices)
    {
        var layer = _layers[layerIndex];
        if (IsLayerLocked(layer)) throw new InvalidOperationException(DomainStrings.Project_CannotBakeLockedLayerEffects);
        var order = LayerEffects.BakeSet(layer.Effects, effectIndices);
        if (order.Count == 0) throw new ArgumentException(DomainStrings.Project_NoEffectsToBake, nameof(effectIndices));
        if (HasChildren(layer) && order.Any(i => layer.Effects[i].IncludeChildren)) throw new InvalidOperationException(DomainStrings.Project_CannotBakeGroupEffects);
        LayerEffect[] baked = [.. order.Select(i => layer.Effects[i] with { Enabled = true })];
        LayerEffect[] rest = [.. layer.Effects.Where((_, i) => !order.Contains(i))];
        FrameRange? range = baked.Any(e => e.Frames is null) ? null
            : FrameRange.Of(baked.Min(e => e.Frames!.Value.From), baked.Max(e => e.Frames!.Value.To), _frames.Count);

        var size = CelSize(layer);
        var pad = layer.IsPart ? LayerEffects.Padding(baked, size.Width, size.Height) : default;
        // 자리는 원래 셀 왼쪽 위 기준(그림마다 여백이 다를 수 있음 — 옮겨 간 만큼 넓혀 그린 프레임)
        var keep = new Rectangle(0, 0, size.Width, size.Height);
        var rendered = new List<(CelKey Key, PixelImage Image, Point Offset)>();
        // 부위: 프레임마다 효과가 옮긴 거리(반올림 — 그 프레임의 첫 셀 기준, 스킨이 달라도 같은 위치를 쓴다)
        var shifts = new Dictionary<Frame, Point>();
        var area = keep;
        // 셀이 없어도 구울 효과 중 글씨처럼 원본 없이 그리는 효과가 켜진 프레임은 빈 그림에 그려 새 셀로 넣는다
        foreach (var (key, cel) in CelsWithContentOf(layer, baked))
        {
            int f = _frames.IndexOf(key.Frame);
            // 잔상은 같은 스킨의 다른 프레임 셀(굽기 전 그대로, 셀이 없으면 그리는 것과 같이 빈 그림)을 본다
            var skinKey = key.Skin;
            var effects = EffectsAt(layer, baked, f, skinKey,
                other => _cels.GetValueOrDefault(new(layer, _frames[other], skinKey)) ?? (HasContentEffect(layer, other) ? BlankCel() : null));
            if (effects.Count == 0) continue;
            var (image, dx, dy, moved) = LayerEffects.RenderWithShift(cel, effects, pad);
            if (layer.IsPart)
            {
                if (!shifts.TryGetValue(key.Frame, out var s))
                    shifts[key.Frame] = s = new Point((int)MathF.Round(moved.X, MidpointRounding.AwayFromZero), (int)MathF.Round(moved.Y, MidpointRounding.AwayFromZero));
                dx -= s.X;
                dy -= s.Y;
            }
            rendered.Add((key, image, new Point(dx, dy)));
            if (layer.IsPart && image.GetOpaqueBounds() is { IsEmpty: false } b)
            {
                b.Offset(dx, dy);
                area = Rectangle.Union(area, b);
            }
        }
        foreach (var (frame, s) in shifts.Where(kv => kv.Value.IsEmpty).ToList()) shifts.Remove(frame);
        bool grow = area != keep;
        if (grow) ValidatePartSize(area.Width, area.Height);
        return new BakePlan(layer, baked, rest, range, size, keep, area, rendered, shifts);
    }

    /// <summary>
    /// 계산한 굽기를 넣는다(실행취소 한 단계, UI 스레드). <see cref="PrepareBake"/> 바로 뒤, 그 사이 프로젝트를 고치지 않은 채로 부른다.
    /// </summary>
    public (IReadOnlyList<LayerEffect> Baked, FrameRange? Frames, int Cels, bool Grown) ApplyBake(BakePlan plan)
    {
        var layer = plan.Layer;
        var (size, keep, area) = (plan.Size, plan.Keep, plan.Area);
        bool grow = area != keep;
        // 구운 셀의 원래 그림(링크 — 같은 그림을 같이 쓰던 셀 — 은 구운 결과가 모두 같으면 계속 같이 쓴다)
        var before = plan.Rendered.Where(r => _cels.ContainsKey(r.Key)).ToDictionary(r => r.Key, r => _cels[r.Key]);
        Edit(DomainStrings.Project_BakeEffects, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            if (grow)
            {
                int ox = keep.X - area.X, oy = keep.Y - area.Y; // 넓힌 부위 안 원래 셀 자리
                var grown = OncePerImage(cel =>
                {
                    var moved = new PixelImage(area.Width, area.Height);
                    moved.Blit(cel, ox, oy);
                    return moved;
                });
                foreach (var (key, cel) in CelsOf(layer)) _cels[key] = grown(cel);
                MapLayerPivots(layer, p =>
                {
                    var (px, py) = p.ToLocalPixel(size.Width, size.Height);
                    return new Pivot((px + ox) / area.Width, 1f - (py + oy) / area.Height);
                });
                if (!layer.PartBorder.IsNone)
                {
                    var b = layer.PartBorder;
                    layer.PartBorder = new SpriteBorder(b.Left + ox, b.Top + oy, b.Right + area.Right - keep.Right, b.Bottom + area.Bottom - keep.Bottom);
                }
                layer.PartOffset = new Point(layer.PartOffset.X - ox, layer.PartOffset.Y - oy);
                layer.PartSize = area.Size;
            }
            PutKeepingLinks(plan.Rendered.Select(r =>
            {
                // 구운 그림을 새 셀 자리(area, 원래 셀 기준)에 놓는다 — 밖은 버림
                var placed = new PixelImage(area.Width, area.Height);
                placed.Blit(r.Image, r.Offset.X - area.X, r.Offset.Y - area.Y);
                return (r.Key, before.GetValueOrDefault(r.Key), placed);
            }).ToList());
            // 효과가 옮긴 거리만큼 그 프레임의 부위 위치를 옮긴다(넓혀 바뀐 기준 위치에 더하는 거리라 그대로 따라감)
            foreach (var (frame, s) in plan.Shifts)
            {
                var cur = frame.PartShifts.GetValueOrDefault(layer);
                SetShift(frame, layer, new Point(cur.X + s.X, cur.Y + s.Y));
            }
            layer.Effects = plan.Rest;
        });
        return (plan.Baked, plan.Frames, plan.Rendered.Count, grow);
    }

    /// <summary><see cref="PrepareBake"/>의 결과(넣기 전): 구울 효과·남길 효과, 셀마다 효과 입힌 그림, 부위를 넓힐 범위.</summary>
    public sealed class BakePlan
    {
        internal BakePlan(Layer layer, IReadOnlyList<LayerEffect> baked, IReadOnlyList<LayerEffect> rest, FrameRange? frames, Size size,
            Rectangle keep, Rectangle area, IReadOnlyList<(CelKey Key, PixelImage Image, Point Offset)> rendered, IReadOnlyDictionary<Frame, Point> shifts)
        {
            Layer = layer;
            Baked = baked;
            Rest = rest;
            Frames = frames;
            Size = size;
            Keep = keep;
            Area = area;
            Rendered = rendered;
            Shifts = shifts;
        }

        internal Layer Layer { get; }
        internal IReadOnlyList<LayerEffect> Baked { get; }
        internal IReadOnlyList<LayerEffect> Rest { get; }
        internal FrameRange? Frames { get; }
        internal Size Size { get; }
        /// <summary>원래 셀 자리(원래 셀 왼쪽 위 기준 — 늘 (0, 0, 셀 크기)).</summary>
        internal Rectangle Keep { get; }
        /// <summary>구운 셀이 차지할 자리(원래 셀 왼쪽 위 기준, 부위면 결과를 모두 감쌈).</summary>
        internal Rectangle Area { get; }
        /// <summary>셀마다 효과를 입힌 그림과 그 그림 왼쪽 위의 자리(원래 셀 왼쪽 위 기준, 부위면 <see cref="Shifts"/>만큼 되돌린 자리).</summary>
        internal IReadOnlyList<(CelKey Key, PixelImage Image, Point Offset)> Rendered { get; }
        /// <summary>부위: 프레임마다 효과가 옮긴 거리(0이 아닌 것만) — 그림 대신 그 프레임의 부위 위치를 옮긴다.</summary>
        internal IReadOnlyDictionary<Frame, Point> Shifts { get; }
    }

    // ───────────── 프레임 ─────────────

    /// <summary>
    /// 빈 프레임을 <paramref name="index"/>(기본: 맨 끝)에 넣는다. 길이는 앞 프레임과 같다.
    /// 태그·효과 프레임 범위 안(시작 뒤 ~ 끝)에 넣으면 그 범위가 늘어나고, 뒤쪽 범위는 밀린다.
    /// </summary>
    public Frame AddFrame(int? index = null)
    {
        int at = Math.Clamp(index ?? _frames.Count, 0, _frames.Count);
        var frame = new Frame();
        if (_frames.Count > 0) frame.DurationMs = _frames[Math.Max(0, at - 1)].DurationMs;
        Edit(DomainStrings.Project_AddFrame, ProjectChange.Frames | ProjectChange.Tags, () =>
        {
            _frames.Insert(at, frame);
            foreach (var t in _tags)
            {
                if (t.From >= at) { t.From++; t.To++; }
                else if (t.To >= at) t.To++;
            }
            MapEffectRanges(r => r.From >= at ? new FrameRange(r.From + 1, r.To + 1) : r.To >= at ? new FrameRange(r.From, r.To + 1) : r);
        });
        return frame;
    }

    /// <summary>
    /// 프레임을 셀까지 복사해 바로 뒤에 넣는다. 원래 셀이 다른 프레임과 링크되어 있으면 사본 칸도 그 링크에 들어가고(그림을 같이 씀), 아니면 그림 사본이다.
    /// 원래 프레임을 포함한 태그·효과 범위는 늘어난다(<paramref name="extendTags"/>가 false면 태그는 늘지 않음 — 맨 끝에 복사로 붙일 때).
    /// </summary>
    public Frame DuplicateFrame(int index, bool extendTags = true)
    {
        var src = _frames[index];
        var copy = CopyFrame(src);
        Edit(DomainStrings.Project_DuplicateFrame, ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            _frames.Insert(index + 1, copy);
            foreach (var (key, cel) in CelsOf(src))
                _cels[key with { Frame = copy }] = LinkedKeys(key).Count > 1 ? cel : cel.Clone();
            CopyOverflow(src, copy);
            foreach (var t in _tags)
            {
                if (t.From > index) { t.From++; t.To++; }
                else if (t.To > index || t.To == index && extendTags) t.To++;
            }
            MapEffectRanges(r => r.From > index ? new FrameRange(r.From + 1, r.To + 1) : r.To >= index ? new FrameRange(r.From, r.To + 1) : r);
        });
        return copy;
    }

    /// <inheritdoc cref="ExtendFrames(FrameExtension)"/>
    public void ExtendFrames(int from, int to, int count, FrameExtendMode mode) => ExtendFrames(new FrameExtension(from, to, count, mode));

    /// <summary>
    /// 프레임 늘리기(실행취소 한 단계): 프레임 [From, To]를 <see cref="FrameExtension.Count"/>개로 바꾼다 — 자리마다 <see cref="FrameExtension.SourceOf"/>의 원래 프레임,
    /// 원래 프레임이 처음 오는 자리는 그 프레임 그대로, 나머지는 사본(모든 레이어·스킨 셀이 원래 셀과 링크 — 그림을 같이 씀, 길이·피벗 같음, 칸 이름은 비움 — 이름 규칙).
    /// 태그·모든 레이어 효과 범위는 <see cref="FrameExtension.MapRange"/>로 바뀐다(범위를 덮으면 늘어나고 뒤쪽은 밀림). 같은 수면 아무것도 하지 않는다.
    /// </summary>
    public void ExtendFrames(FrameExtension extension)
    {
        var x = extension;
        if (x.To >= _frames.Count) throw new ArgumentOutOfRangeException(nameof(extension), DomainStrings.FormatFrames_InvalidRange(from: x.From + 1, to: x.To + 1));
        if (x.Added == 0) return;
        var sources = _frames.GetRange(x.From, x.Length);
        Edit(DomainStrings.Project_ExtendFrames, ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            var cels = new List<(CelKey Key, PixelImage Cel)>?[x.Length];
            // 아직 링크되지 않은 원래 셀은 사본에 새 링크 색을 붙여 원래 칸과 사본 칸이 같이 쓴다(행마다 이번에 준 색은 겹치지 않게)
            var given = new Dictionary<(Layer, Skin?), List<int>>();
            List<(CelKey Key, PixelImage Cel)> LinkSources(Frame src)
            {
                var list = CelsOf(src);
                for (int i = 0; i < list.Count; i++)
                {
                    var (key, cel) = list[i];
                    if (LinkedKeys(key).Count > 1) continue;
                    var row = (key.Layer, key.Skin);
                    if (!given.TryGetValue(row, out var colors)) given[row] = colors = [];
                    int color = NextLinkColor(key.Layer, key.Skin, colors);
                    colors.Add(color);
                    var shared = Tagged(cel.Clone(), color);
                    _cels[key] = shared;
                    list[i] = (key, shared);
                }
                return list;
            }
            var result = new Frame[x.Count];
            var placed = new bool[x.Length];
            for (int k = 0; k < x.Count; k++)
            {
                int s = x.SourceOf(k);
                var src = sources[s];
                if (!placed[s])
                {
                    placed[s] = true;
                    result[k] = src;
                    continue;
                }
                var copy = CopyFrame(src);
                // 사본 셀은 원래 프레임 셀과 링크(그림을 같이 씀): 원래 셀을 고치면 사본도 함께 바뀐다
                foreach (var (key, cel) in cels[s] ??= LinkSources(src)) _cels[key with { Frame = copy }] = cel;
                CopyOverflow(src, copy);
                result[k] = copy;
            }
            _frames.RemoveRange(x.From, x.Length);
            _frames.InsertRange(x.From, result);
            foreach (var t in _tags)
            {
                var r = x.MapRange(new FrameRange(t.From, t.To));
                t.From = r.From;
                t.To = r.To;
            }
            MapEffectRanges(r => x.MapRange(r));
        });
    }

    /// <summary>
    /// 프레임 삭제(마지막 하나는 지울 수 없음). 태그·효과 범위가 줄고, 비게 된 태그와 범위의 프레임이 모두 지워진 효과는 지워진다
    /// (같은 실행취소 단계라 되돌리면 함께 돌아온다).
    /// </summary>
    public void DeleteFrame(int index) => DeleteFrames(index, 1);

    /// <summary>
    /// 이어진 프레임 [<paramref name="start"/>, <paramref name="start"/> + <paramref name="count"/>)를 한 단계로 지운다(<see cref="DeleteFrame"/>와 같은 규칙 —
    /// 태그·효과 범위는 남는 프레임으로 줄고, 남는 프레임이 없으면 그 태그·효과를 지움). 모든 프레임은 지울 수 없다.
    /// </summary>
    public void DeleteFrames(int start, int count)
    {
        if (count <= 0) return;
        if (count >= _frames.Count) throw new InvalidOperationException(DomainStrings.Project_CannotDeleteLastFrame);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, _frames.Count);
        int end = start + count; // 지우는 칸 끝(제외)
        // 범위 [a, b]에서 남는 첫·끝 프레임을 지운 뒤 번호로(남는 프레임이 없으면 null)
        (int From, int To)? Remap(int a, int b)
        {
            int first = a >= start && a < end ? end : a;
            int last = b >= start && b < end ? start - 1 : b;
            if (first > b || last < a || first > last) return null;
            return (first >= end ? first - count : first, last >= end ? last - count : last);
        }
        var removed = _frames.GetRange(start, count);
        Edit(count == 1 ? DomainStrings.Project_DeleteFrame : DomainStrings.Project_DeleteFrames, ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            _frames.RemoveRange(start, count);
            foreach (var frame in removed)
            {
                foreach (var (key, _) in CelsOf(frame).ToList()) _cels.Remove(key);
                RemoveOverflowOf(frame);
            }
            foreach (var t in _tags)
            {
                if (Remap(t.From, t.To) is { } r) (t.From, t.To) = r;
                else t.To = t.From - 1;
            }
            _tags.RemoveAll(t => t.To < t.From);
            MapEffectRanges(r => Remap(r.From, r.To) is { } m ? new FrameRange(m.From, m.To) : null);
        });
    }

    /// <summary>
    /// 프레임 순서 바꾸기(<paramref name="to"/> = 옮긴 뒤 위치). 태그·효과 범위는 프레임 번호 기준이라 그대로다
    /// (태그 안의 순서를 바꾸거나 다른 태그 칸으로 옮기는 동작, 효과는 그 자리에 온 프레임에 입혀짐).
    /// </summary>
    public void MoveFrame(int from, int to)
    {
        var frame = _frames[from];
        to = Math.Clamp(to, 0, _frames.Count - 1);
        if (from == to) return;
        Edit(DomainStrings.Project_MoveFrame, ProjectChange.Frames, () =>
        {
            _frames.RemoveAt(from);
            _frames.Insert(to, frame);
        });
    }

    /// <summary>
    /// 모든 레이어 효과의 프레임 범위를 <paramref name="map"/>으로 고친다(모든 프레임 효과는 그대로, <paramref name="map"/>이 null이면 그 효과를 지움).
    /// 바뀐 레이어만 새 목록으로 바꾼다(편집 안에서 불러 실행취소 기록에 함께 든다).
    /// </summary>
    private void MapEffectRanges(Func<FrameRange, FrameRange?> map)
    {
        foreach (var layer in _layers)
        {
            List<LayerEffect>? list = null;
            for (int i = 0; i < layer.Effects.Count; i++)
            {
                var e = layer.Effects[i];
                var next = e.Frames is { } r ? map(r) : null;
                bool same = e.Frames is null || next == e.Frames;
                if (same && list is null) continue;
                list ??= [.. layer.Effects.Take(i)];
                if (same) list.Add(e);
                else if (next is not null) list.Add(e with { Frames = next });
            }
            if (list is not null) layer.Effects = list.ToArray();
        }
    }

    public void SetFrameDuration(int index, int durationMs)
    {
        var frame = _frames[index];
        Edit(DomainStrings.Project_FrameDuration, ProjectChange.Frames, () => frame.DurationMs = durationMs);
    }

    /// <summary>내보낼 칸 이름 재정의(null/빈 문자열 = 이름 규칙 사용).</summary>
    public void SetFrameName(int index, string? name)
    {
        var frame = _frames[index];
        Edit(DomainStrings.Project_FrameName, ProjectChange.Frames, () => frame.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim());
    }

    // ───────────── 태그 ─────────────

    /// <summary>태그 추가. 이름이 비었거나 이미 있으면 <see cref="ArgumentException"/>(<see cref="UniqueTagName"/> 사용).</summary>
    public AnimationTag AddTag(string name, int from, int to, TagDirection direction = TagDirection.Forward, bool loop = true)
    {
        name = name.Trim();
        ValidateTag(name, from, to, null);
        var tag = new AnimationTag(name, from, to)
        {
            Direction = direction,
            Loop = loop,
            Color = AnimationTag.DefaultColors[_tags.Count % AnimationTag.DefaultColors.Count],
        };
        Edit(DomainStrings.Project_AddTag, ProjectChange.Tags, () => _tags.Add(tag));
        return tag;
    }

    /// <summary>태그 속성 바꾸기(null인 값은 그대로). 잘못된 값이면 <see cref="ArgumentException"/>.</summary>
    public void SetTag(int index, string? name = null, int? from = null, int? to = null,
        TagDirection? direction = null, bool? loop = null, Color32? color = null)
    {
        var tag = _tags[index];
        var newName = string.IsNullOrWhiteSpace(name) ? tag.Name : name.Trim();
        int newFrom = from ?? tag.From, newTo = to ?? tag.To;
        ValidateTag(newName, newFrom, newTo, tag);
        Edit(DomainStrings.Project_EditTag, ProjectChange.Tags, () =>
        {
            tag.Name = newName;
            tag.From = newFrom;
            tag.To = newTo;
            if (direction is { } d) tag.Direction = d;
            if (loop is { } l) tag.Loop = l;
            if (color is { } c) tag.Color = c;
        });
    }

    public void RemoveTag(int index)
    {
        var tag = _tags[index];
        Edit(DomainStrings.Project_DeleteTag, ProjectChange.Tags, () => _tags.Remove(tag));
    }

    private void ValidateTag(string name, int from, int to, AnimationTag? self)
    {
        if (name.Length == 0) throw new ArgumentException(DomainStrings.Project_EmptyTagName, nameof(name));
        if (_tags.Any(t => !ReferenceEquals(t, self) && t.Name == name))
            throw new ArgumentException(DomainStrings.FormatProject_DuplicateTagName(name: name), nameof(name));
        if (from < 0 || to >= _frames.Count || from > to)
            throw new ArgumentOutOfRangeException(nameof(from), DomainStrings.FormatProject_InvalidTagRange(from: from, to: to));
    }

    // ───────────── 캔버스 ─────────────

    /// <summary>
    /// 캔버스 크기 바꾸기: 일반 레이어 셀을 <paramref name="anchor"/> 기준으로 옮기고, 부위는 위치만 같은 만큼 옮기며(부위 셀은 그대로),
    /// 피벗이 같은 그림 지점을 가리키도록 보정한다(<see cref="ResizeCanvasAndScale"/>의 배율 1).
    /// </summary>
    public void ResizeCanvas(int width, int height, Anchor anchor)
    {
        var (ox, oy) = PixelImage.AnchorOffset(_width, _height, width, height, anchor);
        ResizeCanvasAndScale(width, height, new Rectangle(ox, oy, _width, _height));
    }

    /// <summary>
    /// 캔버스 크기 바꾸기 + 그림 크기 바꾸기(실행취소 한 단계): 새 캔버스 <paramref name="width"/>×<paramref name="height"/>에서 지금 캔버스 전체 그림이
    /// <paramref name="image"/>(새 캔버스 좌표) 자리에 그 크기로 놓인다. 일반 레이어 셀은 최근접으로 늘이거나 줄여 놓고(캔버스 밖은 잘림),
    /// 부위는 위치·크기를 같은 비율로(가장자리마다 반올림, 최소 1) 바꿔 옮기며 모든 스킨의 셀을 새 부위 크기로 최근접, 9-slice 테두리도 비율대로 바꾼다.
    /// 기본·프레임 피벗은 같은 그림 지점을 가리키도록 옮긴다(부위 피벗은 부위 기준이라 그대로). 그림 크기가 같으면 부위는 위치만 옮긴다.
    /// <para>
    /// <paramref name="slice"/>가 있으면 9-slice로 늘인다(Unity Sliced·Tiled처럼): 프레임 칸 테두리(<see cref="SpriteSettings.Border"/>)를 쓸 수 있으면
    /// 일반 레이어 셀을 그 테두리로 늘이고 부위 가장자리·피벗도 같은 대응으로 옮기며, 테두리는 그림 가장자리에서 같은 픽셀 수로 남긴다(여백만큼 더함).
    /// 자기 테두리(<see cref="Layer.PartBorder"/>)가 있는 부위는 그 테두리로 늘이고 테두리 값을 그대로 둔다(새 크기에 다 안 들어가면 비율대로).
    /// </para>
    /// <para>
    /// <paramref name="keep"/>(새 캔버스 좌표)가 있으면 바꾼 뒤 그 모양 밖 픽셀을 모두 지운다 — 일반 레이어의 캔버스 밖 그림과 부위(프레임마다 그 위치에서) 포함.
    /// 캔버스 크기 모드에서 테두리 꼭짓점을 마음대로 옮긴 여백(사다리꼴 등)으로 자를 때 쓴다.
    /// </para>
    /// </summary>
    public void ResizeCanvasAndScale(int width, int height, Rectangle image, NineSliceFill? slice = null, PixelMask? keep = null)
    {
        ValidateSize(width, height);
        if (image.Width < 1 || image.Height < 1 || image.Width > PixelImage.MaxSize || image.Height > PixelImage.MaxSize)
            throw new ArgumentOutOfRangeException(nameof(image), DomainStrings.FormatProject_InvalidImageSize(max: PixelImage.MaxSize, width: image.Width, height: image.Height));
        int oldW = _width, oldH = _height;
        if (width == oldW && height == oldH && image == new Rectangle(0, 0, oldW, oldH) && keep is null) return;
        var fill = slice ?? NineSliceFill.Stretch;
        var border = slice is not null && NineSlice.Applies(Sprite.Border, oldW, oldH) ? Sprite.Border : SpriteBorder.None;
        bool sliced = !border.IsNone;
        // 원래 캔버스 좌표(연속) → 새 캔버스 좌표(MapResizedCanvasPoint와 같은 대응).
        double MapXF(double x) => MapResizedAxis(x, oldW, border.Left, border.Right, image.X, image.Width, sliced);
        double MapYF(double y) => MapResizedAxis(y, oldH, border.Top, border.Bottom, image.Y, image.Height, sliced);
        Pivot Map(Pivot p)
        {
            var (px, py) = p.ToLocalPixel(oldW, oldH);
            return new Pivot((float)(MapXF(px) / width), 1f - (float)(MapYF(py) / height));
        }
        // 원래 캔버스 좌표 → 새 캔버스 좌표(반올림). 부위 가장자리를 따로 옮겨 맞닿은 부위는 계속 맞닿는다.
        int MapX(int x) => (int)Math.Floor(MapXF(x) + 0.5);
        int MapY(int y) => (int)Math.Floor(MapYF(y) + 0.5);
        var parts = _layers.Where(l => l.IsPart).Select(part =>
        {
            var r = part.PartRect;
            int left = MapX(r.Left), top = MapY(r.Top);
            int w = Math.Clamp(MapX(r.Right) - left, 1, PixelImage.MaxSize), h = Math.Clamp(MapY(r.Bottom) - top, 1, PixelImage.MaxSize);
            return (Part: part, Rect: new Rectangle(left, top, w, h));
        }).ToList();
        Edit(DomainStrings.Project_CanvasSize, ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Settings | ProjectChange.Layers, () =>
        {
            var oldCanvas = new Size(oldW, oldH);
            var newCanvas = new Size(width, height);
            // 링크된 셀(같은 그림·같은 캔버스 밖 그림)은 한 번만 계산해 같은 새 그림을 같이 쓴다
            var done = new Dictionary<PixelImage, (PixelImage Cel, CelOverflow? Overflow, bool Split)>(ReferenceEqualityComparer.Instance);
            foreach (var key in _cels.Keys.Where(k => !k.Layer.IsPart).Concat(_overflow.Keys).Distinct().ToList())
            {
                var cel = _cels.GetValueOrDefault(key);
                if (cel is null || !done.TryGetValue(cel, out var result))
                {
                    // 그림이 새 캔버스 안에 다 들어가고 캔버스 밖 그림이 없으면 예전처럼(빠름), 아니면 밖으로 나가는 픽셀을 캔버스 밖 그림으로 남긴다.
                    if (cel is not null && !_overflow.ContainsKey(key) && new Rectangle(0, 0, width, height).Contains(image))
                        result = (ImageTransform.ResizeCanvasScaled(cel, width, height, image, border, fill), null, false);
                    else
                    {
                        var (newCel, overflow) = ResizeWithOverflow(key, cel, image, oldCanvas, newCanvas, border, fill);
                        result = (newCel, overflow, true);
                    }
                    if (cel is not null)
                    {
                        done[cel] = result;
                        InheritLinkTag(cel, result.Cel);
                    }
                }
                _cels[key] = result.Cel;
                if (result.Split) SetOverflow(key, result.Overflow);
            }
            foreach (var (part, rect) in parts)
            {
                var old = part.PartSize;
                var from = part.PartOffset;
                // 프레임마다 옮긴 거리도 같은 대응으로(옮긴 왼쪽 위를 옮겨 본 자리 − 새 기준 위치)
                MapPartShifts(part, s => new Point(MapX(from.X + s.X) - rect.X, MapY(from.Y + s.Y) - rect.Y));
                part.PartOffset = rect.Location;
                if (rect.Size == old) continue;
                bool partSliced = slice is not null && NineSlice.Applies(part.PartBorder, old.Width, old.Height);
                var scale = OncePerImage(cel => partSliced ? NineSlice.Scale(cel, rect.Width, rect.Height, part.PartBorder, fill) : ImageTransform.ScaleNearest(cel, rect.Width, rect.Height));
                foreach (var (key, cel) in CelsOf(part)) _cels[key] = scale(cel);
                part.PartSize = rect.Size;
                if (!partSliced || !NineSlice.Applies(part.PartBorder, rect.Width, rect.Height))
                    part.PartBorder = ScaleBorder(part.PartBorder, old, rect.Size);
            }
            _width = width;
            _height = height;
            MapCanvasPivots(Map);
            ShiftTilemaps(image.X, image.Y);
            // 프레임 칸 테두리: 그림 가장자리에서 같은 픽셀 수(그림 바깥 여백만큼 더하고, 캔버스 밖으로 잘린 만큼 뺌).
            if (sliced)
                Sprite.Border = new SpriteBorder(
                    Math.Max(0, border.Left + image.X), Math.Max(0, border.Top + image.Y),
                    Math.Max(0, border.Right + width - image.Right), Math.Max(0, border.Bottom + height - image.Bottom));
            if (keep is { } k) ClearOutside(k);
        });
    }

    /// <summary>
    /// 모양 <paramref name="keep"/>(캔버스 좌표) 밖 픽셀을 모든 셀에서 지운다(<see cref="Edit(string, ProjectChange, Action)"/> 안에서): 일반 레이어는 캔버스 밖 그림까지,
    /// 부위는 프레임마다 그 부위 위치에서 본 자리로. 링크된 셀은 같은 결과면 한 그림을 같이 쓴다.
    /// </summary>
    private void ClearOutside(PixelMask keep)
    {
        var size = Size;
        var done = new HashSet<PixelImage>(ReferenceEqualityComparer.Instance);
        foreach (var key in _cels.Keys.Where(k => !k.Layer.IsPart).Concat(_overflow.Keys).Distinct().ToList())
        {
            var cel = _cels.GetValueOrDefault(key);
            if (cel is not null && !done.Add(cel)) continue;
            var (full, origin) = FullOf(key, cel, size);
            full = full.Clone();
            bool changed = false;
            for (int y = 0; y < full.Height; y++)
                for (int x = 0; x < full.Width; x++)
                {
                    int i = y * full.Width + x;
                    if (full.Pixels[i].A == 0 || keep.Contains(origin.X + x, origin.Y + y)) continue;
                    full.Pixels[i] = Color32.Transparent;
                    changed = true;
                }
            if (!changed) continue;
            var (next, overflow) = Split(full, origin, size);
            if (HasContent(next)) SetLinked(key, next, overflow);
            else
                foreach (var k in LinkedKeys(key))
                {
                    _cels.Remove(k);
                    SetOverflow(k, overflow);
                }
        }
        // 부위: 같은 그림이 같은 위치에 놓인 셀끼리만 결과를 같이 쓴다(위치가 다르면 잘리는 자리도 달라 링크가 갈라짐).
        var parts = new Dictionary<(PixelImage Cel, Point At), PixelImage>();
        foreach (var (key, cel) in _cels.Where(c => c.Key.Layer.IsPart).ToList())
        {
            var at = GetPartOffset(key.Layer, _frames.IndexOf(key.Frame));
            if (!parts.TryGetValue((cel, at), out var next))
            {
                next = cel;
                for (int y = 0; y < cel.Height; y++)
                    for (int x = 0; x < cel.Width; x++)
                    {
                        if (cel.Pixels[y * cel.Width + x].A == 0 || keep.Contains(at.X + x, at.Y + y)) continue;
                        if (ReferenceEquals(next, cel)) next = cel.Clone();
                        next.Pixels[y * cel.Width + x] = Color32.Transparent;
                    }
                InheritLinkTag(cel, next);
                parts[(cel, at)] = next;
            }
            _cels[key] = next;
        }
    }

    /// <summary>9-slice 테두리를 부위 크기 비율대로(반올림) 바꾼다. 마주 보는 두 변의 합은 새 크기를 넘지 않는다.</summary>
    private static SpriteBorder ScaleBorder(SpriteBorder b, Size from, Size to)
    {
        if (b.IsNone) return b;
        static int Scale(int v, int from, int to) => (int)Math.Floor((double)v * to / from + 0.5);
        int left = Math.Min(Scale(b.Left, from.Width, to.Width), to.Width);
        int right = Math.Min(Scale(b.Right, from.Width, to.Width), to.Width - left);
        int top = Math.Min(Scale(b.Top, from.Height, to.Height), to.Height);
        int bottom = Math.Min(Scale(b.Bottom, from.Height, to.Height), to.Height - top);
        return new SpriteBorder(left, top, right, bottom);
    }

    /// <summary>모든 셀 좌우 뒤집기(피벗·부위 위치·테두리도 뒤집음).</summary>
    public void FlipCanvasHorizontal()
    {
        int w = _width;
        TransformCanvas(DomainStrings.Project_FlipHorizontal, ImageTransform.FlipHorizontal, p => new Pivot(1f - p.X, p.Y),
            r => new Rectangle(w - r.Right, r.Y, r.Width, r.Height),
            b => new SpriteBorder(b.Right, b.Top, b.Left, b.Bottom));
    }

    /// <summary>모든 셀 상하 뒤집기(피벗·부위 위치·테두리도 뒤집음).</summary>
    public void FlipCanvasVertical()
    {
        int h = _height;
        TransformCanvas(DomainStrings.Project_FlipVertical, ImageTransform.FlipVertical, p => new Pivot(p.X, 1f - p.Y),
            r => new Rectangle(r.X, h - r.Bottom, r.Width, r.Height),
            b => new SpriteBorder(b.Left, b.Bottom, b.Right, b.Top));
    }

    /// <summary>모든 셀 90° 회전(가로·세로가 바뀌고 피벗·부위 위치·크기·테두리도 회전).</summary>
    public void RotateCanvas90(bool clockwise = true)
    {
        int w = _width, h = _height;
        if (clockwise)
            TransformCanvas(DomainStrings.Project_RotateClockwise, img => ImageTransform.Rotate90(img, true), p => new Pivot(p.Y, 1f - p.X),
                r => new Rectangle(h - r.Bottom, r.X, r.Height, r.Width),
                b => new SpriteBorder(b.Bottom, b.Left, b.Top, b.Right));
        else
            TransformCanvas(DomainStrings.Project_RotateCounterclockwise, img => ImageTransform.Rotate90(img, false), p => new Pivot(1f - p.Y, p.X),
                r => new Rectangle(r.Y, w - r.Right, r.Height, r.Width),
                b => new SpriteBorder(b.Top, b.Right, b.Bottom, b.Left));
    }

    /// <param name="transform">셀 이미지 변환(일반·부위 셀 모두).</param>
    /// <param name="pivotMap">0~1 피벗 변환(기본·프레임·부위 피벗).</param>
    /// <param name="partRectMap">변환 전 캔버스 안 부위 영역 → 변환 뒤 영역.</param>
    /// <param name="borderMap">부위 9-slice 테두리 변환.</param>
    private void TransformCanvas(string label, Func<PixelImage, PixelImage> transform, Func<Pivot, Pivot> pivotMap,
        Func<Rectangle, Rectangle> partRectMap, Func<SpriteBorder, SpriteBorder> borderMap)
    {
        var probe = transform(new PixelImage(_width, _height));
        Edit(label, ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Settings | ProjectChange.Layers, () =>
        {
            var map = OncePerImage(transform);
            foreach (var key in _cels.Keys.ToList()) _cels[key] = map(_cels[key]);
            foreach (var (key, o) in _overflow.ToList())
                _overflow[key] = new CelOverflow(transform(o.Image), partRectMap(o.Rect).Location);
            foreach (var part in _layers.Where(l => l.IsPart))
            {
                var before = part.PartRect;
                var r = partRectMap(before);
                MapPartShifts(part, s =>
                {
                    var moved = partRectMap(before with { X = before.X + s.X, Y = before.Y + s.Y }).Location;
                    return new Point(moved.X - r.X, moved.Y - r.Y);
                });
                part.PartOffset = r.Location;
                part.PartSize = r.Size;
                MapLayerPivots(part, pivotMap);
                part.PartBorder = borderMap(part.PartBorder);
            }
            _width = probe.Width;
            _height = probe.Height;
            MapCanvasPivots(pivotMap);
        });
    }

    // ───────────── 이름 ─────────────

    public string UniqueTagName(string baseName)
    {
        baseName = baseName.Trim();
        if (baseName.Length == 0) baseName = "tag";
        if (!HasTag(baseName)) return baseName;
        for (int i = 1; ; i++)
            if (!HasTag($"{baseName}_{i}")) return $"{baseName}_{i}";
    }

    public string UniqueLayerName(string baseName)
    {
        if (_layers.All(l => l.Name != baseName)) return baseName;
        for (int i = 2; ; i++)
            if (_layers.All(l => l.Name != $"{baseName} {i}")) return $"{baseName} {i}";
    }

    /// <summary>
    /// 복제 이름: 이름 끝이 숫자면 그 숫자 + 1(자릿수 그대로 — <c>item_009</c> → <c>item_010</c>, <c>a-99</c> → <c>a-100</c>),
    /// 숫자가 없으면 <c>_1</c>을 붙인다. <paramref name="taken"/>인 이름이면 쓰이지 않을 때까지 더 올린다.
    /// </summary>
    public static string NextNumberedName(string name, Func<string, bool> taken)
    {
        int end = name.Length, start = end;
        while (start > 0 && char.IsAsciiDigit(name[start - 1])) start--;
        string prefix, format;
        System.Numerics.BigInteger n;
        if (start == end)
        {
            prefix = name + "_";
            format = "";
            n = 0;
        }
        else
        {
            prefix = name[..start];
            format = new string('0', end - start);
            n = System.Numerics.BigInteger.Parse(name[start..], System.Globalization.CultureInfo.InvariantCulture);
        }
        while (true)
        {
            n++;
            var candidate = prefix + n.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            if (!taken(candidate)) return candidate;
        }
    }

    /// <summary>"레이어 N" 중 쓰이지 않은 다음 이름.</summary>
    public string NextLayerName()
    {
        for (int i = _layers.Count + 1; ; i++)
        {
            var candidate = DomainStrings.FormatProject_DefaultLayerName(number: i);
            if (_layers.All(l => l.Name != candidate)) return candidate;
        }
    }

    // ───────────── 파일 읽기·가져오기용 내부 구성 ─────────────

    internal void AddLayerRaw(Layer layer) => _layers.Add(layer);
    internal void ReplaceLayersRaw(IEnumerable<Layer> layers)
    {
        var list = layers.ToList();
        _layers.Clear();
        _layers.AddRange(list);
    }
    internal void AddFrameRaw(Frame frame) => _frames.Add(frame);
    internal void AddTagRaw(AnimationTag tag) => _tags.Add(tag);
    internal void AddSkinPresetRaw(SkinPreset preset) => _presets.Add(preset);
    /// <summary>파일 읽기·가져오기용: 셀 하나를 넣는다. <paramref name="skin"/>이 null이거나 첫 스킨이면 첫 스킨 셀.</summary>
    internal void SetCelRaw(Layer layer, Frame frame, PixelImage image, Skin? skin = null) => _cels[new(layer, frame, SkinKey(layer, skin))] = image;

    /// <summary>파일 쓰기용: 스킨 자신의 셀(<paramref name="skin"/> null = 첫 스킨).</summary>
    internal PixelImage? GetCelRaw(Layer layer, Frame frame, Skin? skin) => _cels.GetValueOrDefault(new(layer, frame, SkinKey(layer, skin)));

    private static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new ArgumentOutOfRangeException(nameof(width), DomainStrings.FormatCanvas_InvalidSize(max: PixelImage.MaxSize, width: width, height: height));
    }

    private static void ValidatePartSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new ArgumentException(DomainStrings.FormatProject_InvalidPartSize(max: PixelImage.MaxSize, width: width, height: height), nameof(width));
    }

    // ───────────── 실행취소 기록 ─────────────

    /// <summary>열린 스트로크.</summary>
    private sealed record StrokeState(CelKey Key, PixelImage Image, PixelImage Before, bool Created);

    private sealed class CelStrokeCommand(SpriteProject project, string label, StrokeState stroke, PixelPatch patch) : IUndoCommand
    {
        public string Label { get; } = label;
        public StrokeState Stroke => stroke;
        public PixelPatch Patch => patch;

        public void Undo()
        {
            patch.ApplyBefore(stroke.Image);
            if (stroke.Created) project._cels.Remove(stroke.Key);
            project.NotifyChanged(ProjectChange.Pixels);
        }

        public void Redo()
        {
            project._cels[stroke.Key] = stroke.Image;
            patch.ApplyAfter(stroke.Image);
            project.NotifyChanged(ProjectChange.Pixels);
        }
    }

    private sealed class SnapshotCommand(SpriteProject project, string label, Snapshot before, Snapshot after, ProjectChange change)
        : IUndoCommand
    {
        public string Label { get; } = label;
        public Snapshot Before => before;
        public Snapshot After => after;
        public ProjectChange Change => change;

        public void Undo()
        {
            before.Restore(project);
            project.NotifyChanged(change | before.SizeChange(after));
        }

        public void Redo()
        {
            after.Restore(project);
            project.NotifyChanged(change | before.SizeChange(after));
        }
    }

    private readonly record struct LayerState(
        Layer Layer, string Name, bool Visible, bool Locked, byte Opacity, LayerKind Kind, Layer? Parent,
        Size PartSize, Point PartOffset, Pivot Pivot, SpriteBorder PartBorder, Rectangle? PartSourceRect,
        (Skin Skin, string Name)[]? Skins, Skin? ActiveSkin, IReadOnlyList<LayerEffect> Effects, GroupLayout? AutoLayout,
        TilemapData? Tiles)
    {
        public static LayerState Of(Layer l) => new(l, l.Name, l.Visible, l.Locked, l.Opacity, l.Kind, l.Parent,
            l.PartSize, l.PartOffset, l.Pivot, l.PartBorder, l.PartSourceRect,
            l.IsTilemap ? null : l.SkinList.Select(s => (s, s.Name)).ToArray(), l.ActiveSkin, l.Effects, l.AutoLayout, l.Tiles);

        /// <summary>
        /// 상태를 되돌린다. 지금 스킨(착장)은 실행취소 대상이 아니므로 스킨 목록 구성이 같으면 지금 고른 스킨을 그대로 두고,
        /// 스킨이 생기거나 없어지는 단계를 되돌릴 때만 기록된 스킨을 고른다.
        /// </summary>
        public Layer Apply()
        {
            Layer.Name = Name;
            Layer.Visible = Visible;
            Layer.Locked = Locked;
            Layer.Opacity = Opacity;
            Layer.Kind = Kind;
            Layer.Parent = Parent;
            Layer.PartSize = PartSize;
            Layer.PartOffset = PartOffset;
            Layer.Pivot = Pivot;
            Layer.PartBorder = PartBorder;
            Layer.PartSourceRect = PartSourceRect;
            Layer.AutoLayout = AutoLayout;
            Layer.Effects = Effects;
            Layer.Tiles = Tiles;
            if (Skins is not null)
            {
                var current = Layer.ActiveSkinRaw;
                var restored = Skins.Select(s => s.Skin).ToList();
                bool sameSet = Layer.SkinList.Count == restored.Count && restored.All(Layer.SkinList.Contains);
                foreach (var (skin, name) in Skins) skin.Name = name;
                Layer.ReplaceSkinsRaw(restored);
                Layer.SetActiveSkinRaw(sameSet && current is not null && restored.Contains(current) ? current : ActiveSkin);
            }
            return Layer;
        }
    }

    /// <summary>구조 상태 사본(접힘·지금 스킨 선택은 보기 상태라 되돌리지 않음 — <see cref="LayerState.Apply"/>). 셀 이미지는 참조만 담는다(구조 편집은 셀을 새 이미지로 바꿔 넣으므로 안전).</summary>
    private sealed partial class Snapshot
    {
        private int _w, _h;
        private LayerState[] _layers = [];
        private (Frame Frame, int Ms, Pivot? Pivot, string? Name, KeyValuePair<Layer, Pivot>[] LayerPivots, KeyValuePair<Layer, Point>[] PartShifts)[] _frames = [];
        /// <summary>캔버스 피벗의 대표 레이어(<see cref="SpriteProject.PivotLayer"/>).</summary>
        private Layer? _pivotLayer;
        private (AnimationTag Tag, AnimationTag State)[] _tags = [];
        private (SkinPreset Preset, string Name, IReadOnlyList<SkinPresetEntry> Entries)[] _presets = [];
        private KeyValuePair<CelKey, PixelImage>[] _cels = [];
        private KeyValuePair<CelKey, CelOverflow>[] _overflow = [];
        private SpriteSettings _sprite = new();
        private ExportSettings _export = new();
        private KeyValuePair<string, Tileset>[] _tilesets = [];

        public static Snapshot Capture(SpriteProject p) => new()
        {
            _w = p._width,
            _h = p._height,
            _layers = p._layers.Select(LayerState.Of).ToArray(),
            // 지운 레이어의 프레임 피벗·위치는 빼고(쓰이지 않음 — 편집 기록 파일에 없는 레이어가 남지 않게)
            _frames = p._frames.Select(f => (f, f.DurationMs, f.Pivot, f.Name, f.LayerPivots.Where(kv => p._layers.Contains(kv.Key)).ToArray(),
                f.PartShifts.Where(kv => p._layers.Contains(kv.Key)).ToArray())).ToArray(),
            _pivotLayer = p.PivotLayer,
            _tags = p._tags.Select(t => (t, t.Clone())).ToArray(),
            _presets = p._presets.Select(o => (o, o.Name, o.Entries)).ToArray(),
            _cels = p._cels.ToArray(),
            _overflow = p._overflow.ToArray(),
            _sprite = p.Sprite.Clone(),
            _export = p.Export.Clone(),
            _tilesets = p._tilesets.ToArray(),
        };

        public void Restore(SpriteProject p)
        {
            p._width = _w;
            p._height = _h;
            p._layers.Clear();
            foreach (var state in _layers) p._layers.Add(state.Apply());
            p._frames.Clear();
            foreach (var (frame, ms, pivot, name, layerPivots, partShifts) in _frames)
            {
                frame.DurationMs = ms;
                frame.Pivot = pivot;
                frame.Name = name;
                frame.LayerPivots.Clear();
                foreach (var (layer, layerPivot) in layerPivots) frame.LayerPivots[layer] = layerPivot;
                frame.PartShifts.Clear();
                foreach (var (part, shift) in partShifts) frame.PartShifts[part] = shift;
                p._frames.Add(frame);
            }
            p.PivotLayer = _pivotLayer;
            p._tags.Clear();
            foreach (var (tag, state) in _tags)
            {
                tag.Name = state.Name;
                tag.From = state.From;
                tag.To = state.To;
                tag.Direction = state.Direction;
                tag.Loop = state.Loop;
                tag.Color = state.Color;
                p._tags.Add(tag);
            }
            p._presets.Clear();
            foreach (var (preset, name, entries) in _presets)
            {
                preset.Name = name;
                preset.Entries = entries;
                p._presets.Add(preset);
            }
            p._cels.Clear();
            foreach (var kv in _cels) p._cels[kv.Key] = kv.Value;
            p._overflow.Clear();
            foreach (var kv in _overflow) p._overflow[kv.Key] = kv.Value;
            p.Sprite.CopyFrom(_sprite);
            p.Export.CopyFrom(_export);
            p._tilesets.Clear();
            foreach (var kv in _tilesets) p._tilesets[kv.Key] = kv.Value;
        }

        public ProjectChange SizeChange(Snapshot other)
            => _w != other._w || _h != other._h ? ProjectChange.Canvas : ProjectChange.None;
    }
}
