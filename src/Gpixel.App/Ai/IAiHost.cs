using Gpixel.Core.Model;

namespace Gpixel.App.Ai;

/// <summary>
/// AI 도구가 편집기(메인 창)에 부탁하는 일. 모두 UI 스레드에서 불리고, 대화상자를 띄우지 않는다
/// (실패하면 false와 이유 문구를 돌려준다).
/// </summary>
public interface IAiHost
{
    EditorContext Context { get; }

    /// <summary>새 파일을 만들 폴더(파일 브라우저의 현재 폴더, 없으면 null).</summary>
    string? CurrentFolder { get; }

    /// <summary>팔레트 열의 활성 팔레트(마지막으로 누르거나 고친 팔레트) 색. <see cref="SetPalette"/>도 활성 팔레트를 바꾼다.</summary>
    IReadOnlyList<Color32> Palette { get; }

    void SetPalette(IReadOnlyList<Color32> colors);

    /// <summary>
    /// 새 프로젝트를 만든다. 열린 문서는 수정이 있어도 메모리에 남고, 저장한 적 없는 새 프로젝트에 수정이 있을 때만 실패한다.
    /// <paramref name="fileName"/>이 있으면 <see cref="CurrentFolder"/>에 저장한다(같은 이름 파일이 있으면 실패).
    /// </summary>
    bool CreateProject(int width, int height, string? fileName, Color32? background, out string message);

    /// <summary>열린 프로젝트를 지금 경로에 저장(+설정대로 시트 내보내기). 경로가 없으면 실패.</summary>
    bool SaveProject(out string message);

    /// <summary>.gpixel/PNG를 연다. 저장한 적 없는 새 프로젝트에 수정이 있을 때만 실패한다(다른 문서는 메모리에 남음).</summary>
    bool OpenFile(string path, out string message);

    /// <summary>AI 참고 자료 보관소(처음 부를 때 연다). 폴더를 열 수 없으면 IO 예외.</summary>
    Gpixel.Core.References.ReferenceLibrary References { get; }
}
