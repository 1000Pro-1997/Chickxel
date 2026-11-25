namespace Chickxel.App;

/// <summary>
/// 튜토리얼(<see cref="MainForm.StartTutorial"/> — 3D를 픽셀 아트로)이 비추는 컨트롤과 넘어갈 조건을 읽는 자리.
/// 단계와 대상의 목록은 저장소 루트 <c>튜토리얼.md</c>.
/// </summary>
public sealed partial class ModelImportDialog
{
    /// <summary>미리보기 아래 출력 묶음(크기·프레임).</summary>
    private readonly Control _outputBox;

    /// <summary>오른쪽 설정 묶음들(프리셋 · 카메라 · 빛 · 픽셀 다듬기 · 외곽선).</summary>
    private readonly Control _settingsGroups;

    /// <summary>예시 모델 파일을 넣는다('모델 열기…'·끌어 놓기와 같은 길).</summary>
    internal void TutorialLoad(string path) => LoadModel(path);

    /// <summary>원본 모델 · 픽셀 결과 두 칸.</summary>
    internal Control TutorialPreview => _preview;

    internal Control TutorialOutput => _outputBox;

    internal Control TutorialSettings => _settingsGroups;

    /// <summary>가져오기 단추.</summary>
    internal Control TutorialImport => OkButton;

    /// <summary>모델을 넣었는지.</summary>
    internal bool TutorialLoaded => ModelPath is not null;

    /// <summary>보는 각도(비교용 글 — 모델을 끌어 돌렸는지).</summary>
    internal string TutorialAngle => $"{_yaw.Value}|{_pitch.Value}|{_roll.Value}";

    /// <summary>결과 크기(바꿨는지).</summary>
    internal PixelSize TutorialSize => _size.Size;
}
