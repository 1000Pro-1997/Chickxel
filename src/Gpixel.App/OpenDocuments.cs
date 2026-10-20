using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>다시 열 때 되돌릴 보기 상태: 현재 레이어·프레임, 캔버스 확대율·위치.</summary>
internal readonly record struct DocumentView(int Layer, int Frame, int Zoom, Point Offset);

/// <summary>
/// 메모리에 남겨 둔 저장 안 한 문서: 프로젝트 인스턴스(수정 내용과 실행취소 기록 그대로), 연 파일, 보기 상태, 목록 썸네일 그림.
/// </summary>
internal sealed class OpenDocument(SpriteProject project, string? sourcePath, DocumentView view, PixelImage thumbnail)
{
    public SpriteProject Project { get; } = project;

    /// <summary>프로젝트 파일과 다른, 사용자가 연 파일(아직 <c>.gpixel</c>이 없는 PNG 가져오기). 없으면 null.</summary>
    public string? SourcePath { get; set; } = sourcePath;

    public DocumentView View { get; } = view;

    /// <summary>파일 목록에 보일 메모리 속 그림(첫 프레임 합성).</summary>
    public PixelImage Thumbnail { get; } = thumbnail;

    /// <summary>디스크 파일(프로젝트 파일, 없으면 연 PNG)의 마지막 수정 시각: 밖에서 바뀌었는지 알아볼 때 쓴다.</summary>
    public DateTime DiskStamp { get; set; }

    /// <summary>문서의 디스크 파일: 프로젝트 파일이 있으면 그것, 아니면 연 PNG(둘 다 없으면 프로젝트 경로).</summary>
    public string? DiskPath => Project.Path is { } p && File.Exists(p) ? p : SourcePath ?? Project.Path;

    /// <summary>이 문서를 가리키는 파일 경로들(프로젝트 경로, 연 파일).</summary>
    public IEnumerable<string> Paths => PathsOf(Project, SourcePath);

    public bool Matches(string path) => Paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>프로젝트를 가리키는 파일 경로들(프로젝트 경로, 연 파일 — 없는 것은 뺀다).</summary>
    public static IEnumerable<string> PathsOf(SpriteProject project, string? sourcePath)
    {
        if (project.Path is { } path) yield return path;
        if (sourcePath is not null && !string.Equals(sourcePath, project.Path, StringComparison.OrdinalIgnoreCase)) yield return sourcePath;
    }
}

/// <summary>
/// 다른 파일로 바꿀 때 메모리에 남겨 둔, 저장 안 한 변경이 있는 문서들(지금 열린 문서는 넣지 않는다).
/// 문서가 저장돼 수정됨 표시가 꺼지면 저절로 빠진다.
/// </summary>
internal sealed class OpenDocuments
{
    private readonly List<OpenDocument> _docs = new();

    /// <summary>목록이나 문서 경로가 바뀔 때.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<OpenDocument> Items => _docs;

    public int Count => _docs.Count;

    /// <summary>경로(프로젝트 파일 또는 연 PNG)로 찾는다.</summary>
    public OpenDocument? Find(string path) => _docs.Find(d => d.Matches(path));

    public void Add(OpenDocument doc)
    {
        if (_docs.Exists(d => ReferenceEquals(d.Project, doc.Project))) return;
        doc.Project.DirtyChanged += OnDirtyChanged;
        _docs.Add(doc);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(OpenDocument doc)
    {
        if (!_docs.Remove(doc)) return false;
        doc.Project.DirtyChanged -= OnDirtyChanged;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>문서 경로가 밖에서 바뀌었을 때(이름 바꾸기) 알린다.</summary>
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void OnDirtyChanged(object? sender, EventArgs e)
    {
        if (sender is SpriteProject { IsDirty: false } p && _docs.Find(d => ReferenceEquals(d.Project, p)) is { } doc) Remove(doc);
    }
}
