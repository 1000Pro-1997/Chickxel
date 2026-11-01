using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>탭을 다시 보일 때 되돌릴 보기 상태: 현재 레이어·프레임, 캔버스 보기(확대율·화면 가운데의 그림 좌표 — 화면 크기와 무관).</summary>
internal readonly record struct DocumentView(int Layer, int Frame, CanvasView Canvas);

/// <summary>
/// 열린 문서 하나(탭 하나가 보여 줌 — 한 문서는 한 탭에서만 연다): 프로젝트 인스턴스(수정 내용과 실행취소 기록 그대로), 연 파일, 디스크 파일 시각, 목록 썸네일 그림.
/// </summary>
internal sealed class OpenDocument(SpriteProject project, string? sourcePath)
{
    /// <summary>문서의 프로젝트(되돌리기·밖에서 바뀐 파일 다시 읽기·프레임 나누기 때 새 인스턴스로 바뀐다).</summary>
    public SpriteProject Project { get; set; } = project;

    /// <summary>프로젝트 파일과 다른, 사용자가 연 파일(아직 <c>.chickxel</c>이 없는 PNG 가져오기). 없으면 null.</summary>
    public string? SourcePath { get; set; } = sourcePath;

    /// <summary>파일 목록에 보일 메모리 속 그림(저장 안 한 문서를 다른 탭으로 바꿀 때의 첫 프레임 합성). 저장하면 버린다.</summary>
    public PixelImage? Thumbnail { get; set; }

    /// <summary>디스크 파일(<see cref="DiskPath"/>)의 마지막 수정 시각: 밖에서 바뀌었는지 알아볼 때 쓴다.</summary>
    public DateTime DiskStamp { get; set; }

    /// <summary>보이지 않는 동안 디스크 파일이 밖에서 바뀌었다(수정이 없으면 다음에 보일 때 다시 읽는다).</summary>
    public bool ChangedOnDisk { get; set; }

    /// <summary>
    /// 저장하지 않는 문서(효과 만들기의 미리보기 장면 — 장면은 효과와 함께 저장됨)의 탭 제목. null이면 보통 문서.
    /// 이런 문서는 닫을 때·끝낼 때 저장을 묻지 않고, 탭에 수정됨 점을 보이지 않는다.
    /// </summary>
    public string? DetachedTitle { get; init; }

    /// <summary>저장하지 않는 문서인지(<see cref="DetachedTitle"/>).</summary>
    public bool IsDetached => DetachedTitle is not null;

    /// <summary>탭을 닫아 문서를 놓을 때 부른다(저장하지 않는 문서의 주인에게 알림). 없으면 null.</summary>
    public Action? Closed { get; init; }

    /// <summary>문서의 디스크 파일: 프로젝트 파일이 있으면 그것, 아니면 연 PNG(둘 다 없으면 프로젝트 경로).</summary>
    public string? DiskPath => Project.Path is { } p && File.Exists(p) ? p : SourcePath ?? Project.Path;

    /// <summary>
    /// 임시 제목(<see cref="SpriteProject.TempTitle"/>)을 붙일 수 있는지: 디스크 파일이 없는(저장한 적 없고 파일에서 열지도 않은) 보통 문서만.
    /// 저장하지 않는 문서(<see cref="IsDetached"/>)는 주인이 정한 제목을 쓴다.
    /// </summary>
    public bool CanHaveTempTitle => !IsDetached && DiskPath is null;

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
