using System.Drawing;
using Gpixel.Core.Edit;

namespace Gpixel.Core.Model;

/// <summary>
/// Gpixel 프로젝트(<c>.gpixel</c>): 공통 캔버스 크기 × 레이어 × 프레임의 셀(cel) + 태그(애니메이션) +
/// Unity·내보내기 설정 + 실행취소 기록.
/// <para>
/// 레이어는 아래 → 위 평평한 목록이다. 그룹(합친 레이어)의 부위 레이어들은 그룹 바로 아래에 이어 붙어 있고(<c>g-n … g-1</c>),
/// 그룹·일반 레이어만 최상위이며 그룹은 겹치지 않는다. 부위는 자기 크기의 셀을 프레임마다 갖고, 모든 프레임에 공통인 위치에 합성된다.
/// </para>
/// <para>
/// 모든 구조 변경 메서드(레이어·프레임·태그·캔버스·설정)는 <see cref="History"/>에 기록되고
/// <see cref="Changed"/>를 발생시키며 수정됨 표시를 켠다. 그리기는 <see cref="BeginCelStroke"/> →
/// 픽셀 수정(필요하면 <see cref="NotifyPixelsChanged"/>로 다시 그리기 알림) → <see cref="CommitCelStroke"/>로
/// 스트로크 하나를 기록한다. 레이어·프레임·태그 객체의 속성을 직접 바꾸지 말고 Set… 메서드나
/// <see cref="Edit(string, ProjectChange, Action)"/> 안에서 바꿔야 실행취소된다.
/// </para>
/// </summary>
public sealed class SpriteProject
{
    public const string UntitledName = "새 프로젝트";
    public const string FileExtension = ".gpixel";

    private readonly List<Layer> _layers = new();
    private readonly List<Frame> _frames = new();
    private readonly List<AnimationTag> _tags = new();
    /// <summary>셀: (레이어, 프레임) → 이미지. 일반 레이어는 캔버스 크기, 부위는 부위 크기, 그룹은 셀이 없다.</summary>
    private readonly Dictionary<(Layer, Frame), PixelImage> _cels = new();
    private int _width, _height;
    private string? _path;
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
    public static SpriteProject FromImage(PixelImage image, string layerName = "레이어 1")
    {
        var p = CreateBlank(image.Width, image.Height);
        var layer = new Layer(layerName);
        var frame = new Frame();
        p._layers.Add(layer);
        p._frames.Add(frame);
        if (!image.IsRegionEmpty(image.Bounds)) p._cels[(layer, frame)] = image.Clone();
        return p;
    }

    // ───────────── 상태 ─────────────

    public int Width => _width;
    public int Height => _height;
    public Size Size => new(_width, _height);
    public Rectangle Bounds => new(0, 0, _width, _height);

    /// <summary>레이어(0 = 맨 아래). 그룹의 부위는 그룹 바로 아래에 이어 붙어 있다.</summary>
    public IReadOnlyList<Layer> Layers => _layers;
    public IReadOnlyList<Frame> Frames => _frames;
    public IReadOnlyList<AnimationTag> Tags => _tags;

    /// <summary>Unity 설정(모드·PPU·기본 피벗·Single 테두리·메시·Read/Write). 바꿀 때는 <see cref="Edit(string, ProjectChange, Action)"/> 안에서.</summary>
    public SpriteSettings Sprite { get; } = new() { Mode = SpriteMode.Multiple };

    /// <summary>내보내기 설정. 바꿀 때는 <see cref="Edit(string, ProjectChange, Action)"/> 안에서.</summary>
    public ExportSettings Export { get; } = new();

    /// <summary>실행취소 기록. 스트로크 중일 수 있으므로 실행취소는 <see cref="Undo"/>/<see cref="Redo"/>를 쓴다.</summary>
    public UndoStack History { get; } = new();

    /// <summary><c>.gpixel</c> 파일 경로(저장 전이면 null).</summary>
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

    public string DisplayName => _path is null ? UntitledName : System.IO.Path.GetFileName(_path);

    /// <summary>확장자 뺀 파일 이름(칸 이름 규칙 <c>{파일}</c>). 저장 전이면 "sprite".</summary>
    public string BaseName => _path is null ? "sprite" : System.IO.Path.GetFileNameWithoutExtension(_path);

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>내용이 바뀔 때마다 발생.</summary>
    public event EventHandler<ProjectChangedEventArgs>? Changed;

    /// <summary><see cref="IsDirty"/>가 바뀔 때 발생.</summary>
    public event EventHandler? DirtyChanged;

