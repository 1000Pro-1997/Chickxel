using Avalonia.VisualTree;

namespace Chickxel.App;

/// <summary>
/// 튜토리얼(<see cref="MainForm.StartTutorial"/> — 그림을 픽셀 아트로)이 비추는 컨트롤과 넘어갈 조건을 읽는 자리.
/// 단계와 대상의 목록은 저장소 루트 <c>튜토리얼.md</c>.
/// </summary>
public sealed partial class PixelateDialog
{
    /// <summary>설정 칸의 간편 설정 묶음(튜토리얼이 펼쳐 비춤).</summary>
    private readonly Expander _quickSection;

    /// <summary>첫 준비(격자 찾기)가 끝났고 최적화가 돌지 않는다 — 후보 칸 우클릭·정리하기를 쓸 수 있다.</summary>
    internal bool TutorialReady => _appliedFrames is not null && _optCts is null;

    /// <summary>후보 줄 위 자동 최적화 단추(도는 중에는 '취소', 우클릭 '모두 취소').</summary>
    internal Control TutorialOptimizeButton => _optRun;

    /// <summary>최적화(자동 최적화·프리셋 하나·정리하기)가 도는 중인지.</summary>
    internal bool TutorialRunning => _optCts is not null;

    /// <summary>왼쪽 설정 칸을 펼쳤는지.</summary>
    internal bool TutorialSettingsOpen => _settingsExpanded;

    /// <summary>끌어 놓는 자리: 원본이 없으면 가운데 안내 칸, 있으면 원본 그림 칸.</summary>
    internal Control TutorialSourceArea => _emptyState.IsVisible ? _emptyState : _sourceView;

    /// <summary>예시 그림 파일을 원본으로 넣는다(끌어 놓기·파일 찾기와 같은 길 — 튜토리얼이 창을 연 뒤 예시를 넣음).</summary>
    internal void TutorialLoad(string path)
    {
        if (_loadSource(this, path) is { } input) SetSource(input);
    }

    /// <summary>후보 칸 줄.</summary>
    internal Control TutorialCandidates => _tiles;

    /// <summary>설정 펼치기 단추.</summary>
    internal Control TutorialSettingsToggle => _settingsToggle;

    /// <summary>프리셋 줄 끝 재생 단추(정리하기).</summary>
    internal Control TutorialRunButton => _runButton;

    /// <summary>결과 팔레트 견본(색 빼기·원본에서 더하기).</summary>
    internal Control TutorialColors => _colors;

    /// <summary>결과를 넣는 단추.</summary>
    internal Button TutorialApplyButton => OkButton;

    /// <summary>지금 간편 설정 칸의 값과 설정 칸 전체 값(비교용 글 — 간편 설정을 바꿨는지). 결과 크기는 자동이 꺼졌을 때만(자동이면 결과 따라 바뀌는 회색 숫자).</summary>
    internal string TutorialSettingsText => string.Join("|",
        _quickAuto.IsChecked, _quickAuto.IsChecked == true ? "" : _quickSize.Size.ToString(), _grLock.IsChecked, _quickTargetColors.Value, _quickTargetAuto.IsChecked,
        _quickTargetStrict.IsChecked, _quickSampleFrames.Value, _quickBg.IsChecked, _quickShared.IsChecked, OptimizePreset.Serialize(ReadSettings()));

    /// <summary>뺀 색·원본에서 더한 색(비교용 글 — 결과 색을 바꿨는지).</summary>
    internal string TutorialPaletteText => string.Join(",", _excluded) + "|" + string.Join(",", _pinned);

    /// <summary>간편 설정 묶음을 펼치고 그 묶음이 보이게 설정 칸을 굴린 뒤 돌려준다.</summary>
    internal Control TutorialQuickSection()
    {
        if (!_quickSection.IsExpanded) _quickSection.IsExpanded = true;
        return _quickSection;
    }

    /// <summary>
    /// 아직 돌리지 않은 프리셋 칸: 기본 프리셋 <paramref name="builtIn"/>(예: <c>shade</c> = 같은 색상 계열)의 칸, 없으면 첫 칸. 칸이 없으면 null.
    /// </summary>
    internal (Control Tile, string Preset)? TutorialIdleTile(string builtIn)
    {
        var idle = _tiles.Children.OfType<PendingTile>().Where(t => t.Idle && t.IsVisible).ToList();
        if (idle.Count == 0) return null;
        var name = _opt.OptimizePresets.FirstOrDefault(p => p.BuiltIn == builtIn)?.Name;
        var tile = idle.FirstOrDefault(t => t.PresetName == name) ?? idle[0];
        return (tile, tile.PresetName);
    }

    /// <summary>대상이 설정 칸 안이면 그 자리가 보이게 굴린다.</summary>
    internal static void TutorialBringIntoView(Control target)
    {
        if (target.GetVisualAncestors().OfType<ScrollViewer>().Any()) target.BringIntoView();
    }
}
