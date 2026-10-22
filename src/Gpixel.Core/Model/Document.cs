using System.Drawing;

namespace Gpixel.Core.Model;

/// <summary>문서에서 바뀐 부분(여러 개 조합 가능).</summary>
[Flags]
public enum DocumentChange
{
    None = 0,
    /// <summary>픽셀 내용이 바뀜(크기는 같음).</summary>
    Pixels = 1,
    /// <summary><see cref="Document.Image"/>가 다른 이미지(크기가 다를 수 있음)로 바뀜.</summary>
    Image = 2,
    Slices = 4,
    Animations = 8,
    SpriteSettings = 16,
    Grid = 32,
    /// <summary>파일 경로가 바뀜(다른 이름으로 저장 등).</summary>
    Path = 64,
    All = Pixels | Image | Slices | Animations | SpriteSettings | Grid | Path,
}

public sealed class DocumentChangedEventArgs(DocumentChange change) : EventArgs
{
    public DocumentChange Change { get; } = change;
}

/// <summary>
/// 편집 중인 문서: 이미지 + 경로 + 스프라이트 설정·칸·애니메이션·격자 + 수정 여부.
/// 컬렉션과 설정 객체는 직접 수정할 수 있으며, 수정한 뒤 <see cref="NotifyChanged"/>를 불러
/// 수정됨 표시와 이벤트를 발생시킨다.
/// </summary>
public sealed class Document
{
    /// <summary>경로가 없는 새 문서의 표시 이름.</summary>
    public static string UntitledName => Gpixel.Core.Localization.DomainStrings.Document_UntitledName;

    private PixelImage _image;
    private string? _path;
    private bool _isDirty;

    public Document(PixelImage image, string? path = null)
    {
        _image = image;
        _path = path;
    }

    /// <summary>투명한 새 이미지 문서.</summary>
    public static Document CreateNew(int width, int height, Color32 background = default)
    {
        var img = new PixelImage(width, height);
        if (background != default) img.Clear(background);
        return new Document(img);
    }

    /// <summary>내용이 바뀔 때마다 발생.</summary>
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary><see cref="IsDirty"/> 값이 바뀔 때 발생.</summary>
    public event EventHandler? DirtyChanged;

    public PixelImage Image
    {
        get => _image;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _image)) return;
            _image = value;
            NotifyChanged(DocumentChange.Image);
        }
    }

    /// <summary>PNG 파일 경로(새 문서면 null).</summary>
    public string? Path
    {
        get => _path;
        set
        {
            if (_path == value) return;
            _path = value;
            NotifyChanged(DocumentChange.Path, markDirty: false);
        }
    }

    public SpriteSettings Sprite { get; set; } = new();
    public List<Slice> Slices { get; } = new();
    public List<AnimationClip> Animations { get; } = new();
    public GridSettings Grid { get; set; } = new();

    public int Width => _image.Width;
    public int Height => _image.Height;

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

    /// <summary>파일 이름(확장자 포함). 새 문서면 <see cref="UntitledName"/>.</summary>
    public string DisplayName => _path is null ? UntitledName : System.IO.Path.GetFileName(_path);

    /// <summary>칸 이름 규칙의 기본 이름(확장자 뺀 파일 이름, Unity 기본 이름과 같음).</summary>
    public string BaseName => _path is null ? "sprite" : System.IO.Path.GetFileNameWithoutExtension(_path);

    /// <summary>변경을 알린다. 기본으로 수정됨 표시를 켠다.</summary>
    public void NotifyChanged(DocumentChange change, bool markDirty = true)
    {
        if (markDirty) IsDirty = true;
        Changed?.Invoke(this, new DocumentChangedEventArgs(change));
    }

    public Slice? FindSlice(string name) => Slices.Find(s => s.Name == name);

    public AnimationClip? FindAnimation(string name) => Animations.Find(a => a.Name == name);

    /// <summary>
    /// 프레임 칸 이름이 가리키는 영역과 피벗. Single 모드에서 같은 이름의 칸이 없으면
    /// 이미지 전체와 기본 피벗을 쓴다.
    /// </summary>
    public bool TryGetSliceRect(string name, out Rectangle rect, out Pivot pivot)
    {
        var slice = FindSlice(name);
        if (slice is not null)
        {
            rect = slice.Rect;
            pivot = slice.Pivot;
            return true;
        }
        if (Sprite.Mode == SpriteMode.Single && (name == BaseName || Slices.Count == 0))
        {
            rect = _image.Bounds;
            pivot = Sprite.DefaultPivot;
            return true;
        }
        rect = Rectangle.Empty;
        pivot = default;
        return false;
    }

    /// <summary>
    /// 칸 이름을 바꾸고 그 칸을 쓰는 애니메이션 프레임도 함께 바꾼다.
    /// 이름이 비었거나 이미 다른 칸이 쓰고 있으면 false.
    /// </summary>
    public bool RenameSlice(Slice slice, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return false;
        if (slice.Name == newName) return true;
        if (Slices.Any(s => !ReferenceEquals(s, slice) && s.Name == newName)) return false;
        var old = slice.Name;
        slice.Name = newName;
        bool animChanged = false;
        foreach (var frame in Animations.SelectMany(a => a.Frames))
        {
            if (frame.Slice != old) continue;
            frame.Slice = newName;
            animChanged = true;
        }
        NotifyChanged(animChanged ? DocumentChange.Slices | DocumentChange.Animations : DocumentChange.Slices);
        return true;
    }

    /// <summary>다른 칸과 겹치지 않는 이름(<c>base</c>, <c>base_1</c>, …).</summary>
    public string UniqueSliceName(string baseName)
    {
        if (FindSlice(baseName) is null) return baseName;
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (FindSlice(candidate) is null) return candidate;
        }
    }

    /// <summary>다른 애니메이션과 겹치지 않는 이름.</summary>
    public string UniqueAnimationName(string baseName)
    {
        if (FindAnimation(baseName) is null) return baseName;
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (FindAnimation(candidate) is null) return candidate;
        }
    }
}