    public void NotifyChanged(ProjectChange change, bool markDirty = true)
    {
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

    /// <summary>프레임의 실제 피벗(재정의가 없으면 기본 피벗).</summary>
    public Pivot GetFramePivot(int frameIndex) => _frames[frameIndex].Pivot ?? Sprite.DefaultPivot;

    // ───────────── 셀 ─────────────

    /// <summary>
    /// 셀 이미지(빈 셀이면 null). 부위 셀은 부위 크기, 그룹은 항상 null. 반환값을 직접 고치지 말고 스트로크로 고친다.
    /// </summary>
    public PixelImage? GetCel(int layerIndex, int frameIndex)
        => _cels.GetValueOrDefault((_layers[layerIndex], _frames[frameIndex]));

    public PixelImage? GetCel(Layer layer, Frame frame) => _cels.GetValueOrDefault((layer, frame));

    /// <summary>셀이 없거나 모두 투명이면 true.</summary>
    public bool IsCelEmpty(int layerIndex, int frameIndex)
        => GetCel(layerIndex, frameIndex) is not { } c || c.IsRegionEmpty(c.Bounds);

    /// <summary>
    /// 셀을 통째로 바꾼다(null = 비우기, 실행취소됨). 이미지 크기는 캔버스(부위면 부위) 크기와 같아야 한다.
    /// 그룹이면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void SetCel(int layerIndex, int frameIndex, PixelImage? image, string label = "셀 바꾸기")
    {
        var layer = _layers[layerIndex];
        if (layer.IsGroup) throw new InvalidOperationException(GroupHasNoCelsMessage);
        var key = (layer, _frames[frameIndex]);
        var size = CelSize(layer);
        if (image is not null && image.Size != size)
            throw new ArgumentException(layer.IsPart ? "셀 크기는 부위 크기와 같아야 합니다." : "셀 크기는 캔버스 크기와 같아야 합니다.", nameof(image));
        Edit(label, ProjectChange.Pixels, () =>
        {
            if (image is null) _cels.Remove(key);
            else _cels[key] = image;
        });
    }

    /// <summary>레이어 셀의 크기: 부위면 부위 크기, 아니면 캔버스 크기.</summary>
    public Size CelSize(Layer layer) => layer.IsPart ? layer.PartSize : Size;

    /// <summary>
    /// 프레임의 보이는 레이어를 아래부터 불투명도를 곱해 합성한 새 이미지.
    /// </summary>
    public PixelImage Flatten(int frameIndex, bool includeHidden = false)
    {
        var result = new PixelImage(_width, _height);
        FlattenInto(result, frameIndex, includeHidden);
        return result;
    }

    /// <summary>
    /// <paramref name="target"/>(캔버스 크기)을 지우고 프레임을 합성해 넣는다(메모리 재사용).
    /// 일반 레이어는 (0,0)에, 부위는 <see cref="Layer.PartOffset"/>에 불투명도 그룹×부위로 그린다. 숨긴 그룹은 부위도 숨긴다.
    /// <paramref name="layerFilter"/>가 있으면 그리는 레이어(일반·부위)마다 걸러낸다(보이기 여부는 <paramref name="includeHidden"/>로).
    /// </summary>
    public void FlattenInto(PixelImage target, int frameIndex, bool includeHidden = false, Func<Layer, bool>? layerFilter = null)
    {
        if (target.Width != _width || target.Height != _height)
            throw new ArgumentException("대상 이미지 크기가 캔버스와 다릅니다.", nameof(target));
        target.Clear();
        var frame = _frames[frameIndex];
        foreach (var layer in _layers)
        {
            if (layer.IsGroup) continue;
            if (!includeHidden && !(layer.Visible && (layer.Parent?.Visible ?? true))) continue;
            if (layerFilter is not null && !layerFilter(layer)) continue;
            DrawCel(target, layer, frame);
        }
    }

    /// <summary>그룹 하나의 보이는 부위만 합성한 캔버스 크기 이미지(썸네일용, 그룹 자신의 보이기 여부는 무시).</summary>
    public PixelImage FlattenGroup(Layer group, int frameIndex)
    {
        if (!group.IsGroup) throw new ArgumentException("그룹 레이어가 아닙니다.", nameof(group));
        var result = new PixelImage(_width, _height);
        var frame = _frames[frameIndex];
        foreach (var part in ChildrenOf(group))
            if (part.Visible) DrawCel(result, part, frame);
        return result;
    }

    private void DrawCel(PixelImage target, Layer layer, Frame frame)
    {
        if (!_cels.TryGetValue((layer, frame), out var cel)) return;
        if (layer.IsPart)
        {
            byte opacity = (byte)((layer.Opacity * (layer.Parent?.Opacity ?? 255) + 127) / 255);
            target.Composite(cel, layer.PartOffset.X, layer.PartOffset.Y, opacity);
        }
        else target.Composite(cel, 0, 0, layer.Opacity);
    }

    // ───────────── 스트로크(픽셀 편집) ─────────────

    public bool IsStrokeActive => _stroke is not null;

    /// <summary>
    /// 셀에 그리기 시작: 편집할 셀 이미지(없으면 투명 셀을 새로 만듦, 부위면 부위 크기)를 돌려준다. 이 이미지를 직접 고친 뒤
    /// <see cref="CommitCelStroke"/>로 바뀐 픽셀을 한 단계로 기록한다. 잠긴 레이어(또는 잠긴 그룹의 부위)·그룹이면
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public PixelImage BeginCelStroke(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        var frame = _frames[frameIndex];
        if (layer.IsGroup) throw new InvalidOperationException(GroupHasNoCelsMessage);
        if (_stroke is not null) CommitCelStroke("편집");
        if (IsLayerLocked(layerIndex)) throw new InvalidOperationException("잠긴 레이어에는 그릴 수 없습니다.");
        bool created = !_cels.TryGetValue((layer, frame), out var image);
        if (created)
        {
            var size = CelSize(layer);
            image = new PixelImage(size.Width, size.Height);
            _cels[(layer, frame)] = image;
        }
        _stroke = new StrokeState(layer, frame, image!, image!.Clone(), created);
        return image!;
    }

    /// <summary>스트로크 확정. 바뀐 픽셀이 없으면 기록하지 않고 false.</summary>
    public bool CommitCelStroke(string label)
    {
        var s = _stroke;
        _stroke = null;
        if (s is null) return false;
        var patch = PixelPatch.Create(s.Before, s.Image);
        if (patch is null)
        {
            if (s.Created) _cels.Remove((s.Layer, s.Frame));
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
        s.Image.CopyFrom(s.Before);
        if (s.Created) _cels.Remove((s.Layer, s.Frame));
        NotifyChanged(ProjectChange.Pixels, markDirty: false);
    }

    /// <summary>열린 스트로크를 확정한 뒤 실행취소.</summary>
    public bool Undo()
    {
        if (_stroke is not null) CommitCelStroke("편집");
        return History.Undo();
    }

    /// <summary>열린 스트로크를 확정한 뒤 다시 실행.</summary>
    public bool Redo()
    {
        if (_stroke is not null) CommitCelStroke("편집");
        return History.Redo();
    }

    // ───────────── 일반 편집(실행취소 기록) ─────────────

    /// <summary>
    /// 실행취소 가능한 편집 하나: 전후 상태(레이어·프레임·태그 목록과 속성, 셀 참조, 캔버스 크기, 설정)를
    /// 기록한다. 안에서 다른 편집 메서드를 불러도 한 단계로 묶인다. 예외가 나면 편집 전으로 되돌린다.
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
        if (_stroke is not null) CommitCelStroke("편집");
        var before = Snapshot.Capture(this);
        _editDepth++;
        try
        {
            action();
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

    private const string GroupHasNoCelsMessage = "그룹 레이어에는 그릴 수 없습니다 — 부위 레이어를 고르세요.";

    /// <summary>그룹의 부위들(아래 → 위). 그룹이 아니면 빈 목록.</summary>
    public IReadOnlyList<Layer> ChildrenOf(Layer group)
    {
        int g = _layers.IndexOf(group);
        int n = ChildCount(g);
        return n == 0 ? [] : _layers.GetRange(g - n, n);
    }

    /// <summary>그룹의 부위 수(그룹이 아니면 0).</summary>
    public int ChildCount(Layer group) => ChildCount(_layers.IndexOf(group));

    private int ChildCount(int groupIndex)
    {
        if (groupIndex < 0 || !_layers[groupIndex].IsGroup) return 0;
        var group = _layers[groupIndex];
        int n = 0;
        while (groupIndex - n - 1 >= 0 && ReferenceEquals(_layers[groupIndex - n - 1].Parent, group)) n++;
        return n;
    }

    /// <summary>실제로 보이는지: 자신이 보이고, 부위면 그룹도 보일 때.</summary>
    public bool IsLayerVisible(int index)
    {
        var layer = _layers[index];
        return layer.Visible && (layer.Parent?.Visible ?? true);
    }

    /// <summary>실제로 잠겼는지: 자신이 잠겼거나, 부위면 그룹이 잠겼을 때.</summary>
    public bool IsLayerLocked(int index)
    {
        var layer = _layers[index];
        return layer.Locked || (layer.Parent?.Locked ?? false);
    }

    /// <summary>최상위 레이어(일반·그룹) 덩어리의 시작 번호(그룹이면 맨 아래 부위).</summary>
    private int BlockStart(int topIndex) => topIndex - ChildCount(topIndex);

    /// <summary>최상위 레이어를 넣을 자리를 덩어리를 쪼개지 않는 경계로 맞춘다(그룹 안이면 그 그룹 바로 위).</summary>
    private int TopLevelInsertIndex(int index)
    {
        int at = Math.Clamp(index, 0, _layers.Count);
        if (at > 0 && _layers[at - 1].Parent is { } group) at = _layers.IndexOf(group) + 1;
        return at;
    }

    private Layer RequirePart(int layerIndex)
    {
        var layer = _layers[layerIndex];
        return layer.IsPart ? layer : throw new ArgumentException("부위 레이어가 아닙니다.", nameof(layerIndex));
    }

    /// <summary>빈 일반 레이어를 <paramref name="index"/>(기본: 맨 위, 그룹 안이면 그 그룹 위)에 넣는다.</summary>
    public Layer AddLayer(int? index = null, string? name = null)
    {
        int at = TopLevelInsertIndex(index ?? _layers.Count);
        var layer = new Layer(string.IsNullOrWhiteSpace(name) ? NextLayerName() : UniqueLayerName(name.Trim()));
        Edit("레이어 추가", ProjectChange.Layers, () => _layers.Insert(at, layer));
        return layer;
    }

    /// <summary>빈 그룹(합친 레이어)을 <paramref name="index"/>(기본: 맨 위, 그룹 안이면 그 그룹 위)에 넣는다.</summary>
    public Layer AddGroup(string? name = null, int? index = null)
    {
        int at = TopLevelInsertIndex(index ?? _layers.Count);
        var group = new Layer(UniqueLayerName(string.IsNullOrWhiteSpace(name) ? "그룹" : name.Trim())) { Kind = LayerKind.Group };
        Edit("그룹 추가", ProjectChange.Layers, () => _layers.Insert(at, group));
        return group;
    }

    /// <summary>
    /// 그룹의 맨 위 부위로 빈 부위 레이어를 넣는다. 위치를 안 주면 캔버스 가운데. 이름은 겹치지 않게 바뀐다.
    /// 그룹이 아니거나 크기가 잘못되면 <see cref="ArgumentException"/>.
    /// </summary>
    public Layer AddPart(Layer group, string? name, int width, int height, Point? offset = null)
    {
        if (!group.IsGroup || !_layers.Contains(group))
            throw new ArgumentException("부위는 이 프로젝트의 그룹 레이어에만 넣을 수 있습니다.", nameof(group));
        ValidatePartSize(width, height);
        var part = new Layer(UniqueLayerName(string.IsNullOrWhiteSpace(name) ? "부위" : name.Trim()))
        {
            Kind = LayerKind.Part,
            Parent = group,
            PartSize = new Size(width, height),
            PartOffset = offset ?? new Point((_width - width) / 2, (_height - height) / 2),
        };
        Edit("부위 추가", ProjectChange.Layers, () => _layers.Insert(_layers.IndexOf(group), part));
        return part;
    }

    /// <summary>부위 위치(캔버스 안 왼쪽 위, 모든 프레임 공통).</summary>
    public void SetPartOffset(int layerIndex, Point offset)
    {
        var part = RequirePart(layerIndex);
        if (part.PartOffset == offset) return;
        Edit("부위 위치", ProjectChange.Layers | ProjectChange.Pixels, () => part.PartOffset = offset);
    }

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
        var (px, py) = part.PartPivot.ToLocalPixel(old.Width, old.Height);
        var pivot = new Pivot((px + ox) / width, 1f - (py + oy) / height);
        var offset = new Point(part.PartOffset.X - ox, part.PartOffset.Y - oy);
        Edit("부위 크기", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var (frame, cel) in CelsOf(part)) _cels[(part, frame)] = cel.ResizeCanvas(width, height, anchor);
            part.PartSize = new Size(width, height);
            part.PartOffset = offset;
            part.PartPivot = pivot;
        });
    }

