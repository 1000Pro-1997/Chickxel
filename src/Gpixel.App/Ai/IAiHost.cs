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
    /// 새 프로젝트를 새 탭으로 만든다(열린 문서는 다른 탭에 그대로 남는다).
    /// <paramref name="fileName"/>이 있으면 <see cref="CurrentFolder"/>에 저장한다(같은 이름 파일이 있으면 실패).
    /// </summary>
    bool CreateProject(int width, int height, string? fileName, Color32? background, out string message);

    /// <summary>열린 프로젝트를 지금 경로에 저장(+설정대로 시트 내보내기). 경로가 없으면 실패.</summary>
    bool SaveProject(out string message);

    /// <summary>.gpixel/PNG·그림·움직이는 그림·영상을 새 탭으로 연다(이미 열려 있으면 그 탭을 고른다 — 다른 문서는 다른 탭에 그대로 남음).</summary>
    bool OpenFile(string path, out string message);

    /// <summary>AI 참고 자료 보관소(처음 부를 때 연다). 폴더를 열 수 없으면 IO 예외.</summary>
    Gpixel.Core.References.ReferenceLibrary References { get; }

    /// <summary>앱 설정(AI 그리기 설정·Codex 경로·모델).</summary>
    AppSettings Settings { get; }

    /// <summary>
    /// 변환한 픽셀 아트 프레임을 넣는다: 열린 문서의 새 레이어(실행취소 한 단계, 프레임이 모자라면 더함) 또는 새 탭의 새 문서. 결과 문구를 돌려준다.
    /// </summary>
    bool InsertPixelArt(string name, IReadOnlyList<PixelImage> frames, int frameDurationMs, bool newDocument, out string message);

    /// <summary>AI 그림 픽셀로 정리 창을 이 원본(가로로 이은 <paramref name="sourceFrames"/>프레임)과 변환 설정으로 연다(다시 조정해 적용).</summary>
    void ShowPixelate(PixelateInput input, int sourceFrames, Gpixel.Core.Pixelate.PixelateSettings settings);
}
