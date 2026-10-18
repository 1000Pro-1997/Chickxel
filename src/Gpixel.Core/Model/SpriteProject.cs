using System.Drawing;
using Gpixel.Core.Edit;

namespace Gpixel.Core.Model;

/// <summary>
/// Gpixel 프로젝트(<c>.gpixel</c>): 공통 캔버스 크기 × 레이어 × 프레임의 셀(cel) + 태그(애니메이션) +
/// Unity·내보내기 설정 + 실행취소 기록.
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
    /// <summary>셀: (레이어, 주인) → 이미지. 주인은 <see cref="Frame"/>(애니메이션) 또는 <see cref="AtlasSprite"/>(아틀라스).</summary>
    private readonly Dictionary<(Layer, object), PixelImage> _cels = new();
    private readonly List<AtlasSprite> _sprites = new();
    private ProjectKind _kind;
    private int _width, _height;
    private string? _path;
    private bool _isDirty;
    private int _editDepth;
    private StrokeState? _stroke;

    /// <summary>
    /// 레이어 1개짜리 새 프로젝트. 애니메이션이면 빈 프레임 1개, 아틀라스면 캔버스 크기의 빈 스프라이트 1개("sprite")와
    /// (쓰이지 않는) 프레임 1개를 갖는다.
    /// </summary>
    public SpriteProject(int width, int height, ProjectKind kind = ProjectKind.Animation) : this(width, height, addDefaults: true)
    {
        _kind = kind;
        if (kind == ProjectKind.Atlas) _sprites.Add(new AtlasSprite("sprite", width, height) { Pivot = Sprite.DefaultPivot });
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
    internal static SpriteProject CreateBlank(int width, int height, ProjectKind kind = ProjectKind.Animation)
        => new(width, height, addDefaults: false) { _kind = kind };

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

    /// <summary>레이어(0 = 맨 아래).</summary>
    public IReadOnlyList<Layer> Layers => _layers;
    public IReadOnlyList<Frame> Frames => _frames;
    public IReadOnlyList<AnimationTag> Tags => _tags;

    /// <summary>프로젝트 종류(애니메이션 / 아틀라스). 파일을 만들 때 정해진다.</summary>
    public ProjectKind Kind => _kind;

    public bool IsAtlas => _kind == ProjectKind.Atlas;

    /// <summary>아틀라스 스프라이트(애니메이션 프로젝트면 비어 있음).</summary>
    public IReadOnlyList<AtlasSprite> Sprites => _sprites;

    /// <summary>
    /// 가져온 원래 시트 크기(아틀라스 "원래 위치 유지" 내보내기에서 시트를 최소 이 크기로 만들어 Unity 좌표가 같게 함).
    /// 없으면 <see cref="Size.Empty"/>.
    /// </summary>
    public Size AtlasSourceSize { get; internal set; }

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

    /// <summary>셀 이미지(빈 셀이면 null). 반환값을 직접 고치지 말고 스트로크로 고친다.</summary>
    public PixelImage? GetCel(int layerIndex, int frameIndex)
        => _cels.GetValueOrDefault((_layers[layerIndex], _frames[frameIndex]));

    public PixelImage? GetCel(Layer layer, Frame frame) => _cels.GetValueOrDefault((layer, frame));

    /// <summary>셀이 없거나 모두 투명이면 true.</summary>
    public bool IsCelEmpty(int layerIndex, int frameIndex)
        => GetCel(layerIndex, frameIndex) is not { } c || c.IsRegionEmpty(c.Bounds);

    /// <summary>셀을 통째로 바꾼다(null = 비우기, 실행취소됨). 이미지 크기는 캔버스와 같아야 한다.</summary>
    public void SetCel(int layerIndex, int frameIndex, PixelImage? image, string label = "셀 바꾸기")
    {
        var key = (_layers[layerIndex], _frames[frameIndex]);
        if (image is not null && (image.Width != _width || image.Height != _height))
            throw new ArgumentException("셀 크기는 캔버스 크기와 같아야 합니다.", nameof(image));
        Edit(label, ProjectChange.Pixels, () =>
        {
            if (image is null) _cels.Remove(key);
            else _cels[key] = image;
        });
    }

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
    /// <paramref name="layerFilter"/>가 있으면 그 레이어만(보이기 여부는 <paramref name="includeHidden"/>로).
    /// </summary>
    public void FlattenInto(PixelImage target, int frameIndex, bool includeHidden = false, Func<Layer, bool>? layerFilter = null)
    {
        if (target.Width != _width || target.Height != _height)
            throw new ArgumentException("대상 이미지 크기가 캔버스와 다릅니다.", nameof(target));
        target.Clear();
        var frame = _frames[frameIndex];
        foreach (var layer in _layers)
        {
            if (!includeHidden && !layer.Visible) continue;
            if (layerFilter is not null && !layerFilter(layer)) continue;
            if (_cels.TryGetValue((layer, frame), out var cel)) target.Composite(cel, 0, 0, layer.Opacity);
        }
    }

    // ───────────── 스트로크(픽셀 편집) ─────────────

    public bool IsStrokeActive => _stroke is not null;

    /// <summary>
    /// 셀에 그리기 시작: 편집할 셀 이미지(없으면 투명 셀을 새로 만듦)를 돌려준다. 이 이미지를 직접 고친 뒤
    /// <see cref="CommitCelStroke"/>로 바뀐 픽셀을 한 단계로 기록한다. 잠긴 레이어면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public PixelImage BeginCelStroke(int layerIndex, int frameIndex)
        => BeginStroke(_layers[layerIndex], _frames[frameIndex], _width, _height);

    /// <summary>아틀라스 스프라이트 셀에 그리기 시작(<see cref="BeginCelStroke"/>와 같고, 확정·취소도 같은 메서드).</summary>
    public PixelImage BeginSpriteStroke(int layerIndex, int spriteIndex)
    {
        var sprite = _sprites[spriteIndex];
        return BeginStroke(_layers[layerIndex], sprite, sprite.Width, sprite.Height);
    }

    private PixelImage BeginStroke(Layer layer, object owner, int width, int height)
    {
        if (_stroke is not null) CommitCelStroke("편집");
        if (layer.Locked) throw new InvalidOperationException("잠긴 레이어에는 그릴 수 없습니다.");
        bool created = !_cels.TryGetValue((layer, owner), out var image);
        if (created)
        {
            image = new PixelImage(width, height);
            _cels[(layer, owner)] = image;
        }
        _stroke = new StrokeState(layer, owner, image!, image!.Clone(), created);
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
            if (s.Created) _cels.Remove((s.Layer, s.Owner));
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
        if (s.Created) _cels.Remove((s.Layer, s.Owner));
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

    /// <summary>빈 레이어를 <paramref name="index"/>(기본: 맨 위)에 넣는다.</summary>
    public Layer AddLayer(int? index = null, string? name = null)
    {
        int at = Math.Clamp(index ?? _layers.Count, 0, _layers.Count);
        var layer = new Layer(string.IsNullOrWhiteSpace(name) ? NextLayerName() : UniqueLayerName(name.Trim()));
        Edit("레이어 추가", ProjectChange.Layers, () => _layers.Insert(at, layer));
        return layer;
    }

    /// <summary>레이어를 셀까지 복사해 바로 위에 넣는다.</summary>
    public Layer DuplicateLayer(int index)
    {
        var src = _layers[index];
        var copy = new Layer(UniqueLayerName(src.Name + " 복사")) { Visible = src.Visible, Opacity = src.Opacity };
        Edit("레이어 복제", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _layers.Insert(index + 1, copy);
            foreach (var (owner, cel) in CelsOf(src))
                _cels[(copy, owner)] = cel.Clone();
        });
        return copy;
    }

    /// <summary>레이어 삭제(마지막 하나는 지울 수 없음).</summary>
    public void DeleteLayer(int index)
    {
        if (_layers.Count <= 1) throw new InvalidOperationException("마지막 레이어는 삭제할 수 없습니다.");
        var layer = _layers[index];
        Edit("레이어 삭제", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            _layers.RemoveAt(index);
            foreach (var (owner, _) in CelsOf(layer)) _cels.Remove((layer, owner));
        });
    }

    /// <summary>레이어 순서 바꾸기(<paramref name="to"/> = 옮긴 뒤 위치).</summary>
    public void MoveLayer(int from, int to)
    {
        var layer = _layers[from];
        to = Math.Clamp(to, 0, _layers.Count - 1);
        if (from == to) return;
        Edit("레이어 이동", ProjectChange.Layers, () =>
        {
            _layers.RemoveAt(from);
            _layers.Insert(to, layer);
        });
    }

    /// <summary>
    /// 레이어를 아래 레이어에 병합한다(위 레이어를 불투명도대로 합성, 보이기 여부와 관계없이). 위 레이어는 삭제된다.
    /// </summary>
    public void MergeLayerDown(int index)
    {
        if (index <= 0 || index >= _layers.Count) throw new ArgumentOutOfRangeException(nameof(index), "아래에 병합할 레이어가 없습니다.");
        var upper = _layers[index];
        var lower = _layers[index - 1];
        Edit("아래 레이어와 병합", ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var (owner, top) in CelsOf(upper))
            {
                var merged = _cels.TryGetValue((lower, owner), out var bottom) ? bottom.Clone() : new PixelImage(top.Width, top.Height);
                merged.Composite(top, 0, 0, upper.Opacity);
                _cels[(lower, owner)] = merged;
                _cels.Remove((upper, owner));
            }
            _layers.RemoveAt(index);
        });
    }

    /// <summary>레이어의 모든 셀(주인, 이미지) 사본 목록.</summary>
    private List<(object Owner, PixelImage Cel)> CelsOf(Layer layer)
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
    /// 캔버스 크기 바꾸기: 모든 셀을 <paramref name="anchor"/> 기준으로 옮기고, 피벗이 같은 그림 지점을 가리키도록 보정한다.
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
        Edit("캔버스 크기", ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Settings, () =>
        {
            foreach (var key in FrameCelKeys()) _cels[key] = _cels[key].ResizeCanvas(width, height, anchor);
            _width = width;
            _height = height;
            Sprite.DefaultPivot = Shift(Sprite.DefaultPivot);
            foreach (var f in _frames)
                if (f.Pivot is { } fp) f.Pivot = Shift(fp);
        });
    }

    /// <summary>모든 셀 좌우 뒤집기(피벗도 뒤집음).</summary>
    public void FlipCanvasHorizontal()
        => TransformCanvas("좌우 뒤집기", ImageTransform.FlipHorizontal, p => new Pivot(1f - p.X, p.Y));

    /// <summary>모든 셀 상하 뒤집기(피벗도 뒤집음).</summary>
    public void FlipCanvasVertical()
        => TransformCanvas("상하 뒤집기", ImageTransform.FlipVertical, p => new Pivot(p.X, 1f - p.Y));

    /// <summary>모든 셀 90° 회전(가로·세로가 바뀌고 피벗도 회전).</summary>
    public void RotateCanvas90(bool clockwise = true)
        => TransformCanvas(clockwise ? "시계 방향 회전" : "반시계 방향 회전",
            img => ImageTransform.Rotate90(img, clockwise),
            p => clockwise ? new Pivot(p.Y, 1f - p.X) : new Pivot(1f - p.Y, p.X));

    private void TransformCanvas(string label, Func<PixelImage, PixelImage> transform, Func<Pivot, Pivot> pivotMap)
    {
        var probe = transform(new PixelImage(_width, _height));
        Edit(label, ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Settings, () =>
        {
            foreach (var key in FrameCelKeys()) _cels[key] = transform(_cels[key]);
            _width = probe.Width;
            _height = probe.Height;
            Sprite.DefaultPivot = pivotMap(Sprite.DefaultPivot);
            foreach (var f in _frames)
                if (f.Pivot is { } fp) f.Pivot = pivotMap(fp);
        });
    }

    /// <summary>애니메이션 프레임 셀의 키들(아틀라스 셀 제외).</summary>
    private List<(Layer, object)> FrameCelKeys() => _cels.Keys.Where(k => k.Item2 is Frame).ToList();

    // ───────────── 아틀라스 스프라이트 ─────────────

    public int IndexOf(AtlasSprite sprite) => _sprites.IndexOf(sprite);

    public AtlasSprite? FindSprite(string name) => _sprites.Find(s => s.Name == name);

    /// <summary>스프라이트 셀(빈 셀이면 null). 반환값을 직접 고치지 말고 <see cref="BeginSpriteStroke"/>로 고친다.</summary>
    public PixelImage? GetSpriteCel(int layerIndex, int spriteIndex)
        => _cels.GetValueOrDefault((_layers[layerIndex], _sprites[spriteIndex]));

    public bool IsSpriteCelEmpty(int layerIndex, int spriteIndex)
        => GetSpriteCel(layerIndex, spriteIndex) is not { } c || c.IsRegionEmpty(c.Bounds);

    /// <summary>스프라이트의 보이는 레이어를 합성한 새 이미지(스프라이트 크기).</summary>
    public PixelImage FlattenSprite(int spriteIndex, bool includeHidden = false)
    {
        var sprite = _sprites[spriteIndex];
        var result = new PixelImage(sprite.Width, sprite.Height);
        FlattenSpriteInto(result, spriteIndex, includeHidden);
        return result;
    }

    /// <summary><paramref name="target"/>(스프라이트 크기)을 지우고 스프라이트를 합성해 넣는다.</summary>
    public void FlattenSpriteInto(PixelImage target, int spriteIndex, bool includeHidden = false, Func<Layer, bool>? layerFilter = null)
    {
        var sprite = _sprites[spriteIndex];
        if (target.Width != sprite.Width || target.Height != sprite.Height)
            throw new ArgumentException("대상 이미지 크기가 스프라이트와 다릅니다.", nameof(target));
        target.Clear();
        foreach (var layer in _layers)
        {
            if (!includeHidden && !layer.Visible) continue;
            if (layerFilter is not null && !layerFilter(layer)) continue;
            if (_cels.TryGetValue((layer, sprite), out var cel)) target.Composite(cel, 0, 0, layer.Opacity);
        }
    }

    /// <summary>
    /// 빈 스프라이트 추가(기본: 맨 끝). 이름은 겹치지 않게 바뀐다(<see cref="UniqueSpriteName"/>). 피벗은 프로젝트 기본 피벗.
    /// </summary>
    public AtlasSprite AddSprite(string? name, int width, int height, int? index = null)
    {
        ValidateSize(width, height);
        int at = Math.Clamp(index ?? _sprites.Count, 0, _sprites.Count);
        var sprite = new AtlasSprite(UniqueSpriteName(string.IsNullOrWhiteSpace(name) ? "sprite" : name.Trim()), width, height)
        {
            Pivot = Sprite.DefaultPivot,
        };
        Edit("스프라이트 추가", ProjectChange.Sprites, () => _sprites.Insert(at, sprite));
        return sprite;
    }

    /// <summary>스프라이트를 셀까지 복사해 바로 뒤에 넣는다(이름 <c>{이름}_1</c> …, 원래 위치 기억은 없음).</summary>
    public AtlasSprite DuplicateSprite(int index)
    {
        var src = _sprites[index];
        var copy = new AtlasSprite(UniqueSpriteName(src.Name), src.Width, src.Height) { Pivot = src.Pivot, Border = src.Border };
        Edit("스프라이트 복제", ProjectChange.Sprites | ProjectChange.Pixels, () =>
        {
            _sprites.Insert(index + 1, copy);
            foreach (var layer in _layers)
                if (_cels.TryGetValue((layer, src), out var cel)) _cels[(layer, copy)] = cel.Clone();
        });
        return copy;
    }

    /// <summary>스프라이트 삭제(마지막 하나는 지울 수 없음).</summary>
    public void DeleteSprite(int index)
    {
        if (_sprites.Count <= 1) throw new InvalidOperationException("마지막 스프라이트는 삭제할 수 없습니다.");
        var sprite = _sprites[index];
        Edit("스프라이트 삭제", ProjectChange.Sprites | ProjectChange.Pixels, () =>
        {
            _sprites.RemoveAt(index);
            foreach (var layer in _layers) _cels.Remove((layer, sprite));
        });
    }

    /// <summary>스프라이트 순서 바꾸기(<paramref name="to"/> = 옮긴 뒤 위치).</summary>
    public void MoveSprite(int from, int to)
    {
        var sprite = _sprites[from];
        to = Math.Clamp(to, 0, _sprites.Count - 1);
        if (from == to) return;
        Edit("스프라이트 이동", ProjectChange.Sprites, () =>
        {
            _sprites.RemoveAt(from);
            _sprites.Insert(to, sprite);
        });
    }

    /// <summary>이름 바꾸기. 비었거나 다른 스프라이트가 쓰는 이름이면 <see cref="ArgumentException"/>.</summary>
    public void RenameSprite(int index, string name)
    {
        var sprite = _sprites[index];
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("스프라이트 이름이 비어 있습니다.", nameof(name));
        if (_sprites.Any(s => !ReferenceEquals(s, sprite) && s.Name == name))
            throw new ArgumentException($"이미 같은 이름의 스프라이트가 있습니다: {name}", nameof(name));
        if (sprite.Name == name) return;
        Edit("스프라이트 이름", ProjectChange.Sprites, () => sprite.Name = name);
    }

    /// <summary>스프라이트 피벗(스프라이트 기준 0~1, 아래 왼쪽 원점).</summary>
    public void SetSpritePivot(int index, Pivot pivot)
    {
        var sprite = _sprites[index];
        Edit("스프라이트 피벗", ProjectChange.Sprites, () => sprite.Pivot = pivot);
    }

    /// <summary>9-slice 테두리(px).</summary>
    public void SetSpriteBorder(int index, SpriteBorder border)
    {
        var sprite = _sprites[index];
        Edit("스프라이트 테두리", ProjectChange.Sprites, () => sprite.Border = border);
    }

    /// <summary>
    /// 스프라이트 크기 바꾸기: 셀을 <paramref name="anchor"/> 기준으로 옮기고, 피벗은 같은 그림 픽셀을 가리키도록 보정한다.
    /// </summary>
    public void ResizeSprite(int index, int width, int height, Anchor anchor)
    {
        ValidateSize(width, height);
        var sprite = _sprites[index];
        if (width == sprite.Width && height == sprite.Height) return;
        var (ox, oy) = PixelImage.AnchorOffset(sprite.Width, sprite.Height, width, height, anchor);
        var (px, py) = sprite.Pivot.ToLocalPixel(sprite.Width, sprite.Height);
        var pivot = new Pivot((px + ox) / width, 1f - (py + oy) / height);
        Edit("스프라이트 크기", ProjectChange.Sprites | ProjectChange.Pixels, () =>
        {
            foreach (var layer in _layers)
                if (_cels.TryGetValue((layer, sprite), out var cel)) _cels[(layer, sprite)] = cel.ResizeCanvas(width, height, anchor);
            sprite.Width = width;
            sprite.Height = height;
            sprite.Pivot = pivot;
        });
    }

    /// <summary>다른 스프라이트와 겹치지 않는 이름(<c>base</c>, <c>base_1</c>, …).</summary>
    public string UniqueSpriteName(string baseName)
    {
        baseName = baseName.Trim();
        if (baseName.Length == 0) baseName = "sprite";
        if (FindSprite(baseName) is null) return baseName;
        for (int i = 1; ; i++)
            if (FindSprite($"{baseName}_{i}") is null) return $"{baseName}_{i}";
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
    internal void AddFrameRaw(Frame frame) => _frames.Add(frame);
    internal void AddTagRaw(AnimationTag tag) => _tags.Add(tag);
    internal void SetCelRaw(Layer layer, object owner, PixelImage image) => _cels[(layer, owner)] = image;
    internal void AddSpriteRaw(AtlasSprite sprite) => _sprites.Add(sprite);

    private static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new ArgumentOutOfRangeException(nameof(width), $"캔버스 크기는 1~{PixelImage.MaxSize} 사이여야 합니다: {width}x{height}");
    }

    // ───────────── 실행취소 기록 ─────────────

    /// <summary>열린 스트로크. 주인은 <see cref="Frame"/> 또는 <see cref="AtlasSprite"/>.</summary>
    private sealed record StrokeState(Layer Layer, object Owner, PixelImage Image, PixelImage Before, bool Created);

    private sealed class CelStrokeCommand(SpriteProject project, string label, StrokeState stroke, PixelPatch patch) : IUndoCommand
    {
        public string Label { get; } = label;

        public void Undo()
        {
            patch.ApplyBefore(stroke.Image);
            if (stroke.Created) project._cels.Remove((stroke.Layer, stroke.Owner));
            project.NotifyChanged(ProjectChange.Pixels);
        }

        public void Redo()
        {
            project._cels[(stroke.Layer, stroke.Owner)] = stroke.Image;
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

    /// <summary>구조 상태 사본. 셀 이미지는 참조만 담는다(구조 편집은 셀을 새 이미지로 바꿔 넣으므로 안전).</summary>
    private sealed class Snapshot
    {
        private int _w, _h;
        private (Layer Layer, string Name, bool Visible, bool Locked, byte Opacity)[] _layers = [];
        private (Frame Frame, int Ms, Pivot? Pivot, string? Name)[] _frames = [];
        private (AnimationTag Tag, AnimationTag State)[] _tags = [];
        private (AtlasSprite Sprite, string Name, int W, int H, Pivot Pivot, SpriteBorder Border, Rectangle? Source)[] _sprites = [];
        private KeyValuePair<(Layer, object), PixelImage>[] _cels = [];
        private SpriteSettings _sprite = new();
        private ExportSettings _export = new();

        public static Snapshot Capture(SpriteProject p) => new()
        {
            _w = p._width,
            _h = p._height,
            _layers = p._layers.Select(l => (l, l.Name, l.Visible, l.Locked, l.Opacity)).ToArray(),
            _frames = p._frames.Select(f => (f, f.DurationMs, f.Pivot, f.Name)).ToArray(),
            _tags = p._tags.Select(t => (t, t.Clone())).ToArray(),
            _sprites = p._sprites.Select(s => (s, s.Name, s.Width, s.Height, s.Pivot, s.Border, s.SourceRect)).ToArray(),
            _cels = p._cels.ToArray(),
            _sprite = p.Sprite.Clone(),
            _export = p.Export.Clone(),
        };

        public void Restore(SpriteProject p)
        {
            p._width = _w;
            p._height = _h;
            p._layers.Clear();
            foreach (var (layer, name, visible, locked, opacity) in _layers)
            {
                layer.Name = name;
                layer.Visible = visible;
                layer.Locked = locked;
                layer.Opacity = opacity;
                p._layers.Add(layer);
            }
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
            p._sprites.Clear();
            foreach (var (sprite, name, w, h, pivot, border, source) in _sprites)
            {
                sprite.Name = name;
                sprite.Width = w;
                sprite.Height = h;
                sprite.Pivot = pivot;
                sprite.Border = border;
                sprite.SourceRect = source;
                p._sprites.Add(sprite);
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