    /// <summary>부위 피벗(부위 기준 0~1, 아래 왼쪽 원점). 부위 아틀라스 내보내기에 쓴다.</summary>
    public void SetPartPivot(int layerIndex, Pivot pivot)
    {
        var part = RequirePart(layerIndex);
        Edit("부위 피벗", ProjectChange.Layers, () => part.PartPivot = pivot);
    }

    /// <summary>부위 9-slice 테두리(px). 부위 아틀라스 내보내기에 쓴다.</summary>
    public void SetPartBorder(int layerIndex, SpriteBorder border)
    {
        var part = RequirePart(layerIndex);
        Edit("부위 테두리", ProjectChange.Layers, () => part.PartBorder = border);
    }

    /// <summary>그룹 접기/펼치기(타임라인 표시용, 실행취소·수정됨 표시 없음).</summary>
    public void SetLayerCollapsed(int layerIndex, bool collapsed)
    {
        var group = _layers[layerIndex];
        if (!group.IsGroup) throw new ArgumentException("그룹 레이어가 아닙니다.", nameof(layerIndex));
        if (group.Collapsed == collapsed) return;
        group.Collapsed = collapsed;
        NotifyChanged(ProjectChange.Layers, markDirty: false);
    }

    /// <summary>
    /// 레이어를 셀까지 복사해 바로 위에 넣는다. 그룹은 부위까지 통째로(덩어리 바로 위), 부위는 같은 그룹 안 바로 위.
    /// </summary>
    public Layer DuplicateLayer(int index)
    {
        var src = _layers[index];
        return Edit("레이어 복제", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var copy = CopyLayer(src, src.Parent);
            _layers.Insert(index + 1, copy);
            if (src.IsGroup)
            {
                var children = ChildrenOf(src).ToList();
                for (int k = 0; k < children.Count; k++)
                    _layers.Insert(index + 1 + k, CopyLayer(children[k], copy));
            }
            return copy;
        });
    }

    /// <summary>레이어 하나를 셀까지 복사한 새 레이어(목록에는 넣지 않음, 원래 칸 기억은 복사하지 않음).</summary>
    private Layer CopyLayer(Layer src, Layer? parent)
    {
        var copy = new Layer(UniqueLayerName(src.Name + " 복사"))
        {
            Visible = src.Visible,
            Opacity = src.Opacity,
            Kind = src.Kind,
            Parent = parent,
            Collapsed = src.Collapsed,
            PartSize = src.PartSize,
            PartOffset = src.PartOffset,
            PartPivot = src.PartPivot,
            PartBorder = src.PartBorder,
        };
        foreach (var (frame, cel) in CelsOf(src)) _cels[(copy, frame)] = cel.Clone();
        return copy;
    }

    /// <summary>레이어 삭제. 그룹은 부위까지 지운다. 남는 레이어가 없으면 <see cref="InvalidOperationException"/>.</summary>
    public void DeleteLayer(int index)
    {
        var layer = _layers[index];
        int start = layer.IsGroup ? BlockStart(index) : index;
        int count = index - start + 1;
        if (_layers.Count - count < 1) throw new InvalidOperationException("마지막 레이어는 지울 수 없습니다.");
        Edit("레이어 삭제", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var l in _layers.GetRange(start, count))
                foreach (var (frame, _) in CelsOf(l)) _cels.Remove((l, frame));
            _layers.RemoveRange(start, count);
        });
    }

    /// <summary>
    /// 레이어 순서 바꾸기. 부위는 자기 그룹 안에서만(<paramref name="to"/> = 옮긴 뒤 위치, 그룹 범위로 자름).
    /// 일반 레이어·그룹은 덩어리째 옮기고, <paramref name="to"/>는 덩어리를 뺀 목록에서 덩어리가 들어갈 자리
    /// (그룹 안이면 가까운 그룹 경계로 맞춤). 일반 레이어만 있으면 옮긴 뒤 위치와 같다.
    /// </summary>
    public void MoveLayer(int from, int to)
    {
        var layer = _layers[from];
        if (layer.IsPart)
        {
            int g = _layers.IndexOf(layer.Parent!);
            to = Math.Clamp(to, g - ChildCount(g), g - 1);
            if (from == to) return;
            Edit("레이어 이동", ProjectChange.Layers, () =>
            {
                _layers.RemoveAt(from);
                _layers.Insert(to, layer);
            });
            return;
        }

        int start = BlockStart(from);
        int count = from - start + 1;
        var rest = new List<Layer>(_layers);
        rest.RemoveRange(start, count);
        int at = Math.Clamp(to, 0, rest.Count);
        if (at > 0 && rest[at - 1].Parent is { } group)
        {
            int top = rest.IndexOf(group);
            int bottom = top - rest.Count(l => ReferenceEquals(l.Parent, group));
            int below = at - bottom, above = top + 1 - at;
            at = below < above || (below == above && at < start) ? bottom : top + 1;
        }
        if (at == start) return;
        var block = _layers.GetRange(start, count);
        Edit("레이어 이동", ProjectChange.Layers, () =>
        {
            _layers.RemoveRange(start, count);
            _layers.InsertRange(at, block);
        });
    }

    /// <summary>한 칸 위로(▲): 부위는 그룹 안에서 위 부위와, 일반 레이어·그룹은 바로 위 덩어리와 자리를 바꾼다. 맨 위면 그대로.</summary>
    public void MoveLayerUp(int index)
    {
        var layer = _layers[index];
        if (layer.IsPart)
        {
            if (_layers[index + 1].IsPart) MoveLayer(index, index + 1);
            return;
        }
        if (index == _layers.Count - 1) return;
        var next = _layers[index + 1];
        int aboveTop = next.Parent is { } g ? _layers.IndexOf(g) : index + 1;
        MoveLayer(index, BlockStart(index) + (aboveTop - index));
    }

    /// <summary>한 칸 아래로(▼): 부위는 그룹 안에서 아래 부위와, 일반 레이어·그룹은 바로 아래 덩어리와 자리를 바꾼다. 맨 아래면 그대로.</summary>
    public void MoveLayerDown(int index)
    {
        var layer = _layers[index];
        if (layer.IsPart)
        {
            if (index > 0 && ReferenceEquals(_layers[index - 1].Parent, layer.Parent)) MoveLayer(index, index - 1);
            return;
        }
        int start = BlockStart(index);
        if (start == 0) return;
        MoveLayer(index, BlockStart(start - 1));
    }

    /// <summary>
    /// 일반 레이어를 바로 아래 일반 레이어에 병합한다(위 레이어를 불투명도대로 합성, 보이기 여부와 관계없이). 위 레이어는 삭제된다.
    /// 부위·그룹이 끼면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void MergeLayerDown(int index)
    {
        if (index <= 0 || index >= _layers.Count) throw new ArgumentOutOfRangeException(nameof(index), "아래에 병합할 레이어가 없습니다.");
        var upper = _layers[index];
        var lower = _layers[index - 1];
        if (upper.Kind != LayerKind.Normal || lower.Kind != LayerKind.Normal)
            throw new InvalidOperationException("부위·그룹 레이어는 아래로 병합할 수 없습니다.");
        Edit("아래 레이어와 병합", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var (frame, top) in CelsOf(upper))
            {
                var merged = _cels.TryGetValue((lower, frame), out var bottom) ? bottom.Clone() : new PixelImage(top.Width, top.Height);
                merged.Composite(top, 0, 0, upper.Opacity);
                _cels[(lower, frame)] = merged;
                _cels.Remove((upper, frame));
            }
            _layers.RemoveAt(index);
        });
    }

    /// <summary>레이어의 모든 셀(프레임, 이미지) 사본 목록.</summary>
    private List<(Frame Frame, PixelImage Cel)> CelsOf(Layer layer)
        => _cels.Where(kv => ReferenceEquals(kv.Key.Item1, layer)).Select(kv => (kv.Key.Item2, kv.Value)).ToList();

    /// <summary>레이어 속성 바꾸기(null인 값은 그대로).</summary>
    public void SetLayerProperties(int index, string? name = null, bool? visible = null, bool? locked = null, byte? opacity = null)
    {
        var layer = _layers[index];
        Edit("레이어 속성", ProjectChange.Layers, () =>
        {
            if (!string.IsNullOrWhiteSpace(name)) layer.Name = name.Trim();
            if (visible is { } v) layer.Visible = v;
            if (locked is { } l) layer.Locked = l;
            if (opacity is { } o) layer.Opacity = o;
        });
    }

    // ───────────── 프레임 ─────────────

    /// <summary>
    /// 빈 프레임을 <paramref name="index"/>(기본: 맨 끝)에 넣는다. 길이는 앞 프레임과 같다.
    /// 태그 범위 안(시작 뒤 ~ 끝)에 넣으면 그 태그가 늘어나고, 뒤쪽 태그는 밀린다.
    /// </summary>
    public Frame AddFrame(int? index = null)
    {
        int at = Math.Clamp(index ?? _frames.Count, 0, _frames.Count);
        var frame = new Frame();
        if (_frames.Count > 0) frame.DurationMs = _frames[Math.Max(0, at - 1)].DurationMs;
        Edit("프레임 추가", ProjectChange.Frames | ProjectChange.Tags, () =>
        {
            _frames.Insert(at, frame);
            foreach (var t in _tags)
            {
                if (t.From >= at) { t.From++; t.To++; }
                else if (t.To >= at) t.To++;
            }
        });
        return frame;
    }

    /// <summary>프레임을 셀까지 복사해 바로 뒤에 넣는다. 원래 프레임을 포함한 태그는 늘어난다.</summary>
    public Frame DuplicateFrame(int index)
    {
        var src = _frames[index];
        var copy = new Frame { DurationMs = src.DurationMs, Pivot = src.Pivot };
        Edit("프레임 복제", ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            _frames.Insert(index + 1, copy);
            foreach (var layer in _layers)
                if (_cels.TryGetValue((layer, src), out var cel)) _cels[(layer, copy)] = cel.Clone();
            foreach (var t in _tags)
            {
                if (t.From > index) { t.From++; t.To++; }
                else if (t.To >= index) t.To++;
            }
        });
        return copy;
    }

    /// <summary>프레임 삭제(마지막 하나는 지울 수 없음). 태그 범위가 줄고, 비게 된 태그는 지워진다.</summary>
    public void DeleteFrame(int index)
    {
        if (_frames.Count <= 1) throw new InvalidOperationException("마지막 프레임은 삭제할 수 없습니다.");
        var frame = _frames[index];
        Edit("프레임 삭제", ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            _frames.RemoveAt(index);
            foreach (var layer in _layers) _cels.Remove((layer, frame));
            foreach (var t in _tags)
            {
                if (t.From > index) t.From--;
                if (t.To >= index) t.To--;
            }
            _tags.RemoveAll(t => t.To < t.From);
        });
    }

    /// <summary>
    /// 프레임 순서 바꾸기(<paramref name="to"/> = 옮긴 뒤 위치). 태그 범위는 프레임 번호 기준이라 그대로다
    /// (태그 안의 순서를 바꾸거나 다른 태그 칸으로 옮기는 동작).
    /// </summary>
    public void MoveFrame(int from, int to)
    {
        var frame = _frames[from];
        to = Math.Clamp(to, 0, _frames.Count - 1);
        if (from == to) return;
        Edit("프레임 이동", ProjectChange.Frames, () =>
        {
            _frames.RemoveAt(from);
            _frames.Insert(to, frame);
        });
    }

    public void SetFrameDuration(int index, int durationMs)
    {
        var frame = _frames[index];
        Edit("프레임 길이", ProjectChange.Frames, () => frame.DurationMs = durationMs);
    }

    /// <summary>프레임 피벗 재정의(null = 기본 피벗 사용).</summary>
    public void SetFramePivot(int index, Pivot? pivot)
    {
        var frame = _frames[index];
        Edit("프레임 피벗", ProjectChange.Frames, () => frame.Pivot = pivot);
    }

    /// <summary>내보낼 칸 이름 재정의(null/빈 문자열 = 이름 규칙 사용).</summary>
    public void SetFrameName(int index, string? name)
    {
        var frame = _frames[index];
        Edit("프레임 이름", ProjectChange.Frames, () => frame.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim());
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
        Edit("태그 추가", ProjectChange.Tags, () => _tags.Add(tag));
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
        Edit("태그 편집", ProjectChange.Tags, () =>
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
        Edit("태그 삭제", ProjectChange.Tags, () => _tags.Remove(tag));
    }

    private void ValidateTag(string name, int from, int to, AnimationTag? self)
    {
        if (name.Length == 0) throw new ArgumentException("태그 이름이 비어 있습니다.", nameof(name));
        if (_tags.Any(t => !ReferenceEquals(t, self) && t.Name == name))
            throw new ArgumentException($"이미 같은 이름의 태그가 있습니다: {name}", nameof(name));
        if (from < 0 || to >= _frames.Count || from > to)
            throw new ArgumentOutOfRangeException(nameof(from), $"태그 프레임 범위가 잘못되었습니다: {from}~{to}");
    }

    // ───────────── 캔버스 ─────────────

    /// <summary>
    /// 캔버스 크기 바꾸기: 일반 레이어 셀을 <paramref name="anchor"/> 기준으로 옮기고, 부위는 위치만 같은 만큼 옮기며(부위 셀은 그대로),
    /// 피벗이 같은 그림 지점을 가리키도록 보정한다.
    /// </summary>
    public void ResizeCanvas(int width, int height, Anchor anchor)
    {
        ValidateSize(width, height);
        if (width == _width && height == _height) return;
        var (ox, oy) = PixelImage.AnchorOffset(_width, _height, width, height, anchor);
        int oldW = _width, oldH = _height;
        Pivot Shift(Pivot p)
        {
            var (px, py) = p.ToLocalPixel(oldW, oldH);
            return new Pivot((px + ox) / width, 1f - (py + oy) / height);
        }
        Edit("캔버스 크기", ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Settings | ProjectChange.Layers, () =>
        {
            foreach (var key in _cels.Keys.Where(k => !k.Item1.IsPart).ToList())
                _cels[key] = _cels[key].ResizeCanvas(width, height, anchor);
            foreach (var part in _layers.Where(l => l.IsPart))
                part.PartOffset = new Point(part.PartOffset.X + ox, part.PartOffset.Y + oy);
            _width = width;
            _height = height;
            Sprite.DefaultPivot = Shift(Sprite.DefaultPivot);
            foreach (var f in _frames)
                if (f.Pivot is { } fp) f.Pivot = Shift(fp);
        });
    }

    /// <summary>모든 셀 좌우 뒤집기(피벗·부위 위치·테두리도 뒤집음).</summary>
    public void FlipCanvasHorizontal()
    {
        int w = _width;
        TransformCanvas("좌우 뒤집기", ImageTransform.FlipHorizontal, p => new Pivot(1f - p.X, p.Y),
            r => new Rectangle(w - r.Right, r.Y, r.Width, r.Height),
            b => new SpriteBorder(b.Right, b.Top, b.Left, b.Bottom));
    }

    /// <summary>모든 셀 상하 뒤집기(피벗·부위 위치·테두리도 뒤집음).</summary>
    public void FlipCanvasVertical()
    {
        int h = _height;
        TransformCanvas("상하 뒤집기", ImageTransform.FlipVertical, p => new Pivot(p.X, 1f - p.Y),
            r => new Rectangle(r.X, h - r.Bottom, r.Width, r.Height),
            b => new SpriteBorder(b.Left, b.Bottom, b.Right, b.Top));
    }

    /// <summary>모든 셀 90° 회전(가로·세로가 바뀌고 피벗·부위 위치·크기·테두리도 회전).</summary>
    public void RotateCanvas90(bool clockwise = true)
    {
        int w = _width, h = _height;
        if (clockwise)
            TransformCanvas("시계 방향 회전", img => ImageTransform.Rotate90(img, true), p => new Pivot(p.Y, 1f - p.X),
                r => new Rectangle(h - r.Bottom, r.X, r.Height, r.Width),
                b => new SpriteBorder(b.Bottom, b.Left, b.Top, b.Right));
        else
            TransformCanvas("반시계 방향 회전", img => ImageTransform.Rotate90(img, false), p => new Pivot(1f - p.Y, p.X),
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
            foreach (var key in _cels.Keys.ToList()) _cels[key] = transform(_cels[key]);
            foreach (var part in _layers.Where(l => l.IsPart))
            {
                var r = partRectMap(part.PartRect);
                part.PartOffset = r.Location;
                part.PartSize = r.Size;
                part.PartPivot = pivotMap(part.PartPivot);
                part.PartBorder = borderMap(part.PartBorder);
            }
            _width = probe.Width;
            _height = probe.Height;
            Sprite.DefaultPivot = pivotMap(Sprite.DefaultPivot);
            foreach (var f in _frames)
                if (f.Pivot is { } fp) f.Pivot = pivotMap(fp);
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

    /// <summary>"레이어 N" 중 쓰이지 않은 다음 이름.</summary>
    public string NextLayerName()
    {
        for (int i = _layers.Count + 1; ; i++)
            if (_layers.All(l => l.Name != $"레이어 {i}")) return $"레이어 {i}";
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
    internal void SetCelRaw(Layer layer, Frame frame, PixelImage image) => _cels[(layer, frame)] = image;

    /// <summary>
    /// 파일 읽기용: 그룹이 없는(또는 그룹이 아닌 부모를 가진) 부위는 일반 레이어로 바꾸고, 그룹의 부위를 그룹 바로 아래로 모은다.
    /// 바꾼 것이 있으면 true.
    /// </summary>
    internal bool NormalizeHierarchy()
    {
        bool changed = false;
        foreach (var layer in _layers)
        {
            if (layer.IsPart && (layer.Parent is not { IsGroup: true } parent || !_layers.Contains(parent)))
            {
                layer.Kind = LayerKind.Normal;
                layer.Parent = null;
                changed = true;
            }
            else if (!layer.IsPart && layer.Parent is not null)
            {
                layer.Parent = null;
                changed = true;
            }
        }
        var ordered = new List<Layer>(_layers.Count);
        foreach (var layer in _layers)
        {
            if (layer.IsPart) continue;
            if (layer.IsGroup) ordered.AddRange(_layers.Where(l => ReferenceEquals(l.Parent, layer)));
            ordered.Add(layer);
        }
        if (!ordered.SequenceEqual(_layers))
        {
            _layers.Clear();
            _layers.AddRange(ordered);
            changed = true;
        }
        return changed;
    }

    private static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new ArgumentOutOfRangeException(nameof(width), $"캔버스 크기는 1~{PixelImage.MaxSize} 사이여야 합니다: {width}x{height}");
    }

    private static void ValidatePartSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new ArgumentException($"부위 크기는 1~{PixelImage.MaxSize} 사이여야 합니다: {width}x{height}", nameof(width));
    }

    // ───────────── 실행취소 기록 ─────────────

    /// <summary>열린 스트로크.</summary>
    private sealed record StrokeState(Layer Layer, Frame Frame, PixelImage Image, PixelImage Before, bool Created);

    private sealed class CelStrokeCommand(SpriteProject project, string label, StrokeState stroke, PixelPatch patch) : IUndoCommand
    {
        public string Label { get; } = label;

        public void Undo()
        {
            patch.ApplyBefore(stroke.Image);
            if (stroke.Created) project._cels.Remove((stroke.Layer, stroke.Frame));
            project.NotifyChanged(ProjectChange.Pixels);
        }

        public void Redo()
        {
            project._cels[(stroke.Layer, stroke.Frame)] = stroke.Image;
            patch.ApplyAfter(stroke.Image);
            project.NotifyChanged(ProjectChange.Pixels);
        }
    }

    private sealed class SnapshotCommand(SpriteProject project, string label, Snapshot before, Snapshot after, ProjectChange change)
        : IUndoCommand
    {
        public string Label { get; } = label;

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
        Size PartSize, Point PartOffset, Pivot PartPivot, SpriteBorder PartBorder, Rectangle? PartSourceRect)
    {
        public static LayerState Of(Layer l) => new(l, l.Name, l.Visible, l.Locked, l.Opacity, l.Kind, l.Parent,
            l.PartSize, l.PartOffset, l.PartPivot, l.PartBorder, l.PartSourceRect);

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
            Layer.PartPivot = PartPivot;
            Layer.PartBorder = PartBorder;
            Layer.PartSourceRect = PartSourceRect;
            return Layer;
        }
    }

    /// <summary>구조 상태 사본(그룹 접힘은 제외). 셀 이미지는 참조만 담는다(구조 편집은 셀을 새 이미지로 바꿔 넣으므로 안전).</summary>
    private sealed class Snapshot
    {
        private int _w, _h;
        private LayerState[] _layers = [];
        private (Frame Frame, int Ms, Pivot? Pivot, string? Name)[] _frames = [];
        private (AnimationTag Tag, AnimationTag State)[] _tags = [];
        private KeyValuePair<(Layer, Frame), PixelImage>[] _cels = [];
        private SpriteSettings _sprite = new();
        private ExportSettings _export = new();

        public static Snapshot Capture(SpriteProject p) => new()
        {
            _w = p._width,
            _h = p._height,
            _layers = p._layers.Select(LayerState.Of).ToArray(),
            _frames = p._frames.Select(f => (f, f.DurationMs, f.Pivot, f.Name)).ToArray(),
            _tags = p._tags.Select(t => (t, t.Clone())).ToArray(),
            _cels = p._cels.ToArray(),
            _sprite = p.Sprite.Clone(),
            _export = p.Export.Clone(),
        };

        public void Restore(SpriteProject p)
        {
            p._width = _w;
            p._height = _h;
            p._layers.Clear();
            foreach (var state in _layers) p._layers.Add(state.Apply());
            p._frames.Clear();
            foreach (var (frame, ms, pivot, name) in _frames)
            {
                frame.DurationMs = ms;
                frame.Pivot = pivot;
                frame.Name = name;
                p._frames.Add(frame);
            }
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
            p._cels.Clear();
            foreach (var kv in _cels) p._cels[kv.Key] = kv.Value;
            p.Sprite.CopyFrom(_sprite);
            p.Export.CopyFrom(_export);
        }

        public ProjectChange SizeChange(Snapshot other)
            => _w != other._w || _h != other._h ? ProjectChange.Canvas : ProjectChange.None;
    }
}
