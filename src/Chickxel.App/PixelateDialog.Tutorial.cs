using Avalonia.VisualTree;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 튜토리얼(<see cref="MainForm.StartTutorial"/> — 그림을 픽셀 아트로)이 비추는 컨트롤과 넘어갈 조건을 읽는 자리.
/// 단계와 대상의 목록은 저장소 루트 <c>튜토리얼.md</c>.
/// </summary>
public sealed partial class PixelateDialog
{
    /// <summary>설정 칸의 간편 설정 묶음(튜토리얼이 펼쳐 비춤).</summary>
    private readonly Expander _quickSection;

    /// <summary>후보 칸을 눌러 고른 횟수(튜토리얼이 칸을 눌렀는지 봄).</summary>
    private int _tutorialTileClicks;

    /// <summary>후보 칸을 눌러 고른 횟수.</summary>
    internal int TutorialTileClicks => _tutorialTileClicks;

    /// <summary>지금 고른 후보의 칸과 이름(돌린 프리셋 이름, 예: "기본_1"). 고른 후보가 없으면 null.</summary>
    internal (Control Tile, string Name)? TutorialSelectedTile
    {
        get
        {
            if (_selected is not { } c || _candidates.IndexOf(c) is var i && i < 0) return null;
            var tiles = _tiles.Children.OfType<CandidateTile>().ToList();
            return i < tiles.Count ? (tiles[i], _optimized.TryGetValue(c, out var o) ? o.Name : UiStrings.Pixelate_CurrentSettings) : null;
        }
    }

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

    /// <summary>프리셋 줄의 프리셋 목록 칸.</summary>
    internal ComboBox TutorialPresetBox => _preset;

    /// <summary>기본 설정 프리셋(<c>plain</c> — '기본')의 이름. 목록에서 지웠으면 null.</summary>
    internal string? TutorialDefaultPresetName => _opt.OptimizePresets.FirstOrDefault(p => p.BuiltIn == "plain")?.Name;

    /// <summary>기본 설정 프리셋을 골라 두었는지(목록에서 지웠으면 아무 프리셋이나 골랐는지).</summary>
    internal bool TutorialDefaultPresetChosen
        => TutorialDefaultPresetName is null ? _current is not null : _current?.BuiltIn == "plain";

    /// <summary>프리셋 줄 끝 재생 단추(정리하기).</summary>
    internal Control TutorialRunButton => _runButton;

    /// <summary>큰 미리보기(결과·원본 그림 — 누르면 크게 보기).</summary>
    internal Control TutorialPreview => _preview;

    /// <summary>크게 보기가 열렸는지.</summary>
    internal bool TutorialZoomOpen => _zoom.IsVisible;

    /// <summary>크게 보기를 닫는다(적용 단추가 보이게).</summary>
    internal void TutorialCloseZoom() => CloseZoom();

    /// <summary>결과 팔레트 견본(색 빼기·원본에서 더하기): 크게 보기가 열렸으면 그 안의 것.</summary>
    internal Control TutorialColors => _zoom.IsVisible ? _zoomColors : _colors;

    /// <summary>결과를 넣는 단추.</summary>
    internal Button TutorialApplyButton => OkButton;

    /// <summary>결과 크기가 고정이면 그 가로(간편 설정·4단계 칸 값), 자동이면 null.</summary>
    internal int? TutorialFixedWidth => _quickAuto.On() ? null : _szSize.Size.Width;

    /// <summary>뺀 색(비교용 글 — 색을 뺐는지).</summary>
    internal string TutorialExcludedText => string.Join(",", _excluded);

    /// <summary>원본에서 색 추가(스포이드)가 켜져 있는지(+ 칸을 누른 뒤 원본을 누르기 전).</summary>
    internal bool TutorialPicking => _picking;

    /// <summary>스포이드로 누를 원본 그림 자리: 크게 보기가 열렸으면 그 안의 오른쪽 원본 칸, 아니면 왼쪽 큰 원본 전체.</summary>
    internal (Control Owner, Rect Area) TutorialPickSource
        => _zoom.IsVisible ? (_zoom.View, _zoom.View.SourcePane) : (_sourceView, new Rect(_sourceView.Bounds.Size));

    /// <summary>원본에서 더한 색(비교용 글 — 색을 더했는지).</summary>
    internal string TutorialPinnedText => string.Join(",", _pinned);

    /// <summary>간편 설정 묶음을 펼치고 결과 크기 줄('자동' · 가로 × 세로)을 돌려준다.</summary>
    internal Control TutorialQuickSizeRow()
    {
        if (!_quickSection.IsExpanded) _quickSection.IsExpanded = true;
        return _quickSize.Parent as Control ?? _quickSection;
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
}
