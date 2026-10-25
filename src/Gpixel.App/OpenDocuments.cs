using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>탭을 다시 보일 때 되돌릴 보기 상태: 현재 레이어·프레임, 캔버스 확대율·위치.</summary>
internal readonly record struct DocumentView(int Layer, int Frame, int Zoom, Point Offset);

/// <summary>
/// 열린 문서 하나(탭 하나 이상이 보여 줌): 프로젝트 인스턴스(수정 내용과 실행취소 기록 그대로), 연 파일, 디스크 파일 시각, 목록 썸네일 그림.
/// 같은 문서를 두 묶음(좌우 나눔)에서 함께 보면 탭 두 개가 이 인스턴스 하나를 같이 쓴다.
/// </summary>
internal sealed class OpenDocument(SpriteProject project, string? sourcePath)
{
    /// <summary>문서의 프로젝트(되돌리기·밖에서 바뀐 파일 다시 읽기·프레임 나누기 때 새 인스턴스로 바뀐다).</summary>
    public SpriteProject Project { get; set; } = project;

    /// <summary>프로젝트 파일과 다른, 사용자가 연 파일(아직 <c>.gpixel</c>이 없는 PNG 가져오기). 없으면 null.</summary>
    public string? SourcePath { get; set; } = sourcePath;

    /// <summary>파일 목록에 보일 메모리 속 그림(저장 안 한 문서를 다른 탭으로 바꿀 때의 첫 프레임 합성). 저장하면 버린다.</summary>
    public PixelImage? Thumbnail { get; set; }

    /// <summary>디스크 파일(<see cref="DiskPath"/>)의 마지막 수정 시각: 밖에서 바뀌었는지 알아볼 때 쓴다.</summary>
    public DateTime DiskStamp { get; set; }

    /// <summary>보이지 않는 동안 디스크 파일이 밖에서 바뀌었다(수정이 없으면 다음에 보일 때 다시 읽는다).</summary>
    public bool ChangedOnDisk { get; set; }

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
/// 편집기 탭 하나: 보여 주는 문서와 탭마다의 보기 상태(레이어·프레임·확대율·위치, 선택 영역).
/// 보기 상태는 탭을 떠날 때 저장하고 다시 고를 때 되돌린다(처음 보이는 탭은 null — 화면에 맞춤).
/// </summary>
internal sealed class DocumentTab(OpenDocument doc)
{
    public OpenDocument Doc { get; } = doc;

    public DocumentView? View { get; set; }

    /// <summary>탭을 떠날 때의 선택 영역(떠 있는 픽셀은 먼저 내려놓으므로 영역·모양만). 없으면 null.</summary>
    public SelectionState? Selection { get; set; }
}
